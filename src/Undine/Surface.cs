using System.Numerics;

namespace Undine;

/// <summary>
/// A liquid surface as a height field on a square grid: the linear wave equation for long waves on a layer of the
/// given depth, with viscous damping. It is what runs on the GPU in the site and in the shipped shaders; this is the
/// same scheme on the CPU, for tools, tests and engines that want the heights on the processor.
/// </summary>
/// <remarks>
/// State per cell: height above the rest level and vertical velocity, both in metres and metres per second.
/// Each step: v += c²∇²h·dt + ν∇²v·dt, then h += v·dt, with c = √(g·depth) and ν the kinematic viscosity. The scheme
/// is stable while dt ≤ dx / (c·√2); <see cref="Step"/> splits a longer dt into as many substeps as that needs.
/// Boundaries are free (Neumann): waves reflect off the edges as they would off a pool wall.
/// </remarks>
public sealed class Surface
{
    private readonly float[] height;
    private readonly float[] velocity;
    private readonly float[] scratch;

    /// <param name="cells">Cells per side; the grid is square.</param>
    /// <param name="sizeMetres">Physical side of the grid, metres.</param>
    /// <param name="depthMetres">Depth of the layer at rest, metres; sets the long-wave speed.</param>
    /// <param name="liquid">The liquid; its kinematic viscosity damps the motion.</param>
    public Surface(int cells, double sizeMetres, double depthMetres, Liquid liquid)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cells, 4);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeMetres);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depthMetres);
        ArgumentNullException.ThrowIfNull(liquid);
        Cells = cells;
        SizeMetres = sizeMetres;
        DepthMetres = depthMetres;
        Liquid = liquid;
        height = new float[cells * cells];
        velocity = new float[cells * cells];
        scratch = new float[cells * cells];
    }

    /// <summary>Cells per side.</summary>
    public int Cells { get; }

    /// <summary>Physical side of the grid, metres.</summary>
    public double SizeMetres { get; }

    /// <summary>Depth of the layer at rest, metres.</summary>
    public double DepthMetres { get; }

    /// <summary>The liquid on the grid.</summary>
    public Liquid Liquid { get; }

    /// <summary>Cell size, metres.</summary>
    public double CellMetres => SizeMetres / Cells;

    /// <summary>Long-wave speed √(g·depth), m/s.</summary>
    public double WaveSpeed => Liquid.ShallowWaveSpeed(DepthMetres);

    /// <summary>Largest stable step for one substep, s: the wave's CFL bound and the explicit diffusion bound, whichever is smaller.</summary>
    public double StableStep => Math.Min(CellMetres / (WaveSpeed * Math.Sqrt(2d)), 0.1 * CellMetres * CellMetres / Math.Max(1e-12, Liquid.KinematicViscosity));

    /// <summary>Height above rest at a cell, metres.</summary>
    public float HeightAt(int x, int y) => height[Index(x, y)];

    /// <summary>The height field, row-major, metres. Read-only view; use <see cref="Disturb"/> to change it.</summary>
    public ReadOnlySpan<float> Heights => height;

    /// <summary>Pushes the surface down (negative <paramref name="amountMetres"/>) or up over a Gaussian of the given radius, as a touch or a drop does.</summary>
    /// <param name="xMetres">Position from the grid's left edge.</param>
    /// <param name="yMetres">Position from the grid's near edge.</param>
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

    /// <summary>Advances the surface by <paramref name="seconds"/>, in as many substeps as stability requires.</summary>
    public void Step(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        int substeps = Math.Max(1, (int)Math.Ceiling(seconds / StableStep));
        float dt = (float)(seconds / substeps);
        for (int i = 0; i < substeps; i++)
        {
            Substep(dt);
        }
    }

    private void Substep(float dt)
    {
        int n = Cells;
        float cell = (float)CellMetres;
        float c2 = (float)(WaveSpeed * WaveSpeed) / (cell * cell);
        float nu = (float)Liquid.KinematicViscosity / (cell * cell);
        // Laplacian of the height drives the velocity; Laplacian of the velocity is the viscous term.
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int i = Index(x, y);
                float lapH = Laplacian(height, x, y);
                float lapV = Laplacian(velocity, x, y);
                scratch[i] = velocity[i] + (c2 * lapH + nu * lapV) * dt;
            }
        }

        for (int i = 0; i < scratch.Length; i++)
        {
            velocity[i] = scratch[i];
            height[i] += velocity[i] * dt;
        }
    }

    private float Laplacian(float[] field, int x, int y)
    {
        int n = Cells;
        float centre = field[Index(x, y)];
        float left = field[Index(Math.Max(0, x - 1), y)], right = field[Index(Math.Min(n - 1, x + 1), y)];
        float down = field[Index(x, Math.Max(0, y - 1))], up = field[Index(x, Math.Min(n - 1, y + 1))];
        return left + right + down + up - 4f * centre;
    }

    /// <summary>Unit normal of the surface at a cell, y up, from central differences of the height.</summary>
    public Vector3 NormalAt(int x, int y)
    {
        int n = Cells;
        float cell = (float)CellMetres;
        float dhdx = (height[Index(Math.Min(n - 1, x + 1), y)] - height[Index(Math.Max(0, x - 1), y)]) / (2f * cell);
        float dhdz = (height[Index(x, Math.Min(n - 1, y + 1))] - height[Index(x, Math.Max(0, y - 1))]) / (2f * cell);
        return Vector3.Normalize(new Vector3(-dhdx, 1f, -dhdz));
    }

    /// <summary>The vertical velocity field, row-major, metres per second.</summary>
    public ReadOnlySpan<float> Velocities => velocity;

    /// <summary>Sum of h² over the grid: how far the surface is from rest.</summary>
    public double Displacement()
    {
        double sum = 0;
        foreach (float h in height)
        {
            sum += (double)h * h;
        }

        return sum;
    }

    /// <summary>Sum of v² over the grid: how much the surface is moving.</summary>
    public double Motion()
    {
        double sum = 0;
        foreach (float v in velocity)
        {
            sum += (double)v * v;
        }

        return sum;
    }

    /// <summary>Largest |h| on the grid, metres.</summary>
    public float PeakHeight()
    {
        float peak = 0f;
        foreach (float h in height)
        {
            peak = MathF.Max(peak, MathF.Abs(h));
        }

        return peak;
    }

    private int Index(int x, int y) => y * Cells + x;
}
