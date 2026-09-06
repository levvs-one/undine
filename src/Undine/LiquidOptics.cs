using System.Numerics;
using Caustikon;
using Caustikon.Glasses;

namespace Undine;

/// <summary>
/// What a liquid does to light, from its Caustikon entry: the index per colour channel, the exact Fresnel split at
/// the surface, the refracted direction, and the colour a path of a given length leaves behind. Everything a shader
/// needs is here as numbers; the shipped shaders take exactly these.
/// </summary>
public sealed class LiquidOptics
{
    /// <summary>Wavelengths the three channels stand for: red 610, green 550, blue 465 nm.</summary>
    public static readonly double[] ChannelWavelengths = [610d, 550d, 465d];

    /// <param name="liquid">The liquid whose Caustikon entry supplies the numbers.</param>
    public LiquidOptics(Liquid liquid)
    {
        ArgumentNullException.ThrowIfNull(liquid);
        Liquid = liquid;
        Glass glass = liquid.Optics;
        IndexRgb = new Vector3(IndexAt(glass, 610d), IndexAt(glass, 550d), IndexAt(glass, 465d));
        AbsorptionPerMetreRgb = new Vector3(Absorb(glass, 610d), Absorb(glass, 550d), Absorb(glass, 465d));
    }

    /// <summary>The liquid these numbers describe.</summary>
    public Liquid Liquid { get; }

    /// <summary>Refractive index at 610, 550 and 465 nm, relative to air.</summary>
    public Vector3 IndexRgb { get; }

    /// <summary>Beer–Lambert absorption per metre at the same wavelengths, from the tabulated k; zero when the entry has no k table.</summary>
    public Vector3 AbsorptionPerMetreRgb { get; }

    /// <summary>Whether the catalog entry carries an absorption table; without one the liquid is treated as perfectly clear.</summary>
    public bool HasAbsorption => Liquid.Optics.Extinction is not null;

    /// <summary>Unpolarized reflectance looking straight down at the surface, ((n−1)/(n+1))² at the green channel.</summary>
    public double NormalReflectance => Dielectric.NormalReflectance(1f, IndexRgb.Y);

    /// <summary>Unpolarized Fresnel reflectance entering from air at the given cosine of incidence, per channel.</summary>
    public Vector3 Reflectance(float cosIncident) => new(
        Dielectric.Fresnel(cosIncident, 1f, IndexRgb.X).Unpolarized,
        Dielectric.Fresnel(cosIncident, 1f, IndexRgb.Y).Unpolarized,
        Dielectric.Fresnel(cosIncident, 1f, IndexRgb.Z).Unpolarized);

    /// <summary>The refracted direction of a unit ray entering the liquid through a surface with the given outward normal, for one channel (0 red, 1 green, 2 blue).</summary>
    public RefractionKind Refract(Vector3 incident, Vector3 normal, int channel, out Vector3 refracted) =>
        Dielectric.RefractUnit(incident, normal, 1f, Index(channel), out refracted);

    /// <summary>The refracted direction of a unit ray leaving the liquid through a surface with the given outward normal, for one channel.</summary>
    public RefractionKind Leave(Vector3 travel, Vector3 outwardNormal, int channel, out Vector3 refracted) =>
        Dielectric.RefractUnit(travel, -outwardNormal, Index(channel), 1f, out refracted);

    /// <summary>Critical angle inside the liquid, degrees: steeper rays reflect back down at the surface.</summary>
    public double CriticalAngleDegrees => Math.Asin(1d / IndexRgb.Y) * 180d / Math.PI;

    /// <summary>What white light becomes after travelling <paramref name="pathMetres"/> through the liquid: exp(−α·d) per channel, linear.</summary>
    public Vector3 Transmittance(double pathMetres) => new(
        (float)Math.Exp(-AbsorptionPerMetreRgb.X * pathMetres),
        (float)Math.Exp(-AbsorptionPerMetreRgb.Y * pathMetres),
        (float)Math.Exp(-AbsorptionPerMetreRgb.Z * pathMetres));

    /// <summary>The colour of daylight after <paramref name="pathMetres"/> of this liquid, integrated spectrally by Caustikon; null without a k table.</summary>
    public TransmittedColour? Colour(double pathMetres) => GlassColour.Transmitted(Liquid.Optics, pathMetres * 1000d);

    private float Index(int channel) => channel switch { 0 => IndexRgb.X, 1 => IndexRgb.Y, _ => IndexRgb.Z };

    private static float IndexAt(Glass glass, double wavelength)
    {
        double clamped = Math.Clamp(wavelength, glass.Model.MinimumWavelengthNanometers, glass.Model.MaximumWavelengthNanometers);
        return glass.Model.EvaluateNanometers(clamped, out double n) == DispersionStatus.Success ? (float)n : (float)glass.IndexD;
    }

    private static float Absorb(Glass glass, double wavelength)
    {
        if (glass.Extinction is not { } extinction)
        {
            return 0f;
        }

        double clamped = Math.Clamp(wavelength, extinction.MinimumWavelengthNanometers, extinction.MaximumWavelengthNanometers);
        return extinction.AbsorptionCoefficient(clamped, out double perMetre) == DispersionStatus.Success ? (float)perMetre : 0f;
    }
}
