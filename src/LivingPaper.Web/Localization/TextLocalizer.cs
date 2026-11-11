using System.Globalization;
using LivingPaper.Domain;
using Microsoft.Extensions.Logging;

namespace LivingPaper.Web.Localization;

public sealed partial class TextLocalizer(TranslationCatalog catalog, LanguageContext context, ILogger<TextLocalizer> logger) : ITextLocalizer
{
    // The language of the request is read at every call, so one instance follows the request it serves.
    public string Language => SupportedLanguages.TryFind(context.Code, out var language) ? language.Code : SupportedLanguages.Default.Code;

    public string this[string key] => Resolve(key);

    public string Format(string key, object values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Replace(key, Resolve(key), ToVariables(values));
    }

    public string Plural(string key, long count, object? values = null)
    {
        var variables = values is null ? [] : ToVariables(values);
        variables["count"] = count.ToString("N0", CultureInfo.GetCultureInfo(Language));
        var form = key + "." + (IsSingular(Language, count) ? "one" : "other");
        return Replace(form, Resolve(form), variables);
    }

    // English: only 1. French: 0 and 1 (and -1), as in "0 livre", "1 livre", "2 livres".
    private static bool IsSingular(string language, long count) => language == "fr" ? Math.Abs(count) <= 1 : count == 1;

    private string Resolve(string key)
    {
        var language = Language;
        if (catalog.TryGet(language, key, out var text))
        {
            return text;
        }

        var fallback = SupportedLanguages.Default.Code;
        if (language != fallback && catalog.TryGet(fallback, key, out var english))
        {
            if (catalog.FirstReport($"fallback:{language}:{key}"))
            {
                LogFallback(logger, key, language);
            }

            return english;
        }

        if (catalog.FirstReport($"missing:{language}:{key}"))
        {
            LogMissing(logger, key, language);
        }

        return key;
    }

    // The same single-pass replacement as the prompts: a value that contains "{name}" is never replaced again.
    // A variable the caller did not give is a bug of the caller: logged, and the placeholder stays visible.
    private string Replace(string key, string text, Dictionary<string, string> variables)
    {
        try
        {
            return PromptTemplate.Parse(text).Render(variables);
        }
        catch (ArgumentException exception)
        {
            LogVariable(logger, key, Language, exception.Message);
            return text;
        }
    }

    private Dictionary<string, string> ToVariables(object values)
    {
        var culture = CultureInfo.GetCultureInfo(Language);
        return values.GetType().GetProperties()
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToDictionary(
                property => property.Name,
                property => property.GetValue(values) switch
                {
                    null => string.Empty,
                    IFormattable formattable => formattable.ToString(null, culture),
                    var other => other.ToString() ?? string.Empty,
                },
                StringComparer.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The text '{Key}' is missing in '{Language}': the English text is shown. Add it to Resources/{Language}.json.")]
    private static partial void LogFallback(ILogger logger, string key, string language);

    [LoggerMessage(Level = LogLevel.Error, Message = "The text '{Key}' exists in no language (asked in '{Language}'): the key is shown. Add it to Resources/en.json.")]
    private static partial void LogMissing(ILogger logger, string key, string language);

    [LoggerMessage(Level = LogLevel.Error, Message = "The text '{Key}' ('{Language}') could not be filled in: {Problem}")]
    private static partial void LogVariable(ILogger logger, string key, string language, string problem);
}
