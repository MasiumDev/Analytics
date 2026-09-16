namespace Analytics.Api.Configuration;

public sealed class LocalizationOptions
{
    public const string SectionName = "Localization";

    public string DefaultLocale { get; init; } = "fa-IR";

    public string DisplayTimeZone { get; init; } = "Asia/Tehran";
}
