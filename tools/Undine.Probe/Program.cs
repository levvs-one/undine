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
else if (mode == "dish")
{
    // The overflow test's dish: 100 mL of a liquid into a 19 mL dish, a map of the front every second.
    Liquid liquid = args.Length > 1 ? Liquids.Find(args[1]) ?? Liquids.Water : Liquids.Water;
    ShallowFlow flow = new(96, 0.24, liquid);
    flow.ShapeFloor((x, y) => -0.01 * Math.Max(0, 1 - Math.Pow(Math.Sqrt((x - 0.12) * (x - 0.12) + (y - 0.12) * (y - 0.12)) / 0.03, 4)));
    flow.Pour(0.12, 0.12, 0.004, 2e-5);
    Console.WriteLine($"{liquid.Name}: puddle {flow.PuddleThickness * 1e3:F2} mm, angle {flow.ContactAngleDegrees:F0}");
    for (int tenth = 1; tenth <= 30; tenth++)
    {
        flow.Step(0.1);
        double second = tenth * 0.1;
        if (tenth % 2 == 1 && tenth > 10) continue;
        int wetCells = 0;
        float maxR = 0f;
        for (int y = 0; y < 96; y++)
        {
            for (int x = 0; x < 96; x++)
            {
                if (flow.ThicknessAt(x, y) > 1e-4)
                {
                    wetCells++;
                    maxR = Math.Max(maxR, (float)Math.Sqrt((x - 47.5) * (x - 47.5) + (y - 47.5) * (y - 47.5)));
                }
            }
        }

        Console.WriteLine($"t={second:F1} s volume {flow.Volume() * 1e6:F1} mL wet {wetCells} cells, reach {maxR * 2.5:F1} mm; row 48 h mm | u cm/s: " +
            string.Join(" ", Enumerable.Range(48, 48).Where(x => x % 3 == 0).Select(x => (flow.ThicknessAt(x, 48) * 1e3).ToString("F1") + "|" + (flow.VelocityAt(x, 48).X * 100).ToString("F0"))));
    }
}
else if (mode == "shape")
{
    // A pour onto a flat table: how far the front gets along the axes against the diagonals, and a map of the wet cells.
    Liquid liquid = args.Length > 1 ? Liquids.Find(args[1]) ?? Liquids.Water : Liquids.Water;
    double seconds = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 2.0;
    const int n = 128;
    ShallowFlow flow = new(n, 0.256, liquid);
    flow.Pour(0.128, 0.128, 0.004, 2e-5);
    for (double done = 0; done < seconds; done += 0.005)
    {
        flow.Step(0.005);
        if (done < 0.03) Console.WriteLine($"  t={done + 0.005:F3} stable dt {flow.StableStep() * 1e3:F3} ms, volume {flow.Volume() * 1e6:F4} mL, wet {flow.Thickness.ToArray().Count(t => t > 1e-4)}, max h {flow.Thickness.ToArray().Max() * 1e3:F2} mm, any NaN {flow.Thickness.ToArray().Any(float.IsNaN)}");
    }
    double c = (n - 1) / 2.0;
    double axis = 0, diagonal = 0;
    int wet = 0;
    for (int y = 0; y < n; y++)
    {
        for (int x = 0; x < n; x++)
        {
            if (flow.ThicknessAt(x, y) <= 1e-4) continue;
            wet++;
            double dx = x - c, dy = y - c, r = Math.Sqrt(dx * dx + dy * dy);
            if (r < 1e-9) continue;
            double along = Math.Abs(dx) / r, across = Math.Abs(dy) / r;
            if (Math.Max(along, across) > 0.995) axis = Math.Max(axis, r);
            if (Math.Abs(along - across) < 0.01) diagonal = Math.Max(diagonal, r);
        }
    }

    Console.WriteLine($"{liquid.Name} after {seconds} s: {wet} wet cells, reach along axes {axis * 2:F1} mm, along diagonals {diagonal * 2:F1} mm, ratio {diagonal / Math.Max(axis, 1e-9):F2} (1 round, 1.41 square)");
    for (int y = 0; y < n; y += 2)
    {
        Console.WriteLine(string.Concat(Enumerable.Range(0, n).Select(x => { float t = flow.ThicknessAt(x, y); return t <= 1e-4 ? '.' : t < 0.002 ? '-' : t < 0.004 ? '+' : '#'; })));
    }
}
else if (mode == "step")
{
    // A placed block of water on a flat table, a row through it after a few steps: does it flow at all?
    ShallowFlow flow = new(32, 0.064, Liquids.Water) { ContactAngleDegrees = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 0 };
    if (args.Length > 2) flow.Pour(0.032, 0.032, 0.004, 2e-5); else flow.Place(0.032, 0.032, 0.006, 0.01);
    for (int k = 0; k < 4; k++)
    {
        flow.Step(k == 0 ? 1e-4 : 0.02);
        Console.WriteLine($"after {(k == 0 ? 1e-4 : k * 0.02):F4} s, volume {flow.Volume() * 1e6:F3} mL, row 16 h mm | u cm/s:");
        Console.WriteLine(string.Join(" ", Enumerable.Range(8, 16).Select(x => (flow.ThicknessAt(x, 16) * 1e3).ToString("F2") + "|" + (flow.VelocityAt(x, 16).X * 100).ToString("F1"))));
    }
}
else if (mode == "ritter")
{
    // The Ritter test's own set-up: a 5 mm sheet behind x = 4 cm let go for 80 ms, the tongue's profile.
    const double h0 = 0.005, held = 0.04, t = 0.08;
    ShallowFlow flow = new(256, 0.256, Liquids.Water) { ContactAngleDegrees = 0 };
    for (int y = 0; y < 256; y++)
        for (int x = 0; x < 256; x++)
            if ((x + 0.5) * flow.CellMetres < held)
                flow.Place((x + 0.5) * flow.CellMetres, (y + 0.5) * flow.CellMetres, flow.CellMetres * 0.4, h0);
    flow.Step(t);
    double front = 0;
    for (int x = 0; x < 256; x++) if (flow.ThicknessAt(x, 128) > 1e-4) front = (x + 0.5) * flow.CellMetres;
    Console.WriteLine($"front {front:F4} m (Ritter {held + 2 * Math.Sqrt(G * h0) * t:F4}), volume {flow.Volume() * 1e6:F2} mL");
    Console.WriteLine("row 128, x 30..80 step 2, h mm | u cm/s: " + string.Join(" ", Enumerable.Range(15, 26).Select(i => (flow.ThicknessAt(2 * i, 128) * 1e3).ToString("F2") + "|" + (flow.VelocityAt(2 * i, 128).X * 100).ToString("F0"))));
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
