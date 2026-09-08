using System.Numerics;

namespace Undine.Tests;

/// <summary>The particle liquid against what is known in closed form, for every liquid in the table.</summary>
[TestClass]
public sealed class FluidTests
{
    private const double G = 9.80665;

    [TestMethod]
    public void ALiquidSettlesToItsOwnVolumeAndDensity()
    {
        foreach (Liquid liquid in Liquids.All)
        {
            // A block a tenth of a metre square and five centimetres tall, left in a box: it must stay that tall.
            ParticleFluid fluid = new(liquid, 0.005, new Vector3(0.1f, 0.2f, 0.1f));
            fluid.FillBox(Vector3.Zero, new Vector3(0.1f, 0.05f, 0.1f));
            int count = fluid.Count;
            double volume = fluid.Volume;
            fluid.Step(0.6);
            float[] heights = fluid.Positions.Select(p => p.Y).OrderBy(y => y).ToArray();
            float top = heights[(int)(heights.Length * 0.98)];
            Assert.AreEqual(count, fluid.Count, "particles were lost");
            Assert.AreEqual(volume, fluid.Volume, volume * 1e-9);
            Assert.AreEqual(0.05f, top + fluid.Spacing / 2, 0.003f, $"{liquid.Name}: the block stands {top + fluid.Spacing / 2:F4} m tall");
            double motion = Math.Sqrt(fluid.Velocities.Average(v => (double)v.LengthSquared()));
            Assert.IsTrue(motion < 0.03, $"{liquid.Name}: still moving at {motion:F3} m/s RMS after settling");
        }
    }

    [TestMethod]
    public void ADamBreakRunsOutWhenFreeAndSlumpsWhenViscous()
    {
        // A column a tenth of a metre square let go in a long box; Ritter's front would reach 2√(g·H)·t, Martin and
        // Moses measured about three quarters of that for water; four projection iterations at 5 mm lose some more.
        // At this size viscosity hardly matters even for glycerol (its boundary layer in 0,15 s is a centimetre), so
        // glycerol is only asked not to outrun water.
        const float height = 0.1f, width = 0.1f, t = 0.15f;
        double ritter = width + 2 * Math.Sqrt(G * height) * t;
        Dictionary<string, float> fronts = [];
        foreach (Liquid liquid in Liquids.All)
        {
            ParticleFluid fluid = new(liquid, 0.005, new Vector3(0.5f, 0.2f, 0.03f));
            fluid.FillBox(Vector3.Zero, new Vector3(width, height, 0.03f));
            fluid.Step(t);
            float[] xs = fluid.Positions.Select(p => p.X).OrderByDescending(x => x).ToArray();
            float front = xs[(int)(xs.Length * 0.01)];
            fronts[liquid.Name] = front;
            double relax = height * height / (3 * liquid.KinematicViscosity);
            string report = $"{liquid.Name}: front at {front:F3} m, Ritter {ritter:F3} m";
            Assert.IsTrue(front > width + 0.3 * (ritter - width) && front < ritter * 1.05, report);
        }

        Assert.IsTrue(fronts["Glycerol"] <= fronts["Water"] * 1.02, $"glycerol {fronts["Glycerol"]:F3} vs water {fronts["Water"]:F3}");
    }

    [TestMethod]
    public void AStretchedFreeDropRoundsItselfOnRayleighsTimeAndStaysWhole()
    {
        // A drop in free fall, stretched by 23% in aspect: surface tension pulls it round. Rayleigh's period for the
        // lowest mode, 2π/√(8σ/(ρR³)), sets the time; the projection damps the ring and the cubic lattice keeps a
        // trace of the stretch, so the test asks for the rounding, not the overshoot: aspect under 1,12 inside two
        // periods, and no particle thrown off.
        const float radius = 0.006f;
        Dictionary<string, double> rounding = [];
        foreach (Liquid liquid in Liquids.All)
        {
            ParticleFluid fluid = new(liquid, 0.001, new Vector3(0.05f, 0.05f, 0.05f)) { GravityVector = Vector3.Zero };
            Vector3 centre = new(0.025f, 0.025f, 0.025f);
            float stretch = 1.15f, squeeze = 1f / MathF.Sqrt(stretch);
            fluid.FillEllipsoid(centre, new Vector3(radius * stretch, radius * squeeze, radius * squeeze));
            double omega = Math.Sqrt(8 * liquid.SurfaceTensionMNPerM * 1e-3 / (liquid.DensityKgPerM3 * Math.Pow(radius, 3)));
            double period = 2 * Math.PI / omega;
            double viscousTime = radius * radius / liquid.KinematicViscosity;
            double rounded = double.NaN;
            const double dt = 1.0 / 120;
            double limit = viscousTime < period ? 0.3 : 2 * period;
            for (int step = 1; step * dt <= limit + 1e-9; step++)
            {
                fluid.Step(dt);
                (double aspect, int strays) = Shape(fluid, radius);
                Assert.IsTrue(strays < fluid.Count / 100, $"{liquid.Name}: {strays} particles thrown off at t = {step * dt:F2}");
                if (double.IsNaN(rounded) && aspect < 1.12)
                {
                    rounded = step * dt;
                }
            }

            (double last, _) = Shape(fluid, radius);
            if (viscousTime < period)
            {
                // Glycerol: viscosity outruns the tension; the drop must still be rounding, slowly, and not moving otherwise.
                Assert.IsTrue(last < 1.23 && last > 1.0, $"{liquid.Name}: viscous drop at aspect {last:F3} after {limit:F2} s");
            }
            else
            {
                // Halfway round on a harmonic swing comes at a sixth of the period; within a factor of three of that,
                // with σ and ρ doing the work per liquid, is what the pairwise cohesion and the lattice deliver.
                Assert.IsFalse(double.IsNaN(rounded), $"{liquid.Name}: not round within two Rayleigh periods ({2 * period:F2} s), aspect {last:F3}");
                double periods = rounded / period;
                Assert.IsTrue(periods > 1.0 / 18 && periods < 0.5, $"{liquid.Name}: halfway round after {periods:F2} Rayleigh periods");
                rounding[liquid.Name] = periods;
            }
        }

        Assert.IsTrue(rounding.Count >= 7, string.Join(", ", rounding.Select(p => $"{p.Key} {p.Value:F2}")));
    }

    // The RMS aspect of the cloud along x over y, and how many particles sit beyond one and a half radii.
    private static (double Aspect, int Strays) Shape(ParticleFluid fluid, float radius)
    {
        Vector3 mean = Vector3.Zero;
        foreach (Vector3 p in fluid.Positions)
        {
            mean += p;
        }

        mean /= fluid.Count;
        double sx = 0, sy = 0;
        int strays = 0;
        foreach (Vector3 p in fluid.Positions)
        {
            Vector3 d = p - mean;
            sx += d.X * d.X;
            sy += d.Y * d.Y;
            if (d.Length() > 1.5f * radius)
            {
                strays++;
            }
        }

        return (Math.Sqrt(sx / sy), strays);
    }
}
