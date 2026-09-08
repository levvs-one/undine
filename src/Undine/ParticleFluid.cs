using System.Numerics;

namespace Undine;

/// <summary>
/// A liquid as particles in a box: drops that fall, splash and bead, sheets that break, viscous blobs that slump.
/// Position-based fluids (Macklin and Müller 2013) with the liquid's own density, viscosity and surface tension:
/// each substep predicts positions, then projects them so every particle's neighbourhood has the rest density,
/// then takes the velocity from the move; viscosity is the Monaghan SPH term with the liquid's ν, surface tension
/// the Akinci cohesion between pairs, attractive at the rest spacing and repulsive closer in, scaled by the liquid's σ. This is what the Splash page runs on
/// the GPU; the same scheme here on the CPU, for tools, tests and engines that want particles on the processor.
/// </summary>
public sealed class ParticleFluid
{
    private const float Gravity = 9.80665f;

    private readonly List<Vector3> positions = [];
    private readonly List<Vector3> velocities = [];
    private readonly List<Vector3> predicted = [];
    private readonly List<float> lambdas = [];
    private readonly List<float> inverses = [];
    private readonly List<Vector3> deltas = [];
    private readonly List<Vector3> normals = [];
    private readonly List<float> densities = [];
    private readonly Dictionary<long, List<int>> grid = [];
    private List<int>[] neighbourLists = [];

    /// <param name="liquid">The liquid: density, viscosity and surface tension.</param>
    /// <param name="spacingMetres">Rest distance between particles; the kernel reaches twice as far.</param>
    /// <param name="box">The box the liquid lives in: its extent from the origin, metres. Walls are no-slip-free and elastic-free.</param>
    public ParticleFluid(Liquid liquid, double spacingMetres, Vector3 box)
    {
        ArgumentNullException.ThrowIfNull(liquid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingMetres);
        Liquid = liquid;
        Spacing = (float)spacingMetres;
        Radius = 2f * Spacing;
        Box = box;
        Mass = (float)(liquid.DensityKgPerM3 * spacingMetres * spacingMetres * spacingMetres);
        RestDensity = (float)liquid.DensityKgPerM3;
        MaxStep = 0.2 * spacingMetres;
        // Kernel constants (Müller 2003), for the 3D poly6 and spiky kernels of radius h.
        float h = Radius;
        poly6 = 315f / (64f * MathF.PI * MathF.Pow(h, 9));
        spikyGrad = -45f / (MathF.PI * MathF.Pow(h, 6));
        // The rest density a regular lattice of particles at the spacing produces with these kernels; the constraint
        // is measured against it, so the rest state is exactly at rest and the constraint does not fight the lattice.
        latticeDensity = LatticeDensity();
        latticeGradient = LatticeGradient();
        // The density a wall hides: what the lattice beyond a plane at distance d would have added, tabulated over d in [0, h].
        for (int k = 0; k < wallDensity.Length; k++)
        {
            wallDensity[k] = MissingDensity(k * Radius / (wallDensity.Length - 1));
        }

        cohesionGamma = (float)(liquid.SurfaceTensionMNPerM * 1e-3 / (Math.PI / 8 * (double)RestDensity * RestDensity * CohesionIntegral()));
    }

    // ∫₀ʰ C(r)·r⁴ dr for Akinci's cohesion kernel: what turns a pairwise force into a surface tension.
    private double CohesionIntegral()
    {
        const int samples = 4000;
        double h = Radius, sum = 0;
        for (int k = 0; k < samples; k++)
        {
            double r = (k + 0.5) * h / samples;
            sum += CohesionKernel((float)r) * Math.Pow(r, 4) * h / samples;
        }

        return sum;
    }

