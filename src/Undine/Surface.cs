using System.Numerics;

namespace Undine;

/// <summary>
/// A liquid surface as a height field on a square grid with the liquid's own dispersion: every wavelength runs at
/// its own speed, long gravity waves fast, short ones slow, capillary ripples fast again, and every wavelength is
/// damped by viscosity at its own rate. It is what runs on the GPU in the site and in the shipped shaders; this is
/// the same scheme on the CPU, for tools, tests and engines that want the heights on the processor.
/// </summary>
/// <remarks>
/// State per cell: height above the rest level and vertical velocity, both in metres and metres per second.
/// A step is exact in the linear theory: the field is mirrored to twice its size so the walls reflect, transformed
/// with <see cref="Spectrum"/>, and each mode k is advanced as the damped oscillator ḧ = −ω²h − (2νk² + γ)ḣ with
/// ω² = (g·k + σk³/ρ)·tanh(k·h), solved in closed form, and transformed back. There is no stability limit and no
/// substep; the mean height is held at zero, which is the volume of the liquid staying what it is.
/// </remarks>
public sealed class Surface
{
    private readonly float[] height;
    private readonly float[] velocity;
    private readonly float[] re;
    private readonly float[] im;

    /// <param name="cells">Cells per side, a power of two; the grid is square.</param>
    /// <param name="sizeMetres">Physical side of the grid, metres.</param>
    /// <param name="depthMetres">Depth of the layer at rest, metres.</param>
    /// <param name="liquid">The liquid; its surface tension, density and viscosity shape and damp the waves.</param>
    /// <param name="dampingPerSecond">
    /// Decay of the velocity beyond viscosity, 1/s: what a pool's rim and surface film take out of the motion. Zero
    /// leaves viscosity alone, which on water lets a ripple ring for minutes.
    /// </param>
    public Surface(int cells, double sizeMetres, double depthMetres, Liquid liquid, double dampingPerSecond = 0d)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cells, 4);
        if ((cells & (cells - 1)) != 0)
        {
            throw new ArgumentException("Cells per side must be a power of two.", nameof(cells));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeMetres);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depthMetres);
        ArgumentNullException.ThrowIfNull(liquid);
        ArgumentOutOfRangeException.ThrowIfNegative(dampingPerSecond);
        Cells = cells;
        SizeMetres = sizeMetres;
        DepthMetres = depthMetres;
        Liquid = liquid;
        DampingPerSecond = dampingPerSecond;
        height = new float[cells * cells];
        velocity = new float[cells * cells];
        re = new float[4 * cells * cells];
        im = new float[4 * cells * cells];
    }

    /// <summary>Cells per side.</summary>
    public int Cells { get; }

    /// <summary>Decay of the velocity beyond viscosity, 1/s.</summary>
    public double DampingPerSecond { get; }

    /// <summary>Physical side of the grid, metres.</summary>
    public double SizeMetres { get; }

    /// <summary>Depth of the layer at rest, metres.</summary>
    public double DepthMetres { get; }

    /// <summary>The liquid on the grid.</summary>
    public Liquid Liquid { get; }

    /// <summary>Cell size, metres.</summary>
    public double CellMetres => SizeMetres / Cells;

    /// <summary>Long-wave speed √(g·depth), m/s; every shorter wave runs at <see cref="Liquid.PhaseSpeed"/>.</summary>
    public double WaveSpeed => Liquid.ShallowWaveSpeed(DepthMetres);

    /// <summary>Height at a cell, metres above the rest level.</summary>
    public float HeightAt(int x, int y) => height[Index(x, y)];

    /// <summary>All heights, row-major, metres.</summary>
    public ReadOnlySpan<float> Heights => height;

    /// <summary>All vertical velocities, row-major, m/s.</summary>
    public ReadOnlySpan<float> Velocities => velocity;

    /// <summary>Adds a Gaussian bump: a drop, a finger, a stone.</summary>
    /// <param name="xMetres">Centre along x, metres from the grid's edge.</param>
    /// <param name="yMetres">Centre along y, metres from the grid's edge.</param>
    /// <param name="radiusMetres">Standard deviation of the bump.</param>
    /// <param name="amountMetres">Peak displacement.</param>
    public void Disturb(double xMetres, double yMetres, double radiusMetres, double amountMetres)
    {
        double cell = CellMetres;
        double inv = 1d / (2d * radiusMetres * radiusMetres);
        int reach = (int)Math.Ceiling(3d * radiusMetres / cell);
        int cx = (int)Math.Round(xMetres / cell), cy = (int)Math.Round(yMetres / cell);
        for (int y = Math.Max(0, cy - reach); y <= Math.Min(Cells - 1, cy + reach); y++)
        {
            for (int x = Math.Max(0, cx - reach); x <= Math.Min(Cells - 1, cx + reach); x++)
            {
                double dx = (x + 0.5) * cell - xMetres, dy = (y + 0.5) * cell - yMetres;
                height[Index(x, y)] += (float)(amountMetres * Math.Exp(-(dx * dx + dy * dy) * inv));
            }
        }
    }

    /// <summary>Advances the surface by <paramref name="seconds"/>: one exact step of the linear evolution.</summary>
    public void Step(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        int n = Cells, m = 2 * n;
        // Mirror into twice the size: an even field has no jump at the walls, so waves reflect there.
        for (int y = 0; y < m; y++)
        {
            int sy = y < n ? y : m - 1 - y;
            for (int x = 0; x < m; x++)
            {
                int sx = x < n ? x : m - 1 - x;
                int source = Index(sx, sy);
                re[y * m + x] = height[source];
                im[y * m + x] = velocity[source];
            }
        }

        Spectrum.Transform(re, im, m, inverse: false);
        Evolve(seconds, m);
        Spectrum.Transform(re, im, m, inverse: true);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                height[Index(x, y)] = re[y * m + x];
                velocity[Index(x, y)] = im[y * m + x];
            }
        }
    }

    // Z = H + iV holds both real spectra; each is recovered from Z(k) and Z(−k), rotated, decayed and put back.
    private void Evolve(double dt, int m)
    {
        double dk = 2 * Math.PI / (m * CellMetres);
        double nu = Liquid.KinematicViscosity;
        for (int j = 0; j < m; j++)
        {
            int jm = (m - j) % m;
            double ky = (j < m / 2 ? j : j - m) * dk;
            for (int i = 0; i < m; i++)
            {
                int im2 = (m - i) % m;
                // Each pair (k, −k) is handled once, from the member that comes first in memory.
                if (jm * m + im2 < j * m + i)
                {
                    continue;
                }

                int a = j * m + i, b = jm * m + im2;
                double kx = (i < m / 2 ? i : i - m) * dk;
                double k = Math.Sqrt(kx * kx + ky * ky);
                double zr = re[a], zi = im[a], wr = re[b], wi = im[b];
                // H = (Z + conj(W))/2, V = (Z − conj(W))/(2i).
                double hr = 0.5 * (zr + wr), hi = 0.5 * (zi - wi);
                double vr = 0.5 * (zi + wi), vi = -0.5 * (zr - wr);
                if (k == 0)
                {
                    hr = hi = vr = vi = 0;
                }
                else
                {
                    // The damped oscillator ḧ = −ω²h − 2βḣ, solved exactly over dt: under-, over- and critically damped alike.
                    double omega = Liquid.AngularFrequency(k, DepthMetres);
                    double beta = nu * k * k + 0.5 * DampingPerSecond;
                    double omega2 = omega * omega;
                    double discriminant = omega2 - beta * beta;
                    double c, s;
                    if (discriminant > 1e-12)
                    {
                        double w = Math.Sqrt(discriminant);
                        c = Math.Cos(w * dt);
                        s = Math.Sin(w * dt) / w;
                    }
                    else if (discriminant < -1e-12)
                    {
                        double w = Math.Sqrt(-discriminant);
                        c = Math.Cosh(w * dt);
                        s = Math.Sinh(w * dt) / w;
                    }
                    else
                    {
                        c = 1;
                        s = dt;
                    }

                    double decay = Math.Exp(-beta * dt);
                    double nhr = decay * (hr * c + (vr + beta * hr) * s), nhi = decay * (hi * c + (vi + beta * hi) * s);
                    double nvr = decay * (vr * c - (omega2 * hr + beta * vr) * s), nvi = decay * (vi * c - (omega2 * hi + beta * vi) * s);
                    (hr, hi, vr, vi) = (nhr, nhi, nvr, nvi);
                }

                // Z' = H' + iV' at k, and its partner at −k from the conjugate symmetry of real fields.
                re[a] = (float)(hr - vi);
                im[a] = (float)(hi + vr);
                re[b] = (float)(hr + vi);
                im[b] = (float)(-hi + vr);
            }
        }
    }

    /// <summary>Unit normal of the surface at a cell, y up, from central differences of the height.</summary>
    public Vector3 NormalAt(int x, int y)
    {
        float cell = (float)CellMetres;
        float left = height[Index(Math.Max(0, x - 1), y)], right = height[Index(Math.Min(Cells - 1, x + 1), y)];
        float down = height[Index(x, Math.Max(0, y - 1))], up = height[Index(x, Math.Min(Cells - 1, y + 1))];
        return Vector3.Normalize(new Vector3(-(right - left) / (2f * cell), 1f, -(up - down) / (2f * cell)));
    }

    /// <summary>Root mean square of the height, metres: how far the surface is from flat.</summary>
    public double Displacement()
    {
        double sum = 0;
        foreach (float h in height)
        {
            sum += (double)h * h;
        }

        return Math.Sqrt(sum / height.Length);
    }

    /// <summary>Root mean square of the vertical velocity, m/s: how much the surface is moving.</summary>
    public double Motion()
    {
        double sum = 0;
        foreach (float v in velocity)
        {
            sum += (double)v * v;
        }

        return Math.Sqrt(sum / velocity.Length);
    }

    /// <summary>The largest |height| on the grid, metres.</summary>
    public float PeakHeight()
    {
        float peak = 0f;
        foreach (float h in height)
        {
            peak = Math.Max(peak, Math.Abs(h));
        }

        return peak;
    }

    private int Index(int x, int y) => y * Cells + x;
}
