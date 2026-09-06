using Caustikon.Glasses;

namespace Undine;

/// <summary>
/// A liquid: what it does to light, from Caustikon's catalog entry, and what it does when it moves, from the bulk
/// properties that set wave speed and damping. The mechanical numbers are handbook values at 20 °C.
/// </summary>
/// <param name="Name">Display name.</param>
/// <param name="Optics">The Caustikon catalog entry: dispersion fit, absorption table, provenance.</param>
/// <param name="DensityKgPerM3">Density at 20 °C.</param>
/// <param name="ViscosityMPaS">Dynamic viscosity at 20 °C, mPa·s. Water is 1.00; glycerol is over a thousand.</param>
/// <param name="SurfaceTensionMNPerM">Surface tension against air at 20 °C, mN/m.</param>
/// <param name="Source">Where the mechanical numbers come from.</param>
public sealed record Liquid(
    string Name,
    Glass Optics,
    double DensityKgPerM3,
    double ViscosityMPaS,
    double SurfaceTensionMNPerM,
    string Source)
{
    /// <summary>Kinematic viscosity ν = η/ρ, m²/s: the number the wave equation's damping term takes.</summary>
    public double KinematicViscosity => ViscosityMPaS * 1e-3 / DensityKgPerM3;

    /// <summary>Refractive index at the d line, relative to air, from the catalog fit.</summary>
    public double IndexD => Optics.IndexD;

    /// <summary>Phase speed of a long gravity wave on a layer <paramref name="depthMetres"/> deep: √(g·h). Ripples add surface tension; see <see cref="PhaseSpeed"/>.</summary>
    public static double ShallowWaveSpeed(double depthMetres) => Math.Sqrt(9.80665 * Math.Max(0d, depthMetres));

    /// <summary>
    /// Phase speed of a surface wave of length <paramref name="wavelengthMetres"/> on a layer <paramref name="depthMetres"/> deep,
    /// with gravity and surface tension: c² = (g/k + σk/ρ) tanh(kh). Long waves are gravity waves; below about 17 mm on
    /// water they are capillary ripples and get faster again.
    /// </summary>
    public double PhaseSpeed(double wavelengthMetres, double depthMetres)
    {
        double k = 2 * Math.PI / Math.Max(1e-6, wavelengthMetres);
        double sigma = SurfaceTensionMNPerM * 1e-3;
        double c2 = (9.80665 / k + sigma * k / DensityKgPerM3) * Math.Tanh(k * Math.Max(1e-6, depthMetres));
        return Math.Sqrt(c2);
    }
}

/// <summary>The liquids Undine knows, with their Caustikon optics and handbook mechanics.</summary>
public static class Liquids
{
    private const string Crc = "CRC Handbook of Chemistry and Physics, 97th ed. (2016), values at 20 °C, rounded";

    private static Liquid Make(string catalogName, string name, double density, double viscosity, double tension) =>
        new(name, GlassCatalog.Find("liquids", catalogName) ?? throw new InvalidOperationException($"Caustikon has no liquid named {catalogName}."), density, viscosity, tension, Crc);

    /// <summary>Water, the reference liquid: index 1.3334, clear over centimetres, blue over metres.</summary>
    public static Liquid Water { get; } = Make("Water", "Water", 998.2, 1.002, 72.8);

    /// <summary>Ethanol: lighter and less viscous than water, index 1.3608.</summary>
    public static Liquid Ethanol { get; } = Make("Ethanol", "Ethanol", 789.3, 1.074, 22.3);

    /// <summary>Methanol: the lowest index here, 1.3270.</summary>
    public static Liquid Methanol { get; } = Make("Methanol", "Methanol", 791.4, 0.593, 22.6);

    /// <summary>Acetone: thin and fast, index 1.3575.</summary>
    public static Liquid Acetone { get; } = Make("Acetone", "Acetone", 790.5, 0.32, 23.7);

    /// <summary>Glycerol: index 1.4713 and a viscosity over a thousand times water's; ripples die at once.</summary>
    public static Liquid Glycerol { get; } = Make("Glycerol", "Glycerol", 1261, 1412, 63.4);

    /// <summary>Ethylene glycol: index 1.4316, twenty times water's viscosity.</summary>
    public static Liquid EthyleneGlycol { get; } = Make("Ethylene glycol", "Ethylene glycol", 1113.5, 21.0, 48.4);

    /// <summary>Benzene: index 1.4957, strong dispersion for a liquid.</summary>
    public static Liquid Benzene { get; } = Make("Benzene", "Benzene", 876.5, 0.65, 28.9);

    /// <summary>Toluene: index 1.4921.</summary>
    public static Liquid Toluene { get; } = Make("Toluene", "Toluene", 866.9, 0.59, 28.5);

    /// <summary>Carbon disulfide: the highest index and dispersion here, 1.6276 at the d line in the Chang fit.</summary>
    public static Liquid CarbonDisulfide { get; } = Make("Carbon disulfide", "Carbon disulfide", 1263, 0.36, 32.3);

    /// <summary>Every liquid, water first.</summary>
    public static IReadOnlyList<Liquid> All { get; } =
    [
        Water, Ethanol, Methanol, Acetone, Glycerol, EthyleneGlycol, Benzene, Toluene, CarbonDisulfide,
    ];

    /// <summary>A liquid by display name, case-insensitive; null when there is none.</summary>
    public static Liquid? Find(string name) => All.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
}