    // Cohesion kernel of Akinci et al. 2013, integrating to one over the sphere of radius h.
    private float CohesionKernel(float r)
    {
        float h = Radius;
        if (r >= h || r <= 0f)
        {
            return 0f;
        }

        float scale = 32f / (MathF.PI * MathF.Pow(h, 9));
        float a = (h - r) * (h - r) * (h - r) * r * r * r;
        return r > h / 2 ? scale * a : scale * (2f * a - h * h * h * h * h * h / 64f);
    }

    private readonly float poly6, spikyGrad, latticeDensity, cohesionGamma, latticeGradient;
    private readonly float[] wallDensity = new float[33];

    /// <summary>The liquid.</summary>
    public Liquid Liquid { get; }

    /// <summary>Rest distance between particles, metres.</summary>
    public float Spacing { get; }

    /// <summary>Kernel radius, metres: twice the spacing.</summary>
    public float Radius { get; }

    /// <summary>The box the liquid lives in, metres from the origin corner.</summary>
    public Vector3 Box { get; }

    /// <summary>Mass of one particle, kg: the liquid's density over a spacing cube.</summary>
    public float Mass { get; }

    /// <summary>The liquid's density, kg/m³.</summary>
    public float RestDensity { get; }

    /// <summary>The density the kernels measure on a rest lattice, kg/m³: what the constraint holds every neighbourhood to.</summary>
    public float LatticeRestDensity => latticeDensity;

    /// <summary>Gravity as a vector, m/s²; set it to zero for a drop in free fall or in orbit.</summary>
    public Vector3 GravityVector { get; set; } = new(0f, -Gravity, 0f);

    /// <summary>Density-constraint iterations per substep.</summary>
    public int Iterations { get; set; } = 4;

    /// <summary>
    /// A multiplier on the cohesion strength, one by default. The strength itself comes from the liquid's σ through
    /// the pairwise-force relation σ = (π/8)·ρ²·∫C(r)·r⁴dr·γ (Tartakovsky and Panchenko 2016), so it is not tuned;
    /// the tests check the outcome on a stretched drop of every liquid against Rayleigh's time.
    /// </summary>
    public float TensionCalibration { get; set; } = 1f;

    /// <summary>Akinci's γ for this liquid at this spacing, from its σ: the pairwise cohesion per unit mass is −γ·m·C(r).</summary>
    public float Cohesion => cohesionGamma * TensionCalibration;

    /// <summary>
    /// The longest substep allowed whatever the other bounds say, s. By default a fifth of a millisecond per
    /// millimetre of spacing: the projection's corrections turn into velocity, and beyond this step that velocity
    /// feeds back faster than the smoothing takes it out; at this step a settled block stays settled and stands its
    /// true height.
    /// </summary>
    public double MaxStep { get; set; }

    /// <summary>XSPH velocity smoothing, the share of the neighbourhood's mean motion a particle takes on each substep.</summary>
    public float Smoothing { get; set; } = 0.1f;

    /// <summary>
    /// How much of Akinci's curvature term to apply beside the cohesion, 0 to 1. Off by default: with the normals a
    /// millimetre lattice gives, it throws surface particles off rather than smoothing the surface.
    /// </summary>
    public float CurvatureWeight { get; set; }

    /// <summary>The largest position correction of the last substep's last iteration, metres: how far the projection still moves particles.</summary>
    public float LastCorrection { get; private set; }

    /// <summary>The largest surface-tension acceleration of the last substep, m/s².</summary>
    public float LastTension { get; private set; }

    /// <summary>The largest viscous acceleration of the last substep, m/s².</summary>
    public float LastViscous { get; private set; }

    /// <summary>How many particles there are.</summary>
    public int Count => positions.Count;

    /// <summary>Positions, metres.</summary>
    public IReadOnlyList<Vector3> Positions => positions;

    /// <summary>Velocities, m/s.</summary>
    public IReadOnlyList<Vector3> Velocities => velocities;

    /// <summary>Total volume, m³: the particle count over the rest density.</summary>
    public double Volume => (double)Count * Mass / RestDensity;

