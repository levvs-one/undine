namespace Undine;

/// <summary>
/// A liquid poured onto a floor and flowing over it: the shallow-water equations on a grid with a floor height,
/// a no-slip bottom (Poiseuille drag 3νu/h²) and a source where a spout lands. Thin films of honey creep and
/// thick sheets of water rush; wet and dry cells coexist, the liquid keeps its volume and a puddle at rest stays
/// at rest on any floor. This is what the Pour page runs on the GPU; the same scheme here on the CPU.
/// </summary>
/// <remarks>
/// State per cell: thickness h (metres) and discharge q = h·u (m²/s) in x and y. The hyperbolic part is a
/// finite-volume step with the HLL flux and Audusse's hydrostatic reconstruction at the faces, second order in space
/// (each side's state at a face comes from its cell's minmod-limited slope of h, the surface η and the velocities,
/// so a sheet spreads the same in every direction instead of faster along the grid's axes), which keeps h ≥ 0 and
/// a still surface still over a sloping floor; the grid's edges are walls. The drag is applied implicitly after it,
/// so honey is never stiff. Where the drag relaxes the flow faster than a step (τ = h²/(3ν) under dt) the mass flux
/// blends into the lubrication flux −g·h³·∇η/(3ν), Huppert's thin-film equation, so a creeping film creeps instead
/// of being carried by the Riemann solver's own diffusion.
/// Surface tension enters at the contact line: a wet-dry front advances only when what pushes it, the edge's weight
/// ½ρgh², what the floor's fall adds, and the momentum flux ρhu² of a sheet running at the line, beats the retention
/// σ(1 − cos θ) of the contact angle θ; so a poured splat spreads evenly from where it lands, and a puddle stops at
/// the thickness 2·l_c·sin(θ/2) with l_c = √(σ/ρg) instead of thinning without end; a liquid that wets (small θ)
/// spreads to a film. The line holds a bulge of the front harder than a straight stretch and gives way in a notch
/// (the wet share of a disc across the face), which draws a puddle round, and the floor holds it unevenly from spot
/// to spot by ±20% (contact-angle hysteresis), so it is never a circle. A held line is not a wall: it pulls on the
/// edge with the Young force σ(1 − cos θ), so an edge thinner than the puddle's thickness is drawn back into it,
/// which is how a splashed sheet gathers itself up again (the Taylor–Culick balance ρhv² = σ(1 − cos θ)). Surface tension also
/// acts inside the liquid as the capillary pressure −σ∇²η, so a poured pool rings with capillary ripples; the
/// meniscus's own curvature at the edge is not in the flow. The step obeys the CFL bound
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
    private double spoutX, spoutY, spoutRadius, sheetSpeed;

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
        ContactAngleDegrees = DefaultContactAngle(liquid);
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

    /// <summary>
    /// Contact angle of the liquid on the floor, degrees. Not a property of the liquid alone but of the pair; the
    /// default follows the surface tension, from about 90° for water on a waxed table to a few degrees for the
    /// solvents that wet everything. Set it for the surface at hand.
    /// </summary>
    public double ContactAngleDegrees { get; set; }

    /// <summary>The thickness a puddle of this liquid settles to on a level floor, metres: 2·l_c·sin(θ/2).</summary>
    public double PuddleThickness => 2 * CapillaryLength * Math.Sin(ContactAngleDegrees * Math.PI / 360);

    /// <summary>The capillary length √(σ/(ρg)), metres.</summary>
    public double CapillaryLength => Math.Sqrt(Liquid.SurfaceTensionMNPerM * 1e-3 / (Liquid.DensityKgPerM3 * Gravity));

    /// <summary>A contact angle from the surface tension alone: liquids near water bead on a table, solvents wet it.</summary>
    public static double DefaultContactAngle(Liquid liquid)
    {
        ArgumentNullException.ThrowIfNull(liquid);
        return Math.Clamp(90 * (liquid.SurfaceTensionMNPerM - 20) / (72.8 - 20), 5, 90);
    }

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
    public void Pour(double xMetres, double yMetres, double radiusMetres, double rateCubicMetresPerSecond) =>
        Pour(xMetres, yMetres, radiusMetres, rateCubicMetresPerSecond, 0);

    /// <summary>
    /// Pours as above, with the jet landing at the given speed: where it lands, over twice its radius, the flow is
    /// Watson's stagnation flow, radial and rising from rest at the centre to the jet's speed at the jet's edge. A
    /// jet's momentum turns sideways on the floor, which is what drives the thin fast sheet and the hydraulic jump
    /// around the point of impact; the mass still arrives through the spout alone.
    /// </summary>
    public void Pour(double xMetres, double yMetres, double radiusMetres, double rateCubicMetresPerSecond, double speedMetresPerSecond)
    {
        Array.Clear(source);
        spoutX = xMetres;
        spoutY = yMetres;
        spoutRadius = radiusMetres;
        sheetSpeed = rateCubicMetresPerSecond > 0 ? Math.Max(0, speedMetresPerSecond) : 0;
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

        // Never longer than the bound of a layer one cell thick, so on a dry floor the spout's liquid still arrives in
        // small portions instead of a whole step's worth in one column; nor than the period of the shortest capillary
        // ripple the grid holds, ω² = σk³/ρ at k = π/cell, which the explicit capillary term needs.
        double bound = Math.Min(0.25 * CellMetres / fastest, 0.25 * CellMetres / Math.Sqrt(Gravity * CellMetres));
        double tension = Liquid.SurfaceTensionMNPerM * 1e-3 / Liquid.DensityKgPerM3;
        if (tension > 0)
        {
            bound = Math.Min(bound, Math.Sqrt(CellMetres * CellMetres * CellMetres / (Math.PI * Math.PI * Math.PI * tension)));
        }

        return bound;
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

        // Faces in x, then in y: the flux through each face leaves one cell and enters the next. Between its two
        // faces each cell also feels its own floor's slope, −g·h̄·(b⁺ − b⁻) over its reconstructed edges (Audusse's
        // centred source), which at second order is what keeps a level surface still on a sloping floor.
        for (int y = 0; y < n; y++)
        {
            int first = Index(0, y);
            float leftH = h[first], leftB = floor[first];
            for (int x = 0; x < n - 1; x++)
            {
                int l = Index(x, y), r = Index(x + 1, y);
                Face(l, r, 1, qx, qy, g, (float)dt, out float fh, out float fqn, out float fqt, out float sl, out float sr, out float ha, out float ba, out float hb, out float bb);
                nh[l] -= k * fh;
                nh[r] += k * fh;
                nqx[l] -= k * (fqn - sl) + k * g * 0.5f * (leftH + ha) * (ba - leftB);
                nqx[r] += k * (fqn - sr);
                nqy[l] -= k * fqt;
                nqy[r] += k * fqt;
                leftH = hb;
                leftB = bb;
            }

            int last = Index(n - 1, y);
            nqx[last] -= k * g * 0.5f * (leftH + h[last]) * (floor[last] - leftB);
        }

        for (int x = 0; x < n; x++)
        {
            int first = Index(x, 0);
            float downH = h[first], downB = floor[first];
            for (int y = 0; y < n - 1; y++)
            {
                int d = Index(x, y), u = Index(x, y + 1);
                Face(d, u, n, qy, qx, g, (float)dt, out float fh, out float fqn, out float fqt, out float sd, out float su, out float hd, out float bd, out float hu, out float bu);
                nh[d] -= k * fh;
                nh[u] += k * fh;
                nqy[d] -= k * (fqn - sd) + k * g * 0.5f * (downH + hd) * (bd - downB);
                nqy[u] += k * (fqn - su);
                nqx[d] -= k * fqt;
                nqx[u] += k * fqt;
                downH = hu;
                downB = bu;
            }

            int last = Index(x, n - 1);
            nqy[last] -= k * g * 0.5f * (downH + h[last]) * (floor[last] - downB);
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

        // The capillary pressure −σ∇²η: the liquid is pushed from where its surface is convex to where it is concave,
        // q += dt·h·(σ/ρ)·∇(∇²η), explicit, on cells whose whole stencil is wet (at an edge the surface's curvature is
        // the meniscus, which the contact line handles).
        float tension = (float)(Liquid.SurfaceTensionMNPerM * 1e-3 / Liquid.DensityKgPerM3);
        if (tension > 0f)
        {
            for (int y = 1; y < n - 1; y++)
            {
                for (int x = 1; x < n - 1; x++)
                {
                    int i = Index(x, y);
                    if (h[i] <= Dry)
                    {
                        continue;
                    }

                    float push = (float)dt * h[i] * tension / (2f * cell);
                    if (Laplacian(x - 1, y, out float lapL) && Laplacian(x + 1, y, out float lapR))
                    {
                        nqx[i] += push * (lapR - lapL);
                    }

                    if (Laplacian(x, y - 1, out float lapD) && Laplacian(x, y + 1, out float lapU))
                    {
                        nqy[i] += push * (lapU - lapD);
                    }
                }
            }
        }

        // The spout, then the bottom's drag on what moves: implicit, so a thin film of honey simply stops.
        float nu = (float)Liquid.KinematicViscosity;
        float zone = (float)(2 * spoutRadius);
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
            if (sheetSpeed > 0)
            {
                // The impact zone: Watson's stagnation flow, radial, from rest at the centre to the jet's speed at
                // its edge, prescribed rather than pushed, so the sheet leaves the zone the same in every direction.
                float dx = (float)((i % Cells + 0.5) * cell - spoutX), dy = (float)((i / Cells + 0.5) * cell - spoutY);
                float r = MathF.Sqrt(dx * dx + dy * dy);
                if (r < zone && r > 1e-9f)
                {
                    float speed = (float)sheetSpeed * Math.Min(1f, r / (float)spoutRadius);
                    ux = speed * dx / r;
                    uy = speed * dy / r;
                }
            }

            nqx[i] = t * ux;
            nqy[i] = t * uy;
        }

        Array.Copy(nh, h, h.Length);
        Array.Copy(nqx, qx, qx.Length);
        Array.Copy(nqy, qy, qy.Length);
    }

    // The HLL flux between two cells across a face, with the hydrostatic reconstruction so the floor's step is felt as
    // pressure and a level surface pushes nothing. qn is the discharge normal to the face, qt along it; step is the
    // index distance between a and b (1 across x, Cells across y). The wave speeds follow Toro, with the dry-bed
    // estimates when one side is empty. The source terms sa, sb are the pressure each side's floor step takes off
    // its cell, ½g(h*² − h²) with h the side's own edge value; the edge values ha, ba, hb, bb come out for the
    // cells' own centred sources.
    private void Face(int a, int b, int step, float[] qn, float[] qt, float g, float dt, out float fh, out float fqn, out float fqt, out float sa, out float sb, out float ha, out float ba, out float hb, out float bb)
    {
        int along = step == 1 ? a % Cells : a / Cells;
        int prev = along > 0 ? a - step : a, next = along + 1 < Cells - 1 ? b + step : b;
        Edge(prev, a, b, 1f, qn, qt, out ha, out ba, out float ul, out float vl);
        Edge(a, b, next, -1f, qn, qt, out hb, out bb, out float ur, out float vr);
        float bf = Math.Max(ba, bb);
        float hl = Math.Max(0f, ha + ba - bf), hr = Math.Max(0f, hb + bb - bf);
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

        // The contact line: a wet-dry front holds until the head behind it beats the retention of the contact angle.
        if ((hr <= Dry) != (hl <= Dry))
        {
            bool leftWet = hl > Dry;
            float thickness = leftWet ? hl : hr;
            float fall = leftWet ? Math.Max(0f, ba - bb) : Math.Max(0f, bb - ba);
            // What pushes the line outwards: the weight of the edge, the fall of the floor, and the momentum flux
            // h·u² of a sheet running at the line, which is how a poured splat spreads evenly from where it lands.
            // The sheet runs at the line, not at the grid's face: its whole speed counts once it heads within 30°
            // of the face's normal, so a front running diagonally is pushed as hard as one running along an axis.
            float un = leftWet ? ul : -ur, ut = leftWet ? vl : vr;
            float speed2 = un * un + ut * ut;
            float heading = speed2 > 0f ? Math.Clamp(2f * un / (float)Math.Sqrt(speed2), 0f, 1f) : 0f;
            float head = 0.5f * g * thickness * thickness + g * thickness * fall + thickness * speed2 * heading;
            // The line's own curvature: a disc across the face is half wet at a straight stretch of the front, less
            // at a bulge, more in a notch. The line holds a bulge harder and gives way in a notch, which is what
            // draws a poured shape into a round puddle; measured on a disc it is the same in every direction.
            float curvature = Math.Clamp(1f + 8f * (0.5f - WetShare(a, b)), 0.2f, 4f);
            // A real table holds the line unevenly from spot to spot (contact-angle hysteresis); a fixed pattern of
            // ±20% over the dry cell keeps a front from marching as a lattice.
            float grain = 0.8f + 0.4f * Hash(leftWet ? b : a);
            float retention = (float)(Liquid.SurfaceTensionMNPerM * 1e-3 / Liquid.DensityKgPerM3 * (1 - Math.Cos(ContactAngleDegrees * Math.PI / 180))) * curvature * grain;
            if (head < retention)
            {
                // The held line is not a wall: it pulls on the edge with the Young force, so the face carries the
                // retention as its momentum flux. An edge thinner than the puddle thickness is drawn back by the
                // unbalanced part, which is how a film dewets from its edge and a splashed sheet gathers up; one at
                // the thickness feels nothing. A cell on a diagonal stretch of the front has two such faces, and
                // holds √2 of front, so it is not scaled: measured, the puddle then grows near round (a tenth
                // shorter along the diagonals), where a scaling by the face's angle to the front made it square.
                fh = 0f;
                fqt = 0f;
                fqn = retention;
                return;
            }
        }

        // The creeping regime: when the bottom's drag relaxes the film within a step, the flow is the thin-film
        // equation and the mass flux is its lubrication flux from the surface slope, taken from the higher side.
        float nu = (float)Liquid.KinematicViscosity;
        float hf = Math.Max(hl, hr);
        float relax = hf * hf / (3f * nu);
        float inertial = relax / (relax + dt);
        if (inertial < 0.999f)
        {
            float etaL = h[a] + floor[a], etaR = h[b] + floor[b];
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

    // A cell's state at one of its faces (side +1 towards the higher cell, −1 towards the lower) from minmod-limited
    // slopes of h, the surface η and the velocities, between its neighbours prev and next (the cell itself where the
    // grid ends, which makes the slope zero); the floor at the face is η − h. Beside a dry cell the slopes are
    // dropped, as wet-dry schemes do: a slope towards a dry cell would halve the edge's thickness at the face.
    private void Edge(int prev, int i, int next, float side, float[] qn, float[] qt, out float he, out float be, out float ue, out float ve)
    {
        float hi = h[i], etai = hi + floor[i];
        if (hi <= Dry || h[prev] <= Dry || h[next] <= Dry)
        {
            he = hi;
            be = floor[i];
            ue = Velocity(i, qn);
            ve = Velocity(i, qt);
            return;
        }

        float sh = Minmod(hi - h[prev], h[next] - hi);
        float se = Minmod(etai - h[prev] - floor[prev], h[next] + floor[next] - etai);
        float ui = Velocity(i, qn), vi = Velocity(i, qt);
        float su = Minmod(ui - Velocity(prev, qn), Velocity(next, qn) - ui);
        float sv = Minmod(vi - Velocity(prev, qt), Velocity(next, qt) - vi);
        he = hi + 0.5f * side * sh;
        be = etai + 0.5f * side * se - he;
        ue = he > Dry ? ui + 0.5f * side * su : 0f;
        ve = he > Dry ? vi + 0.5f * side * sv : 0f;
    }

    // ∇²η of the surface at a cell from its four neighbours, when it and they are all wet and on the floor.
    private bool Laplacian(int x, int y, out float lap)
    {
        lap = 0f;
        if (x < 1 || y < 1 || x >= Cells - 1 || y >= Cells - 1)
        {
            return false;
        }

        int i = Index(x, y);
        int l = i - 1, r = i + 1, d = i - Cells, u = i + Cells;
        if (h[i] <= Dry || h[l] <= Dry || h[r] <= Dry || h[d] <= Dry || h[u] <= Dry)
        {
            return false;
        }

        float cell = (float)CellMetres;
        lap = (h[l] + floor[l] + h[r] + floor[r] + h[d] + floor[d] + h[u] + floor[u] - 4f * (h[i] + floor[i])) / (cell * cell);
        return true;
    }

    private static float Minmod(float a, float b) => a * b <= 0f ? 0f : Math.Abs(a) < Math.Abs(b) ? a : b;

    // Velocity from discharge without blowing up on a film a few microns thick (Kurganov and Petrova).
    private float Velocity(int i, float[] q)
    {
        float t = h[i];
        if (t <= Dry)
        {
            return 0f;
        }

        float t2 = t * t;
        return q[i] * 2f * t / (t2 + Math.Max(t2, Thin * Thin));
    }

    private static float Hash(int i)
    {
        uint x = (uint)i * 2654435761u;
        x ^= x >> 13;
        x *= 0x5bd1e995u;
        x ^= x >> 15;
        return (x & 0xFFFFFFu) / 16777216f;
    }

    // The wet share of a disc of radius 3.5 cells centred on the face between a and b, weighted towards its centre
    // (cells beyond the floor count as dry): half at a straight stretch of the front, whatever its direction, less
    // at a bulge, more in a notch.
    private float WetShare(int a, int b)
    {
        const float radius = 3.5f;
        int n = Cells;
        float cx = 0.5f * (a % n + b % n), cy = 0.5f * (a / n + b / n);
        float wet = 0f, total = 0f;
        for (int y = (int)Math.Floor(cy - radius); y <= (int)Math.Ceiling(cy + radius); y++)
        {
            for (int x = (int)Math.Floor(cx - radius); x <= (int)Math.Ceiling(cx + radius); x++)
            {
                float dx = x - cx, dy = y - cy, r2 = (dx * dx + dy * dy) / (radius * radius);
                if (r2 > 1f)
                {
                    continue;
                }

                float w = 1f - r2;
                total += w;
                if (x >= 0 && y >= 0 && x < n && y < n && h[Index(x, y)] > Dry)
                {
                    wet += w;
                }
            }
        }

        return wet / total;
    }

    private int Index(int x, int y) => y * Cells + x;
}
