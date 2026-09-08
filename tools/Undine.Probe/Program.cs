// A scratch probe for the flow solver: prints where a still puddle moves, how far a dam break runs, how a cylinder of
// glycerol spreads. Not shipped; run with dotnet run --project tools/Undine.Probe.
using System.Globalization;
using Undine;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
const double G = 9.80665;

{
    ShallowFlow flow = new(64, 0.32, Liquids.Water);
    flow.ShapeFloor((x, y) => 0.01 * (Math.Sin(x * 60) + Math.Cos(y * 45)) + 0.02 * (x > 0.2 ? 1 : 0));
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

    for (int step = 0; step < 5; step++)
    {
        flow.Step(0.01);
        double fastest = 0;
        int bx = 0, by = 0;
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                (double ux, double uy) = flow.VelocityAt(x, y);
                double u = Math.Sqrt(ux * ux + uy * uy);
                if (u > fastest)
                {
                    fastest = u;
                    bx = x;
                    by = y;
                }
            }
        }

        Console.WriteLine($"still: t={(step + 1) * 0.01:F2} fastest {fastest:E2} at ({bx},{by}) h={flow.ThicknessAt(bx, by):E3} floor={flow.FloorAt(bx, by):F4} surface={flow.ThicknessAt(bx, by) + flow.FloorAt(bx, by):F5}");
        if (bx > 0) Console.WriteLine($"   left  h={flow.ThicknessAt(bx - 1, by):E3} floor={flow.FloorAt(bx - 1, by):F4}");
        if (bx < 63) Console.WriteLine($"   right h={flow.ThicknessAt(bx + 1, by):E3} floor={flow.FloorAt(bx + 1, by):F4}");
    }
}

{
    const double h0 = 0.005, held = 0.04;
    ShallowFlow flow = new(256, 0.256, Liquids.Water);
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

    for (int i = 1; i <= 4; i++)
    {
        flow.Step(0.02);
        double t = i * 0.02;
        string line = "";
        foreach (double threshold in new[] { 1e-3, 1e-4, 1e-5, 1e-6 })
        {
            double front = 0;
            for (int x = 0; x < 256; x++)
            {
                if (flow.ThicknessAt(x, 128) > threshold) front = (x + 0.5) * flow.CellMetres;
            }

            line += $" >{threshold:E0}: {front:F4}";
        }

        Console.WriteLine($"dam: t={t:F2} Ritter={held + 2 * Math.Sqrt(G * h0) * t:F4}{line}");
    }
}

{
    Liquid glycerol = Liquids.Glycerol;
    const double radius0 = 0.01, thickness0 = 0.032;
    double volume = Math.PI * radius0 * radius0 * thickness0;
    ShallowFlow flow = new(128, 0.128, glycerol);
    flow.Place(0.064, 0.064, radius0, thickness0);
    double Huppert(double t) => 0.894 * Math.Pow(G * volume * volume * volume * t / (3 * glycerol.KinematicViscosity), 1.0 / 8);
    for (int i = 1; i <= 8; i++)
    {
        flow.Step(1);
        double peak = 0;
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) peak = Math.Max(peak, flow.ThicknessAt(x, y));
        string line = "";
        foreach (double frac in new[] { 0.5, 0.2, 0.05, 0.01 })
        {
            int wet = 0;
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) if (flow.ThicknessAt(x, y) > frac * peak) wet++;
            line += $" >{frac:F2}: {Math.Sqrt(wet * flow.CellMetres * flow.CellMetres / Math.PI):F4}";
        }

        Console.WriteLine($"honey: t={i} Huppert={Huppert(i):F4} peak={peak:E2} V={flow.Volume():E3}{line}");
    }
}
