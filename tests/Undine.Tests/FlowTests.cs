using System.Globalization;

namespace Undine.Tests;

/// <summary>The flow solver against what is known in closed form, for every liquid in the table.</summary>
[TestClass]
public sealed class FlowTests
{
    private const double G = 9.80665;

    [TestMethod]
    public void AStillPuddleOnAnUnevenFloorStaysStillAndKeepsItsVolume()
    {
        foreach (Liquid liquid in Liquids.All)
        {
            ShallowFlow flow = new(64, 0.32, liquid);
            flow.ShapeFloor((x, y) => 0.01 * (Math.Sin(x * 60) + Math.Cos(y * 45)) + 0.02 * (x > 0.2 ? 1 : 0));
            // Fill to a level: thickness is the level minus the floor, nothing where the floor is above it.
            const double level = 0.03;
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    double t = level - flow.FloorAt(x, y);
                    if (t > 0)
                    {
                        flow.Place((x + 0.5) * flow.CellMetres, (y + 0.5) * flow.CellMetres, flow.CellMetres * 0.4, t);
                    }
                }
            }

            double volume = flow.Volume();
            flow.Step(0.5);
            double fastest = 0;
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    (double ux, double uy) = flow.VelocityAt(x, y);
                    fastest = Math.Max(fastest, Math.Sqrt(ux * ux + uy * uy));
                }
            }

            Assert.IsTrue(fastest < 1e-5, $"{liquid.Name}: a still puddle moves at {fastest:E2} m/s");
            Assert.AreEqual(volume, flow.Volume(), volume * 1e-6, $"{liquid.Name}: volume drifted");
        }
    }

    [TestMethod]
    public void AReleasedSheetRunsOutAtRitterSpeedWhenFreeAndCreepsWhenViscous()
    {
        // A 5 mm sheet held behind a line and let go: the dry-bed dam break. Ritter's front runs at 2√(g·h0).
        const double h0 = 0.005, held = 0.04, t = 0.08;
        double ritter = held + 2 * Math.Sqrt(G * h0) * t;
        foreach (Liquid liquid in Liquids.All)
        {
            ShallowFlow flow = new(256, 0.256, liquid);
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    if ((x + 0.5) * flow.CellMetres < held)
                    {
                        flow.Place((x + 0.5) * flow.CellMetres, (y + 0.5) * flow.CellMetres, flow.CellMetres * 0.4, h0);
                    }
                }
            }

            flow.Step(t);
            double front = 0;
            for (int x = 0; x < 256; x++)
            {
                if (flow.ThicknessAt(x, 128) > 1e-4)
                {
                    front = (x + 0.5) * flow.CellMetres;
                }
            }

            // The bottom's drag on a 5 mm sheet relaxes in h²/(3ν): milliseconds for glycerol, seconds for water.
            // A first-order scheme lets the dry tongue lag Ritter's ideal front; half the distance is the bound it clears.
            double relax = h0 * h0 / (3 * liquid.KinematicViscosity);
            string report = $"{liquid.Name}: front at {front:F4} m, Ritter {ritter:F4} m, relaxes in {relax:F3} s";
            if (relax > 10 * t)
            {
                Assert.IsTrue(front > held + 0.5 * (ritter - held) && front < held + 1.1 * (ritter - held), report);
            }
            else if (relax < t)
            {
                Assert.IsTrue(front < held + 0.3 * (ritter - held), report);
            }
            else
            {
                // In between (ethylene glycol): the sheet runs, but its thinning tongue is already held back.
                Assert.IsTrue(front > held + 0.15 * (ritter - held) && front < held + 1.1 * (ritter - held), report);
            }
        }
    }

    [TestMethod]
    public void GlycerolSpreadsAsHuppertSays()
    {
        // A cylinder of glycerol set down on a flat floor spreads as a viscous gravity current:
        // R(t) = 0,894·(g·V³·t/(3ν))^(1/8) (Huppert 1982). Ten millilitres, then four and eight seconds.
        Liquid glycerol = Liquids.Glycerol;
        const double radius0 = 0.01, thickness0 = 0.032;
        double volume = Math.PI * radius0 * radius0 * thickness0;
        ShallowFlow flow = new(128, 0.128, glycerol);
        flow.Place(0.064, 0.064, radius0, thickness0);
        double placed = flow.Volume();
        double Radius()
        {
            double peak = 0;
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    peak = Math.Max(peak, flow.ThicknessAt(x, y));
                }
            }

            int wet = 0;
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    if (flow.ThicknessAt(x, y) > 0.05 * peak)
                    {
                        wet++;
                    }
                }
            }

            return Math.Sqrt(wet * flow.CellMetres * flow.CellMetres / Math.PI);
        }

        double Huppert(double t) => 0.894 * Math.Pow(G * volume * volume * volume * t / (3 * glycerol.KinematicViscosity), 1.0 / 8);
        flow.Step(4);
        double r4 = Radius();
        flow.Step(4);
        double r8 = Radius();
        string report = string.Format(CultureInfo.InvariantCulture, "R(4) = {0:F4} vs {1:F4}, R(8) = {2:F4} vs {3:F4}", r4, Huppert(4), r8, Huppert(8));
        Assert.AreEqual(Huppert(4), r4, Huppert(4) * 0.2, report);
        Assert.AreEqual(Huppert(8), r8, Huppert(8) * 0.2, report);
        Assert.AreEqual(Math.Pow(2, 1.0 / 8), r8 / r4, 0.05, report);
        Assert.AreEqual(placed, flow.Volume(), placed * 1e-6, "volume drifted");
    }

    [TestMethod]
    public void PouringFillsADishAndOverflowsIt()
    {
        foreach (Liquid liquid in Liquids.All)
        {
            ShallowFlow flow = new(96, 0.24, liquid);
            // A dish 6 cm across and 1 cm deep in the middle of a flat table.
            flow.ShapeFloor((x, y) => -0.01 * Math.Max(0, 1 - Math.Pow(Math.Sqrt((x - 0.12) * (x - 0.12) + (y - 0.12) * (y - 0.12)) / 0.03, 4)));
            const double rate = 2e-5;
            flow.Pour(0.12, 0.12, 0.004, rate);
            flow.Step(1.0);
            Assert.AreEqual(rate * 1.0, flow.Volume(), rate * 0.01, $"{liquid.Name}: poured volume");
            Assert.IsTrue(flow.ThicknessAt(48, 48) > 0.004, $"{liquid.Name}: the dish should be filling, has {flow.ThicknessAt(48, 48):F4} m");
            flow.Step(4.0);
            // A hundred millilitres into a dish that holds about nineteen: the table around it is wet.
            bool overflow = flow.ThicknessAt(48, 20) > 1e-4 || flow.ThicknessAt(48, 76) > 1e-4;
            Assert.IsTrue(overflow, $"{liquid.Name}: no overflow after 5 s");
        }
    }
}
