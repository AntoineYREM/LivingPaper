using System.Text.Json;
using System.Text.RegularExpressions;

namespace LivingPaper.Web.Localization;

// A translation file (Resources/en.json) is a flat object of "key": "text" pairs. Anything else is an error that names the
// file and the key and never repeats a text.
public static partial class TranslationFile
{
    // Letters, digits and underscores, in parts separated by single dots: "home.tagline", "settings.error.name_taken".
    [GeneratedRegex(@"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)*$")]
    private static partial Regex KeyPattern();

    public static IReadOnlyDictionary<string, string> Parse(string json, string fileName)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            // Where it broke ("line 3, position 12"), never what the file contains.
            throw new TranslationFileException(fileName, null, $"the file is not valid JSON (line {exception.LineNumber}, position {exception.BytePositionInLine})");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new TranslationFileException(fileName, null, "the file must be one object of \"key\": \"text\" pairs");
            }

            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var key = property.Name;
                if (!KeyPattern().IsMatch(key))
                {
                    throw new TranslationFileException(fileName, key, "a key is made of letters, digits and underscores, in parts separated by single dots");
                }

                if (texts.ContainsKey(key))
                {
                    throw new TranslationFileException(fileName, key, "the key is defined twice");
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new TranslationFileException(fileName, key, "the value must be a text (the file is flat: no number, list or nested object)");
                }

                var text = property.Value.GetString()!;
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new TranslationFileException(fileName, key, "the text is empty");
                }

                if (text.Contains('<', StringComparison.Ordinal) || text.Contains('>', StringComparison.Ordinal))
                {
                    throw new TranslationFileException(fileName, key, "a text is plain text: it cannot contain the characters < and >");
                }

                texts[key] = text;
            }

            return texts;
        }
    }
}

public sealed class TranslationFileException(string fileName, string? key, string problem)
    : Exception(key is null ? $"{fileName}: {problem}" : $"{fileName}, key '{key}': {problem}")
{
    public string FileName { get; } = fileName;

    public string? Key { get; } = key;

    public string Problem { get; } = problem;
}