    /// <summary>Fills a box with particles on a lattice at the rest spacing.</summary>
    public void FillBox(Vector3 from, Vector3 to, Vector3 velocity = default)
    {
        for (float z = from.Z + Spacing / 2; z < to.Z; z += Spacing)
        {
            for (float y = from.Y + Spacing / 2; y < to.Y; y += Spacing)
            {
                for (float x = from.X + Spacing / 2; x < to.X; x += Spacing)
                {
                    Add(new Vector3(x, y, z), velocity);
                }
            }
        }
    }

    /// <summary>Fills a sphere with particles on a lattice at the rest spacing.</summary>
    public void FillSphere(Vector3 centre, float radius, Vector3 velocity = default)
    {
        for (float z = -radius; z <= radius; z += Spacing)
        {
            for (float y = -radius; y <= radius; y += Spacing)
            {
                for (float x = -radius; x <= radius; x += Spacing)
                {
                    Vector3 offset = new(x, y, z);
                    if (offset.LengthSquared() <= radius * radius)
                    {
                        Add(centre + offset, velocity);
                    }
                }
            }
        }
    }

    /// <summary>Fills an ellipsoid with particles on a lattice at the rest spacing: a stretched drop at rest density.</summary>
    public void FillEllipsoid(Vector3 centre, Vector3 radii, Vector3 velocity = default)
    {
        for (float z = -radii.Z; z <= radii.Z; z += Spacing)
        {
            for (float y = -radii.Y; y <= radii.Y; y += Spacing)
            {
                for (float x = -radii.X; x <= radii.X; x += Spacing)
                {
                    Vector3 q = new(x / radii.X, y / radii.Y, z / radii.Z);
                    if (q.LengthSquared() <= 1f)
                    {
                        Add(centre + new Vector3(x, y, z), velocity);
                    }
                }
            }
        }
    }

    /// <summary>Adds one particle.</summary>
    public void Add(Vector3 position, Vector3 velocity = default)
    {
        positions.Add(position);
        velocities.Add(velocity);
        predicted.Add(position);
        lambdas.Add(0f);
        inverses.Add(0f);
        deltas.Add(Vector3.Zero);
        normals.Add(Vector3.Zero);
        densities.Add(RestDensity);
    }

    /// <summary>Moves one particle to a new position without changing its velocity.</summary>
    public void Nudge(int index, Vector3 position)
    {
        positions[index] = position;
        predicted[index] = position;
    }

    /// <summary>The substep that keeps the explicit viscosity and the projection well behaved at this spacing.</summary>
    public double StableStep()
    {
        double nu = Liquid.KinematicViscosity;
        double sigma = Liquid.SurfaceTensionMNPerM * 1e-3;
        double viscous = 0.1 * Spacing * Spacing / Math.Max(1e-12, nu);
        double courant = 0.4 * Spacing / Math.Max(0.5, FastestSpeed() + 0.5);
        // Brackbill's capillary bound √(ρ·Δx³/(2πσ)) with a margin: the pairwise cohesion is stiffer than the continuum it stands for.
        double capillary = 0.3 * Math.Sqrt(RestDensity * Math.Pow(Spacing, 3) / (2 * Math.PI * Math.Max(1e-6, sigma)));
        return Math.Min(Math.Min(Math.Min(viscous, courant), capillary), MaxStep);
    }

    /// <summary>Advances the liquid by <paramref name="seconds"/> in as many substeps as it needs.</summary>
    public void Step(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        double done = 0;
        while (done < seconds)
        {
            double dt = Math.Min(seconds - done, StableStep());
            Substep((float)dt);
            done += dt;
        }
    }

    private float FastestSpeed()
    {
        float fastest = 0f;
        foreach (Vector3 v in velocities)
        {
            fastest = MathF.Max(fastest, v.Length());
        }

        return fastest;
    }

