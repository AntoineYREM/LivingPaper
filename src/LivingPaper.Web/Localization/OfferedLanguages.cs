namespace LivingPaper.Web.Localization;

// The languages the site offers right now: a subset of SupportedLanguages (the languages that have a translation file),
// chosen by the LANGUAGES setting ("en,fr"). English alone by default: the texts written by the analysis are in English, and a
// French page around them reads as a mix of two languages (PAPER-57). Adding "fr" to the setting brings French back.
public sealed class OfferedLanguages
{
    private OfferedLanguages(IReadOnlyList<LanguageInfo> languages) => Languages = languages;

    public IReadOnlyList<LanguageInfo> Languages { get; }

    // With one language there is nothing to switch to: the switch is not shown.
    public bool CanSwitch => Languages.Count > 1;

    // English is always offered, and first: it is the source of the texts and the fallback. A language we have no file for
    // stops the application at start rather than being ignored, so a typo in the setting is seen at once.
    public static OfferedLanguages Parse(string? setting)
    {
        var offered = new List<LanguageInfo> { SupportedLanguages.Default };
        foreach (var code in (setting ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!SupportedLanguages.TryFind(code, out var language))
            {
                var known = string.Join(", ", SupportedLanguages.All.Select(candidate => candidate.Code));
                throw new ArgumentException($"The setting LANGUAGES holds the language '{code}', which has no translation file. Known languages: {known}.", nameof(setting));
            }

            if (!offered.Contains(language))
            {
                offered.Add(language);
            }
        }

        return new OfferedLanguages(offered);
    }

    public bool TryFind(string? code, out LanguageInfo language)
    {
        var found = SupportedLanguages.TryFind(code, out var candidate) && Languages.Contains(candidate);
        language = found ? candidate! : SupportedLanguages.Default;
        return found;
    }
}
