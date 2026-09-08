namespace Undine;

/// <summary>
/// A liquid poured onto a floor and flowing over it: the shallow-water equations on a grid with a floor height,
/// a no-slip bottom (Poiseuille drag 3νu/h²) and a source where a spout lands. Thin films of honey creep and
/// thick sheets of water rush; wet and dry cells coexist, the liquid keeps its volume and a puddle at rest stays
/// at rest on any floor. This is what the Pour page runs on the GPU; the same scheme here on the CPU.
/// </summary>
/// <remarks>
/// State per cell: thickness h (metres) and discharge q = h·u (m²/s) in x and y. The hyperbolic part is a first-order
/// finite-volume step with the HLL flux and Audusse's hydrostatic reconstruction at the faces, which keeps h ≥ 0 and
/// a still surface still over a sloping floor; the grid's edges are walls. The drag is applied implicitly after it,
/// so honey is never stiff. Where the drag relaxes the flow faster than a step (τ = h²/(3ν) under dt) the mass flux
/// blends into the lubrication flux −g·h³·∇η/(3ν), Huppert's thin-film equation, so a creeping film creeps instead
/// of being carried by the Riemann solver's own diffusion.
/// Surface tension is not in the flow: a puddle's edge is a little sharper than real. The step obeys the CFL bound
/// 0.25·cell/max(|u| + √(g·h)), the two-dimensional one that keeps h ≥ 0; <see cref="Step"/> splits a longer dt as needed.
/// </remarks>
public sealed class ShallowFlow
{
    private const double Gravity = 9.80665;
    private const double Dry = 1e-7;
    private const float Thin = 1e-5f;

    private readonly float[] floor;
    private readonly float[] h, qx, qy;
    private readonly float[] nh, nqx, nqy;
    private readonly float[] source;

