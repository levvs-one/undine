namespace Undine.Site.Localization;

/// <summary>Display names of the liquids in both languages; the package keeps the English catalog names.</summary>
public static class LiquidNames
{
    private static readonly Dictionary<string, string> Russian = new(StringComparer.Ordinal)
    {
        ["Water"] = "Вода",
        ["Ethanol"] = "Этанол",
        ["Methanol"] = "Метанол",
        ["Acetone"] = "Ацетон",
        ["Glycerol"] = "Глицерин",
        ["Ethylene glycol"] = "Этиленгликоль",
        ["Benzene"] = "Бензол",
        ["Toluene"] = "Толуол",
        ["Carbon disulfide"] = "Сероуглерод",
    };

    public static string Of(Locale locale, Liquid liquid) =>
        locale.IsRussian && Russian.TryGetValue(liquid.Name, out string? name) ? name : liquid.Name;
}
