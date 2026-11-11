using System.Diagnostics.CodeAnalysis;

namespace LivingPaper.Web.Localization;

// A language of the interface: its code ("fr") and its own name, as its speakers write it ("Français"), which is never translated.
public sealed record LanguageInfo(string Code, string NativeName);

// The languages the interface is available in. English is first and is the source of every translation and the fallback.
// Adding a language: add it here, copy Resources/en.json to Resources/<code>.json and translate it (see CONTRIBUTING.md).
public static class SupportedLanguages
{
    public static IReadOnlyList<LanguageInfo> All { get; } =
    [
        new("en", "English"),
        new("fr", "Français"),
    ];

    public static LanguageInfo Default => All[0];

    // The exact code of a language, whatever its case and the spaces around ("fr", " FR "). A regional variant ("fr-FR") is not
    // a language we offer: the browser's own preference list is read by LanguageResolver, which understands variants.
    public static bool TryFind(string? code, [NotNullWhen(true)] out LanguageInfo? language)
    {
        var wanted = code?.Trim();
        language = string.IsNullOrEmpty(wanted) ? null : All.FirstOrDefault(candidate => string.Equals(candidate.Code, wanted, StringComparison.OrdinalIgnoreCase));
        return language is not null;
    }
}