    /// <param name="cells">Cells per side; the grid is square.</param>
    /// <param name="sizeMetres">Physical side of the grid, metres.</param>
    /// <param name="liquid">The liquid: its kinematic viscosity sets the drag.</param>
    public ShallowFlow(int cells, double sizeMetres, Liquid liquid)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cells, 4);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeMetres);
        ArgumentNullException.ThrowIfNull(liquid);
        Cells = cells;
        SizeMetres = sizeMetres;
        Liquid = liquid;
        int count = cells * cells;
        floor = new float[count];
        h = new float[count];
        qx = new float[count];
        qy = new float[count];
        nh = new float[count];
        nqx = new float[count];
        nqy = new float[count];
        source = new float[count];
    }

    /// <summary>Cells per side.</summary>
    public int Cells { get; }

    /// <summary>Physical side of the grid, metres.</summary>
    public double SizeMetres { get; }

    /// <summary>The liquid.</summary>
    public Liquid Liquid { get; }

    /// <summary>Cell size, metres.</summary>
    public double CellMetres => SizeMetres / Cells;

    /// <summary>Thickness of the liquid at a cell, metres.</summary>
    public float ThicknessAt(int x, int y) => h[Index(x, y)];

    /// <summary>All thicknesses, row-major, metres.</summary>
    public ReadOnlySpan<float> Thickness => h;

    /// <summary>Floor height at a cell, metres.</summary>
    public float FloorAt(int x, int y) => floor[Index(x, y)];

    /// <summary>Velocity of the liquid at a cell, m/s; zero where dry.</summary>
    public (double X, double Y) VelocityAt(int x, int y)
    {
        int i = Index(x, y);
        return h[i] > Dry ? (qx[i] / h[i], qy[i] / h[i]) : (0, 0);
    }

    /// <summary>Total volume of liquid on the grid, m³.</summary>
    public double Volume()
    {
        double sum = 0;
        foreach (float t in h)
        {
            sum += t;
        }

        return sum * CellMetres * CellMetres;
    }

    /// <summary>Sets the floor from a function of position in metres.</summary>
    public void ShapeFloor(Func<double, double, double> heightMetres)
    {
        ArgumentNullException.ThrowIfNull(heightMetres);
        double cell = CellMetres;
        for (int y = 0; y < Cells; y++)
        {
            for (int x = 0; x < Cells; x++)
            {
                floor[Index(x, y)] = (float)heightMetres((x + 0.5) * cell, (y + 0.5) * cell);
            }
        }
    }

    /// <summary>Puts a still cylinder of liquid down: thickness over a disc, no motion.</summary>
    public void Place(double xMetres, double yMetres, double radiusMetres, double thicknessMetres)
    {
        double cell = CellMetres;
        for (int y = 0; y < Cells; y++)
        {
            for (int x = 0; x < Cells; x++)
            {
                double dx = (x + 0.5) * cell - xMetres, dy = (y + 0.5) * cell - yMetres;
                if (dx * dx + dy * dy <= radiusMetres * radiusMetres)
                {
                    h[Index(x, y)] += (float)thicknessMetres;
                }
            }
        }
    }

    /// <summary>A spout: liquid arriving at <paramref name="rateCubicMetresPerSecond"/> over a disc, spread evenly; zero rate stops it.</summary>
    public void Pour(double xMetres, double yMetres, double radiusMetres, double rateCubicMetresPerSecond)
    {
        Array.Clear(source);
        if (rateCubicMetresPerSecond <= 0)
        {
            return;
        }

        double cell = CellMetres;
        int count = 0;
        for (int y = 0; y < Cells; y++)
        {
            for (int x = 0; x < Cells; x++)
            {
                double dx = (x + 0.5) * cell - xMetres, dy = (y + 0.5) * cell - yMetres;
                if (dx * dx + dy * dy <= radiusMetres * radiusMetres)
                {
                    source[Index(x, y)] = 1f;
                    count++;
                }
            }
        }

        if (count == 0)
        {
            int cx = Math.Clamp((int)(xMetres / cell), 0, Cells - 1), cy = Math.Clamp((int)(yMetres / cell), 0, Cells - 1);
            source[Index(cx, cy)] = 1f;
            count = 1;
        }

        float perCell = (float)(rateCubicMetresPerSecond / (count * cell * cell));
        for (int i = 0; i < source.Length; i++)
        {
            source[i] *= perCell;
        }
    }

    /// <summary>The largest step the explicit part allows right now, s.</summary>
    public double StableStep()
    {
        double fastest = 1e-9;
        for (int i = 0; i < h.Length; i++)
        {
            if (h[i] > Dry)
            {
                double t2 = (double)h[i] * h[i];
                double u = Math.Sqrt((double)qx[i] * qx[i] + (double)qy[i] * qy[i]) * 2 * h[i] / (t2 + Math.Max(t2, (double)Thin * Thin));
                fastest = Math.Max(fastest, u + Math.Sqrt(Gravity * h[i]));
            }
        }

        return 0.25 * CellMetres / fastest;
    }

    /// <summary>Advances the flow by <paramref name="seconds"/>, in as many substeps as the CFL bound requires.</summary>
    public void Step(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        double done = 0;
        while (done < seconds)
        {
            double dt = Math.Min(seconds - done, StableStep());
            Substep(dt);
            done += dt;
        }
    }

    private void Substep(double dt)
    {
        int n = Cells;
        float cell = (float)CellMetres;
        float g = (float)Gravity;
        Array.Copy(h, nh, h.Length);
        Array.Copy(qx, nqx, qx.Length);
        Array.Copy(qy, nqy, qy.Length);
        float k = (float)(dt / cell);

        // Faces in x, then in y: the flux through each face leaves one cell and enters the next.
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n - 1; x++)
            {
                int l = Index(x, y), r = Index(x + 1, y);
                Face(l, r, qx, qy, g, (float)dt, out float fh, out float fqn, out float fqt, out float sl, out float sr);
                nh[l] -= k * fh;
                nh[r] += k * fh;
                nqx[l] -= k * (fqn - sl);
                nqx[r] += k * (fqn - sr);
                nqy[l] -= k * fqt;
                nqy[r] += k * fqt;
            }
        }

        for (int y = 0; y < n - 1; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int d = Index(x, y), u = Index(x, y + 1);
                Face(d, u, qy, qx, g, (float)dt, out float fh, out float fqn, out float fqt, out float sd, out float su);
                nh[d] -= k * fh;
                nh[u] += k * fh;
                nqy[d] -= k * (fqn - sd);
                nqy[u] += k * (fqn - su);
                nqx[d] -= k * fqt;
                nqx[u] += k * fqt;
            }
        }

        // The grid's edges are walls: no liquid crosses them, but their pressure 0,5·g·h² holds the liquid against them.
        for (int y = 0; y < n; y++)
        {
            int l = Index(0, y), r = Index(n - 1, y);
            nqx[l] += k * 0.5f * g * h[l] * h[l];
            nqx[r] -= k * 0.5f * g * h[r] * h[r];
        }

        for (int x = 0; x < n; x++)
        {
            int d = Index(x, 0), u = Index(x, n - 1);
            nqy[d] += k * 0.5f * g * h[d] * h[d];
            nqy[u] -= k * 0.5f * g * h[u] * h[u];
        }

        // The spout, then the bottom's drag on what moves: implicit, so a thin film of honey simply stops.
        float nu = (float)Liquid.KinematicViscosity;
        for (int i = 0; i < nh.Length; i++)
        {
            nh[i] += source[i] * (float)dt;
            if (nh[i] <= Dry)
            {
                nh[i] = Math.Max(0f, nh[i]);
                nqx[i] = 0f;
                nqy[i] = 0f;
                continue;
            }

            float drag = 1f / (1f + 3f * nu * (float)dt / (nh[i] * nh[i]));
            // Velocity from discharge without blowing up on a film a few microns thick (Kurganov and Petrova).
            float t = nh[i], t2 = t * t;
            float inv = 2f * t / (t2 + Math.Max(t2, Thin * Thin));
            float ux = nqx[i] * inv * drag, uy = nqy[i] * inv * drag;
            nqx[i] = t * ux;
            nqy[i] = t * uy;
        }

        Array.Copy(nh, h, h.Length);
        Array.Copy(nqx, qx, qx.Length);
        Array.Copy(nqy, qy, qy.Length);
    }

    // The HLL flux between two cells across a face, with the hydrostatic reconstruction so the floor's step is felt as
    // pressure and a level surface pushes nothing. qn is the discharge normal to the face, qt along it. The wave speeds
    // follow Toro, with the dry-bed estimates when one side is empty.
    private void Face(int a, int b, float[] qn, float[] qt, float g, float dt, out float fh, out float fqn, out float fqt, out float sa, out float sb)
    {
        float ha = h[a], hb = h[b];
        float ba = floor[a], bb = floor[b];
        float bf = Math.Max(ba, bb);
        float hl = Math.Max(0f, ha + ba - bf), hr = Math.Max(0f, hb + bb - bf);
        float ul = ha > Dry ? qn[a] / ha : 0f, ur = hb > Dry ? qn[b] / hb : 0f;
        float vl = ha > Dry ? qt[a] / ha : 0f, vr = hb > Dry ? qt[b] / hb : 0f;
        sa = 0.5f * g * (hl * hl - ha * ha);
        sb = 0.5f * g * (hr * hr - hb * hb);
        if (hl <= Dry && hr <= Dry)
        {
            fh = fqn = fqt = 0f;
            return;
        }

        float cl = MathF.Sqrt(g * hl), cr = MathF.Sqrt(g * hr);
        float sl, sr;
        if (hl <= Dry)
        {
            sl = ur - 2f * cr;
            sr = ur + cr;
        }
        else if (hr <= Dry)
        {
            sl = ul - cl;
            sr = ul + 2f * cl;
        }
        else
        {
            sl = Math.Min(ul - cl, ur - cr);
            sr = Math.Max(ul + cl, ur + cr);
        }

        float fhl = hl * ul, fhr = hr * ur;
        float fql = hl * ul * ul + 0.5f * g * hl * hl, fqr = hr * ur * ur + 0.5f * g * hr * hr;
        float ftl = hl * ul * vl, ftr = hr * ur * vr;
        if (sl >= 0f)
        {
            fh = fhl;
            fqn = fql;
            fqt = ftl;
        }
        else if (sr <= 0f)
        {
            fh = fhr;
            fqn = fqr;
            fqt = ftr;
        }
        else
        {
            float span = sr - sl;
            fh = (sr * fhl - sl * fhr + sl * sr * (hr - hl)) / span;
            fqn = (sr * fql - sl * fqr + sl * sr * (hr * ur - hl * ul)) / span;
            fqt = (sr * ftl - sl * ftr + sl * sr * (hr * vr - hl * vl)) / span;
        }

        // The creeping regime: when the bottom's drag relaxes the film within a step, the flow is the thin-film
        // equation and the mass flux is its lubrication flux from the surface slope, taken from the higher side.
        float nu = (float)Liquid.KinematicViscosity;
        float hf = Math.Max(hl, hr);
        float relax = hf * hf / (3f * nu);
        float inertial = relax / (relax + dt);
        if (inertial < 0.999f)
        {
            float etaL = ha + ba, etaR = hb + bb;
            float hup = etaL > etaR ? hl : hr;
            float creep = -g * hup * hup * hup / (3f * nu) * (etaR - etaL) / (float)CellMetres;
            // No face may drain more than a quarter of its donor cell in one step: four faces, and h stays ≥ 0.
            float most = 0.25f * hup * (float)CellMetres / dt;
            creep = Math.Clamp(creep, -most, most);
            fh = inertial * fh + (1f - inertial) * creep;
            fqn = inertial * fqn + (1f - inertial) * 0.25f * g * (hl * hl + hr * hr);
            fqt *= inertial;
        }
    }

    private int Index(int x, int y) => y * Cells + x;
}
