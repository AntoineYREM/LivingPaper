using System.Collections.Concurrent;
using System.Reflection;

namespace LivingPaper.Web.Localization;

// Every text of every language, loaded once when the application starts. The files are embedded in the assembly
// (Resources/*.json), so there is nothing to find on disk and nothing to get wrong at run time.
public sealed class TranslationCatalog
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _byLanguage;
    private readonly ConcurrentDictionary<string, bool> _reported = new(StringComparer.Ordinal);

    // English is required: it is the fallback of every missing text.
    public TranslationCatalog(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byLanguage)
    {
        ArgumentNullException.ThrowIfNull(byLanguage);
        if (!byLanguage.ContainsKey(SupportedLanguages.Default.Code))
        {
            throw new ArgumentException($"The texts of '{SupportedLanguages.Default.Code}' are required: it is the fallback language.", nameof(byLanguage));
        }

        _byLanguage = new Dictionary<string, IReadOnlyDictionary<string, string>>(byLanguage, StringComparer.Ordinal);
    }

    // Reads Resources/<code>.json of every supported language from the assembly. A file that is missing or invalid stops
    // the application at start with an error that names it.
    public static TranslationCatalog LoadEmbedded(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var byLanguage = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var language in SupportedLanguages.All)
        {
            var fileName = language.Code + ".json";
            using var stream = assembly.GetManifestResourceStream("Resources." + fileName)
                ?? throw new TranslationFileException(fileName, null, "the file is not embedded in the application");
            using var reader = new StreamReader(stream);
            byLanguage[language.Code] = TranslationFile.Parse(reader.ReadToEnd(), fileName);
        }

        return new TranslationCatalog(byLanguage);
    }

    // The text of a key in exactly this language: the fallback to English is the localizer's job, because it must say so.
    public bool TryGet(string language, string key, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(key)
            || !_byLanguage.TryGetValue(language, out var texts)
            || !texts.TryGetValue(key, out var found))
        {
            return false;
        }

        text = found;
        return true;
    }

    public IReadOnlyCollection<string> KeysOf(string language) =>
        _byLanguage.TryGetValue(language, out var texts) ? [.. texts.Keys] : [];

    // True the first time a problem is reported, so a missing text is logged once and not at every request.
    internal bool FirstReport(string problem) => _reported.TryAdd(problem, true);
}
