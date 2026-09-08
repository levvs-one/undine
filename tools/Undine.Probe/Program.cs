// A scratch probe for the solvers: prints how a stretched free drop of water rings for a few tension calibrations,
// and how a settled block and a dam break behave. Not shipped; run with dotnet run --project tools/Undine.Probe.
using System.Globalization;
using System.Numerics;
using Undine;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
const double G = 9.80665;

string mode = args.Length > 0 ? args[0] : "drop";
if (mode == "drop")
{
    Liquid liquid = args.Length > 1 ? Liquids.Find(args[1]) ?? Liquids.Water : Liquids.Water;
    float calibration = args.Length > 2 ? float.Parse(args[2], CultureInfo.InvariantCulture) : 1f;
    float curvature = args.Length > 3 ? float.Parse(args[3], CultureInfo.InvariantCulture) : 1f;
    const float radius = 0.01f;
    ParticleFluid fluid = new(liquid, 0.001, new Vector3(0.1f, 0.1f, 0.1f)) { GravityVector = Vector3.Zero, TensionCalibration = calibration, CurvatureWeight = curvature };
    Vector3 centre = new(0.05f, 0.05f, 0.05f);
    float stretch = 1.15f, squeeze = 1f / MathF.Sqrt(stretch);
    fluid.FillEllipsoid(centre, new Vector3(radius * stretch, radius * squeeze, radius * squeeze));

    double omega = Math.Sqrt(8 * liquid.SurfaceTensionMNPerM * 1e-3 / (liquid.DensityKgPerM3 * Math.Pow(radius, 3)));
    double period = 2 * Math.PI / omega;
    Console.WriteLine($"{liquid.Name}: {fluid.Count} particles, Rayleigh period {period:F3} s, calibration {calibration}, gamma {fluid.Cohesion:F3}, lattice density {fluid.LatticeRestDensity:F1}, mass {fluid.Mass:E3}, step {fluid.StableStep():E3}");
    fluid.Step(fluid.StableStep());
    Console.WriteLine($"after one substep: NaN positions {fluid.Positions.Count(p => float.IsNaN(p.X))}, vmax {fluid.Velocities.Where(v => !float.IsNaN(v.X)).Select(v => v.Length()).DefaultIfEmpty(-1f).Max():E3}, correction {fluid.LastCorrection:E3} m, tension {fluid.LastTension:E3}, viscous {fluid.LastViscous:E3}");
    const double dt = 1.0 / 240;
    for (int step = 0; step * dt < 2.5 * period; step++)
    {
        fluid.Step(dt);
        if (step % 6 == 0)
        {
            float fastest = fluid.Velocities.Max(v => v.Length());
            Vector3 mean = Vector3.Zero;
            foreach (Vector3 p in fluid.Positions) mean += p;
            mean /= fluid.Count;
            double sx = 0, sy = 0; int strays = 0;
            foreach (Vector3 p in fluid.Positions)
            {
                Vector3 d = p - mean;
                sx += d.X * d.X; sy += d.Y * d.Y;
                if (d.Length() > 1.5f * radius) strays++;
            }
            Console.WriteLine($"t={step * dt:F3} rmsAspect={Math.Sqrt(sx / sy):F3} rmsX={Math.Sqrt(sx / fluid.Count):F5} strays={strays} vmax={fastest:F3} tension={fluid.LastTension:F1}");
        }
    }
}
else if (mode == "rest")
{
    // No gravity, no walls nearby: a block on its lattice in the middle of the box must simply sit there.
    ParticleFluid fluid = new(Liquids.Water, 0.005, new Vector3(0.3f, 0.3f, 0.3f)) { GravityVector = Vector3.Zero };
    if (args.Length > 1) fluid.TensionCalibration = float.Parse(args[1], CultureInfo.InvariantCulture);
    if (args.Length > 2) fluid.Iterations = int.Parse(args[2], CultureInfo.InvariantCulture);
    fluid.FillBox(new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0.2f, 0.15f, 0.2f));
    for (int i = 0; i < 10; i++)
    {
        fluid.Step(0.05);
        Console.WriteLine($"t={(i + 1) * 0.05:F2} vmax={fluid.Velocities.Max(v => v.Length()):E2} correction={fluid.LastCorrection:E2} tension={fluid.LastTension:E2}");
    }
}
else if (mode == "settle")
{
    Liquid liquid = args.Length > 1 ? Liquids.Find(args[1]) ?? Liquids.Water : Liquids.Water;
    ParticleFluid fluid = new(liquid, 0.005, new Vector3(0.1f, 0.2f, 0.1f));
    if (args.Length > 2) fluid.Iterations = int.Parse(args[2], CultureInfo.InvariantCulture);
    if (args.Length > 3) fluid.Smoothing = float.Parse(args[3], CultureInfo.InvariantCulture);
    if (args.Length > 4) fluid.TensionCalibration = float.Parse(args[4], CultureInfo.InvariantCulture);
    if (args.Length > 5) fluid.MaxStep = 1.0 / double.Parse(args[5], CultureInfo.InvariantCulture);
    fluid.FillBox(Vector3.Zero, new Vector3(0.1f, 0.05f, 0.1f));
    Console.WriteLine($"iterations {fluid.Iterations} smoothing {fluid.Smoothing} gamma {fluid.Cohesion:F3} step {fluid.StableStep():E2} lattice {fluid.LatticeRestDensity:F1} wall at floor {fluid.WallDensityAt(new Vector3(0.05f, 0.0025f, 0.05f)):F1}, one up {fluid.WallDensityAt(new Vector3(0.05f, 0.0075f, 0.05f)):F1}, corner {fluid.WallDensityAt(new Vector3(0.0025f, 0.0025f, 0.0025f)):F1}");
    fluid.Step(fluid.StableStep());
    Console.WriteLine($"after one substep NaN {fluid.Positions.Count(p => float.IsNaN(p.X))} correction {fluid.LastCorrection:E2} tension {fluid.LastTension:E2} viscous {fluid.LastViscous:E2}");
    for (int i = 0; i < 12; i++)
    {
        fluid.Step(0.05);
        float[] heights = fluid.Positions.Select(p => p.Y).OrderBy(y => y).ToArray();
        float top = heights[(int)(heights.Length * 0.98)] + fluid.Spacing / 2;
        Console.WriteLine($"t={(i + 1) * 0.05:F2} top={top:F4} vmax={fluid.Velocities.Max(v => v.Length()):F3}");
    }
}
else
{
    const float height = 0.1f, width = 0.1f;
    foreach (string name in new[] { "Water", "Glycerol" })
    {
        Liquid liquid = Liquids.Find(name)!;
        ParticleFluid fluid = new(liquid, 0.005, new Vector3(0.5f, 0.2f, 0.03f));
        fluid.FillBox(Vector3.Zero, new Vector3(width, height, 0.03f));
        for (int i = 1; i <= 6; i++)
        {
            fluid.Step(0.03);
            float t = i * 0.03f;
            float[] xs = fluid.Positions.Select(p => p.X).OrderByDescending(x => x).ToArray();
            Console.WriteLine($"{name} t={t:F2} front={xs[(int)(xs.Length * 0.01)]:F3} Ritter={width + 2 * Math.Sqrt(G * height) * t:F3}");
        }
    }
}
