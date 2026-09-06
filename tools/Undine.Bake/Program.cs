// Writes site/data/liquids.json: the numbers the site's shaders take for each liquid, computed by Undine and
// Caustikon here so the page never types a physical constant by hand. Run: dotnet run --project tools/Undine.Bake
using System.Globalization;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Undine;

string output = args.Length > 0 ? args[0] : Path.Combine(FindRepository(), "site", "data", "liquids.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);

List<LiquidRow> rows = [];
foreach (Liquid liquid in Liquids.All)
{
    LiquidOptics optics = new(liquid);
    Vector3 n = optics.IndexRgb, a = optics.AbsorptionPerMetreRgb;
    string? tenCentimetres = optics.Colour(0.1)?.Hex;
    string? oneMetre = optics.Colour(1)?.Hex;
    string? tenMetres = optics.Colour(10)?.Hex;
    rows.Add(new LiquidRow(
        liquid.Name,
        liquid.Optics.Name,
        [Round(n.X, 5), Round(n.Y, 5), Round(n.Z, 5)],
        [Round(a.X, 5), Round(a.Y, 5), Round(a.Z, 5)],
        optics.HasAbsorption,
        Round(liquid.IndexD, 5),
        Round(liquid.Optics.AbbeD, 2),
        Round(optics.NormalReflectance, 5),
        Round(optics.CriticalAngleDegrees, 2),
        liquid.DensityKgPerM3,
        liquid.ViscosityMPaS,
        liquid.SurfaceTensionMNPerM,
        Round(liquid.KinematicViscosity, 9),
        tenCentimetres,
        oneMetre,
        tenMetres,
        liquid.Optics.Provenance.Citation,
        liquid.Optics.Provenance.Notes,
        liquid.Source));
}

JsonSerializerOptions json = new()
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
File.WriteAllText(output, JsonSerializer.Serialize(new { generatedBy = "Undine.Bake", liquids = rows }, json) + "\n");
Console.WriteLine($"{rows.Count} liquids written to {output}");

static double Round(double value, int digits) => Math.Round(value, digits);

static string FindRepository()
{
    string? directory = AppContext.BaseDirectory;
    while (directory is not null && !File.Exists(Path.Combine(directory, "Undine.slnx")))
    {
        directory = Path.GetDirectoryName(directory);
    }

    return directory ?? Directory.GetCurrentDirectory();
}

sealed record LiquidRow(
    string Name,
    string CatalogName,
    double[] IndexRgb,
    double[] AbsorptionPerMetreRgb,
    bool HasAbsorption,
    double IndexD,
    double AbbeD,
    double NormalReflectance,
    double CriticalAngleDegrees,
    double DensityKgPerM3,
    double ViscosityMPaS,
    double SurfaceTensionMNPerM,
    double KinematicViscosity,
    string? ColourAfter10cm,
    string? ColourAfter1m,
    string? ColourAfter10m,
    string OpticsCitation,
    string OpticsNotes,
    string MechanicsSource);