    private void Substep(float dt)
    {
        int n = Count;
        // Forces first, on the neighbourhoods of the current positions: gravity, viscosity, cohesion. They move the
        // predicted positions, and the projection below answers them; a pull the projection cancels leaves no velocity
        // behind, which is what keeps a liquid at rest at rest.
        if (neighbourLists.Length < n || neighbourLists[n - 1] is null)
        {
            for (int i = 0; i < n; i++)
            {
                predicted[i] = positions[i];
            }

            BuildGrid();
        }

        ApplyViscosityAndTension(dt);
        Vector3 g = GravityVector;
        for (int i = 0; i < n; i++)
        {
            Vector3 v = velocities[i] + g * dt;
            velocities[i] = v;
            predicted[i] = Confine(positions[i] + v * dt);
        }

        BuildGrid();
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            ComputeLambdas();
            ComputeDeltas();
            float largest = 0f;
            for (int i = 0; i < n; i++)
            {
                largest = MathF.Max(largest, deltas[i].Length());
                predicted[i] = Confine(predicted[i] + deltas[i]);
            }

            LastCorrection = largest;
        }

        for (int i = 0; i < n; i++)
        {
            velocities[i] = (predicted[i] - positions[i]) / dt;
            positions[i] = predicted[i];
        }

        Smooth();
    }

    // XSPH: each particle takes on a little of its neighbourhood's mean motion, which takes the jitter out.
    private void Smooth()
    {
        int n = Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 vi = velocities[i];
            Vector3 blend = Vector3.Zero;
            foreach (int j in neighbourLists[i])
            {
                blend += (velocities[j] - vi) * (Mass / densities[j] * Poly6((positions[i] - positions[j]).LengthSquared()));
            }

            deltas[i] = vi + blend * Smoothing;
        }

        for (int i = 0; i < n; i++)
        {
            velocities[i] = deltas[i];
        }
    }

    private Vector3 Confine(Vector3 p)
    {
        float margin = Spacing * 0.5f;
        return new Vector3(
            Math.Clamp(p.X, margin, Box.X - margin),
            Math.Clamp(p.Y, margin, Box.Y - margin),
            Math.Clamp(p.Z, margin, Box.Z - margin));
    }

    private static long Key(int x, int y, int z) => ((long)x << 42) ^ ((long)(y & 0x1FFFFF) << 21) ^ (long)(z & 0x1FFFFF);

    private void BuildGrid()
    {
        grid.Clear();
        float cell = Radius;
        for (int i = 0; i < Count; i++)
        {
            Vector3 p = predicted[i];
            long key = Key((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
            if (!grid.TryGetValue(key, out List<int>? bucket))
            {
                bucket = [];
                grid[key] = bucket;
            }

            bucket.Add(i);
        }

        if (neighbourLists.Length < Count)
        {
            neighbourLists = new List<int>[Count];
        }

        float h2 = Radius * Radius;
        for (int i = 0; i < Count; i++)
        {
            List<int> list = neighbourLists[i] ??= [];
            list.Clear();
            Vector3 p = predicted[i];
            int cx = (int)MathF.Floor(p.X / cell), cy = (int)MathF.Floor(p.Y / cell), cz = (int)MathF.Floor(p.Z / cell);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!grid.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out List<int>? bucket))
                        {
                            continue;
                        }

                        foreach (int j in bucket)
                        {
                            if (j != i && (predicted[j] - p).LengthSquared() < h2)
                            {
                                list.Add(j);
                            }
                        }
                    }
                }
            }
        }
    }

    private float Poly6(float r2)
    {
        float h2 = Radius * Radius;
        if (r2 >= h2)
        {
            return 0f;
        }

        float d = h2 - r2;
        return poly6 * d * d * d;
    }

    private Vector3 SpikyGradient(Vector3 r)
    {
        float len = r.Length();
        if (len <= 1e-7f || len >= Radius)
        {
            return Vector3.Zero;
        }

        float d = Radius - len;
        return r * (spikyGrad * d * d / len);
    }

    private float LatticeDensity()
    {
        float sum = 0f;
        int reach = (int)MathF.Ceiling(Radius / Spacing);
        for (int z = -reach; z <= reach; z++)
        {
            for (int y = -reach; y <= reach; y++)
            {
                for (int x = -reach; x <= reach; x++)
                {
                    Vector3 r = new Vector3(x, y, z) * Spacing;
                    sum += Poly6(r.LengthSquared());
                }
            }
        }

        return Mass * sum;
    }

    // Σ|∇C|² a particle sees on the rest lattice: the scale the relaxation ε is set against.
    private float LatticeGradient()
    {
        float sum = 0f;
        int reach = (int)MathF.Ceiling(Radius / Spacing);
        for (int z = -reach; z <= reach; z++)
        {
            for (int y = -reach; y <= reach; y++)
            {
                for (int x = -reach; x <= reach; x++)
                {
                    if (x == 0 && y == 0 && z == 0)
                    {
                        continue;
                    }

                    sum += (SpikyGradient(new Vector3(x, y, z) * Spacing) * (Mass / latticeDensity)).LengthSquared();
                }
            }
        }

        return sum;
    }

    // The lattice's contribution to the density from beyond a plane at distance d below a particle.
    private float MissingDensity(float distance)
    {
        float sum = 0f;
        int reach = (int)MathF.Ceiling(Radius / Spacing) + 1;
        // The distance is measured from half a spacing off the wall, where the nearest particle sits; the lattice's
        // planes mirrored in the wall then lie a whole spacing, two spacings, … below that particle.
        for (int k = 0; k < reach; k++)
        {
            float depth = distance + (k + 1) * Spacing;
            for (int z = -reach; z <= reach; z++)
            {
                for (int x = -reach; x <= reach; x++)
                {
                    Vector3 r = new(x * Spacing, depth, z * Spacing);
                    sum += Poly6(r.LengthSquared());
                }
            }
        }

        return Mass * sum;
    }

    /// <summary>The density the walls hide from a particle at <paramref name="p"/>, kg/m³, which the constraint counts as present.</summary>
    public float WallDensityAt(Vector3 p) => WallDensity(p);

    // What the six walls hide from a particle's neighbourhood.
    private float WallDensity(Vector3 p)
    {
        // Each wall hides a share of the lattice; where walls meet, the shares overlap, so what is left is the product
        // of what each wall leaves.
        float remaining = 1f;
        float margin = Spacing * 0.5f;
        Span<float> distances = [p.X - margin, Box.X - margin - p.X, p.Y - margin, Box.Y - margin - p.Y, p.Z - margin, Box.Z - margin - p.Z];
        foreach (float d in distances)
        {
            if (d < Radius)
            {
                float t = MathF.Max(0f, d) / Radius * (wallDensity.Length - 1);
                int k = Math.Min(wallDensity.Length - 2, (int)t);
                float hidden = wallDensity[k] + (wallDensity[k + 1] - wallDensity[k]) * (t - k);
                remaining *= 1f - hidden / latticeDensity;
            }
        }

        return latticeDensity * (1f - remaining);
    }

    /// <summary>Under-relaxation of each Jacobi iteration's corrections, 0 to 1; a half keeps neighbours from over-correcting each other.</summary>
    public float Relaxation { get; set; } = 0.5f;

    private void ComputeLambdas()
    {
        float h = Radius;
        // The relaxation parameter of Macklin and Müller, the gradient scale of the rest lattice itself: it softens
        // the constraint so neighbours do not over-correct each other.
        float epsilon = latticeGradient;
        for (int i = 0; i < Count; i++)
        {
            Vector3 pi = predicted[i];
            float density = Mass * Poly6(0f) + WallDensity(pi);
            Vector3 gradI = Vector3.Zero;
            float sumGrad = 0f;
            foreach (int j in neighbourLists[i])
            {
                Vector3 r = pi - predicted[j];
                density += Mass * Poly6(r.LengthSquared());
                Vector3 gradJ = SpikyGradient(r) * (Mass / latticeDensity);
                gradI += gradJ;
                sumGrad += gradJ.LengthSquared();
            }

            sumGrad += gradI.LengthSquared();
            densities[i] = density;
            float constraint = density / latticeDensity - 1f;
            inverses[i] = 1f / (sumGrad + epsilon);
            // Both compression and rarefaction are corrected, as in the paper; the walls' hidden share of the lattice
            // is counted as present, so a particle against a wall is not pulled into it.
            lambdas[i] = -constraint * inverses[i];
        }
    }

    private void ComputeDeltas()
    {
        // The artificial pressure of Macklin and Müller against clumping: k = 0,1, n = 4, Δq = 0,2h, in λ's own units.
        float corrDenominator = Poly6(0.2f * Radius * 0.2f * Radius);
        for (int i = 0; i < Count; i++)
        {
            Vector3 pi = predicted[i];
            Vector3 delta = Vector3.Zero;
            foreach (int j in neighbourLists[i])
            {
                Vector3 r = pi - predicted[j];
                float ratio = Poly6(r.LengthSquared()) / corrDenominator;
                float sCorr = -0.1f * ratio * ratio * ratio * ratio * inverses[i];
                delta += SpikyGradient(r) * (lambdas[i] + lambdas[j] + sCorr);
            }

            deltas[i] = delta * (Mass / latticeDensity * Relaxation);
        }
    }

    // Viscosity as the Morris–Monaghan SPH Laplacian with the liquid's ν, and surface tension as Akinci's cohesion
    // between particle pairs plus the curvature term from the colour-field normals, both scaled by the liquid's σ.
    private void ApplyViscosityAndTension(float dt)
    {
        int n = Count;
        float nu = (float)Liquid.KinematicViscosity;
        float gamma = Cohesion;
        float h = Radius;
        for (int i = 0; i < n; i++)
        {
            Vector3 normal = Vector3.Zero;
            foreach (int j in neighbourLists[i])
            {
                normal += SpikyGradient(positions[i] - positions[j]) * (Mass / densities[j]);
            }

            // The kernel gradient points from a particle toward its neighbours, so the sum points into the liquid; the
            // normal is its opposite, outward, which is what makes the curvature force pull the surface together.
            normals[i] = -normal * h;
        }

        float largestTension = 0f, largestViscous = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 vi = velocities[i];
            Vector3 viscous = Vector3.Zero;
            Vector3 tension = Vector3.Zero;
            foreach (int j in neighbourLists[i])
            {
                Vector3 r = positions[i] - positions[j];
                float len = r.Length();
                if (len <= 1e-7f || len >= h)
                {
                    continue;
                }

                Vector3 vij = vi - velocities[j];
                Vector3 grad = SpikyGradient(r);
                // 2(d+2)ν Σ m/ρ (v·r)/(r²+0,01h²) ∇W, the standard SPH viscous acceleration in three dimensions.
                viscous += grad * (10f * nu * Mass / densities[j] * Vector3.Dot(vij, r) / (len * len + 0.01f * h * h));
                float kij = 2f * RestDensity / (densities[i] + densities[j]);
                // Akinci's forces per unit mass: cohesion −γ·m_j·C(r)·r̂ and curvature −γ·(n_i − n_j), with γ the
                // liquid's σ times the calibration below.
                Vector3 cohesion = -r / len * (gamma * Mass * CohesionKernel(len));
                Vector3 curvature = -(normals[i] - normals[j]) * (gamma * CurvatureWeight);
                tension += (cohesion + curvature) * kij;
            }

            largestTension = MathF.Max(largestTension, tension.Length());
            largestViscous = MathF.Max(largestViscous, viscous.Length());
            deltas[i] = (viscous + tension) * dt;
        }

        for (int i = 0; i < n; i++)
        {
            velocities[i] += deltas[i];
        }

        LastTension = largestTension;
        LastViscous = largestViscous;
    }
}
