using System.Numerics;
using Caustikon;

namespace Undine;

/// <summary>
/// Sunlight on the floor under a moving surface. A grid of parallel rays from the light refracts through the surface
/// at each channel's index and lands on the floor at the given depth; the landings are summed into a map normalized so
/// that a flat surface reads as one everywhere. Ripples focus the rays into bright lines and leave dark gaps around
/// them: the caustic pattern on a pool floor. The site's shader does the same each frame on the GPU.
/// </summary>
public sealed class CausticMap
{
    private readonly Vector3[] cells;

    /// <param name="cellsPerSide">Resolution of the map.</param>
    public CausticMap(int cellsPerSide)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cellsPerSide, 4);
        CellsPerSide = cellsPerSide;
        cells = new Vector3[cellsPerSide * cellsPerSide];
    }

    /// <summary>Cells per side of the map.</summary>
    public int CellsPerSide { get; }

    /// <summary>Relative illumination per channel at a map cell; one is what a flat surface gives.</summary>
    public Vector3 this[int x, int y] => cells[y * CellsPerSide + x];

    /// <summary>
    /// Traces rays from a light travelling along <paramref name="lightDirection"/> (unit, pointing down into the liquid)
    /// through <paramref name="surface"/> to a flat floor at the surface's rest depth, and fills the map.
    /// </summary>
    /// <param name="surface">The surface the light crosses.</param>
    /// <param name="optics">The liquid's indices and Fresnel split.</param>
    /// <param name="lightDirection">Unit direction the light travels; it must point down.</param>
    /// <param name="raysPerSide">Rays per side of the emitting grid; more rays, smoother map.</param>
    public void Build(Surface surface, LiquidOptics optics, Vector3 lightDirection, int raysPerSide)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(optics);
        ArgumentOutOfRangeException.ThrowIfLessThan(raysPerSide, 4);
        Vector3 d = Vector3.Normalize(lightDirection);
        if (d.Y >= 0f)
        {
            throw new ArgumentException("The light must travel downward.", nameof(lightDirection));
        }

        Array.Clear(cells);
        float size = (float)surface.SizeMetres;
        float depth = (float)surface.DepthMetres;
        int n = CellsPerSide;
        // Rays start on the rest plane over the whole grid and refract where they meet the surface; small slopes let
        // the meeting point be taken directly above the start, which is exact for a flat surface.
        for (int channel = 0; channel < 3; channel++)
        {
            for (int j = 0; j < raysPerSide; j++)
            {
                for (int i = 0; i < raysPerSide; i++)
                {
                    float sx = (i + 0.5f) / raysPerSide * size, sz = (j + 0.5f) / raysPerSide * size;
                    int cx = Math.Clamp((int)(sx / size * surface.Cells), 0, surface.Cells - 1);
                    int cz = Math.Clamp((int)(sz / size * surface.Cells), 0, surface.Cells - 1);
                    Vector3 normal = surface.NormalAt(cx, cz);
                    float cosI = Math.Clamp(-Vector3.Dot(d, normal), 0f, 1f);
                    float index = channel switch { 0 => optics.IndexRgb.X, 1 => optics.IndexRgb.Y, _ => optics.IndexRgb.Z };
                    if (optics.Refract(d, normal, channel, out Vector3 inside) != RefractionKind.Refracted || inside.Y >= 0f)
                    {
                        continue;
                    }

                    float weight = 1f - Dielectric.Fresnel(cosI, 1f, index).Unpolarized;
                    float h = surface.HeightAt(cx, cz);
                    float t = (depth + h) / -inside.Y;
                    float fx = sx + inside.X * t, fz = sz + inside.Z * t;
                    if (fx < 0f || fx >= size || fz < 0f || fz >= size)
                    {
                        continue;
                    }

                    int mx = (int)(fx / size * n), mz = (int)(fz / size * n);
                    Vector3 energy = weight * (channel == 0 ? Vector3.UnitX : channel == 1 ? Vector3.UnitY : Vector3.UnitZ);
                    cells[mz * n + mx] += energy;
                }
            }
        }

        // A flat surface sends every ray straight to its own cell: rays per side squared over cells squared each.
        float expected = (float)raysPerSide * raysPerSide / ((float)n * n);
        for (int k = 0; k < cells.Length; k++)
        {
            cells[k] /= expected;
        }
    }

    /// <summary>Mean of the green channel over the map; one for a flat surface, less when rays leave the floor area.</summary>
    public double Mean()
    {
        double sum = 0;
        foreach (Vector3 c in cells)
        {
            sum += c.Y;
        }

        return sum / cells.Length;
    }

    /// <summary>Largest green value: how bright the brightest caustic line is relative to flat lighting.</summary>
    public double Peak()
    {
        float best = 0f;
        foreach (Vector3 c in cells)
        {
            best = MathF.Max(best, c.Y);
        }

        return best;
    }
}
