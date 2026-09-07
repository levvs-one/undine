using System.Numerics;

namespace Undine.Tests;

[TestClass]
public sealed class SurfaceTests
{
    [TestMethod]
    public void WaterTakesItsOpticsFromCaustikon()
    {
        Liquid water = Liquids.Water;
        Assert.AreEqual("Water", water.Optics.Name);
        Assert.AreEqual(1.3334, water.IndexD, 1e-4);
        LiquidOptics optics = new(water);
        // Head-on reflectance of water is two per cent; the rest goes in.
        Assert.AreEqual(0.0204, optics.NormalReflectance, 5e-4);
        // Blue travels farther than red in water: that is why deep water is blue.
        Assert.IsTrue(optics.AbsorptionPerMetreRgb.X > optics.AbsorptionPerMetreRgb.Z * 8, optics.AbsorptionPerMetreRgb.ToString());
        Vector3 tenMetres = optics.Transmittance(10);
        // Hale and Querry: ten metres pass three quarters of the blue and a tenth of the red.
        Assert.IsTrue(tenMetres.Z > 0.7f && tenMetres.X < 0.15f, tenMetres.ToString());
        Assert.AreEqual(48.6, optics.CriticalAngleDegrees, 0.2);
    }

    [TestMethod]
    public void LongWavesRunAtRootGH()
    {
        Assert.AreEqual(Math.Sqrt(9.80665 * 2), Liquid.ShallowWaveSpeed(2), 1e-9);
        // On water, a 1 m wave in deep water is a gravity wave near 1.25 m/s; a 5 mm ripple is faster because of surface tension.
        double gravity = Liquids.Water.PhaseSpeed(1.0, 10);
        double ripple = Liquids.Water.PhaseSpeed(0.005, 10);
        Assert.AreEqual(1.25, gravity, 0.03);
        Assert.IsTrue(ripple > 0.29 && ripple < 0.32, ripple.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ADropSpreadsIntoARingAndNeverGrows()
    {
        Surface surface = new(128, 2.0, 0.5, Liquids.Water);
        surface.Disturb(1.0, 1.0, 0.05, -0.01);
        float initial = surface.PeakHeight();
        Assert.IsTrue(surface.HeightAt(64, 64) < -0.005);
        for (int i = 0; i < 40; i++)
        {
            surface.Step(0.02);
            Assert.IsTrue(surface.PeakHeight() <= initial * 1.05f, $"the surface grew at step {i}: {surface.PeakHeight()} > {initial}");
        }

        // The dip has spread into a ring: ten cells out the surface moves, and the centre has recovered most of the way.
        Assert.IsTrue(MathF.Abs(surface.HeightAt(64 + 10, 64)) > 1e-5f, "the wave has not reached ten cells out");
        Assert.IsTrue(surface.HeightAt(64, 64) > -0.004f, "the centre has not recovered");
    }

    [TestMethod]
    public void GlycerolStopsMovingLongBeforeWater()
    {
        double MotionAfterHalfASecond(Liquid liquid)
        {
            Surface surface = new(64, 0.5, 0.05, liquid);
            surface.Disturb(0.25, 0.25, 0.02, -0.005);
            surface.Step(0.5);
            return surface.Motion();
        }

        double water = MotionAfterHalfASecond(Liquids.Water);
        double glycerol = MotionAfterHalfASecond(Liquids.Glycerol);
        // Viscosity damps each wave as 2νk²: the short components of the bump are gone within the half second, the long
        // ones outlive it, so glycerol keeps a fraction of water's motion rather than none.
        Assert.IsTrue(glycerol < water * 0.5, $"water still moves at {water:E2}, glycerol at {glycerol:E2}");
    }

    [TestMethod]
    public void EachWavelengthRunsAtItsOwnFrequencyAndDampingTakesMotionOut()
    {
        // A standing wave of one wavelength across the pool: after one period of ω(k) it is back where it started,
        // and half a period later it is upside down. 50 cm on 1,2 m of water is a deep-water gravity wave.
        Surface surface = new(64, 2.0, 1.2, Liquids.Water);
        double wavelength = 0.5, k = 2 * Math.PI / wavelength;
        double period = 2 * Math.PI / Liquids.Water.AngularFrequency(k, 1.2);
        Assert.AreEqual(2 * Math.PI / Math.Sqrt(9.80665 * k), period, period * 0.01);
        float[] start = new float[64 * 64];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                start[y * 64 + x] = 0.01f * MathF.Cos((float)(k * (x + 0.5) * surface.CellMetres));
            }
        }

        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                surface.Disturb((x + 0.5) * surface.CellMetres, (y + 0.5) * surface.CellMetres, 1e-3, start[y * 64 + x]);
            }
        }

        surface.Step(period / 2);
        Assert.IsTrue(surface.HeightAt(0, 32) < -0.009f, $"half a period later the crest should be a trough: {surface.HeightAt(0, 32)}");
        surface.Step(period / 2);
        Assert.AreEqual(start[32 * 64], surface.HeightAt(0, 32), 0.0007f, "a period later the wave should be back");

        double MotionAfterASecond(double damping)
        {
            Surface pool = new(64, 1.0, 1.2, Liquids.Water, dampingPerSecond: damping);
            pool.Disturb(0.5, 0.5, 0.04, -0.01);
            pool.Step(0.5);
            pool.Step(0.5);
            return pool.Motion();
        }

        double free = MotionAfterASecond(0), damped = MotionAfterASecond(2.0);
        // ḧ = −ω²h − γḣ decays as exp(−γt/2): two per second over a second leaves e⁻¹ of the motion.
        Assert.AreEqual(Math.Exp(-1), damped / free, 0.06, $"free {free:E2}, damped {damped:E2}");
    }

    [TestMethod]
    public void AFlatSurfaceGivesAnEvenFloorAndRipplesFocusLight()
    {
        Surface flat = new(64, 1.0, 1.0, Liquids.Water);
        LiquidOptics optics = new(Liquids.Water);
        CausticMap map = new(32);
        map.Build(flat, optics, new Vector3(0f, -1f, 0f), 256);
        Assert.AreEqual(1.0, map.Mean(), 0.05);
        Assert.AreEqual(1.0, map.Peak(), 0.05);

        Surface rippled = new(64, 1.0, 1.0, Liquids.Water);
        rippled.Disturb(0.5, 0.5, 0.06, -0.03);
        rippled.Step(0.12);
        CausticMap fine = new(64);
        fine.Build(rippled, optics, new Vector3(0f, -1f, 0f), 512);
        Assert.IsTrue(fine.Peak() > 1.15, $"peak {fine.Peak():F2}: the ripple did not focus light");
    }

    [TestMethod]
    public void EveryLiquidIsComplete()
    {
        foreach (Liquid liquid in Liquids.All)
        {
            Assert.IsTrue(liquid.DensityKgPerM3 is > 700 and < 1400, liquid.Name);
            Assert.IsTrue(liquid.ViscosityMPaS > 0, liquid.Name);
            Assert.IsTrue(liquid.SurfaceTensionMNPerM > 0, liquid.Name);
            Assert.IsTrue(liquid.IndexD is > 1.3 and < 1.7, liquid.Name);
            Assert.IsFalse(string.IsNullOrEmpty(liquid.Optics.Provenance.Citation), liquid.Name);
        }

        Assert.AreSame(Liquids.Water, Liquids.Find("water"));
    }
}
