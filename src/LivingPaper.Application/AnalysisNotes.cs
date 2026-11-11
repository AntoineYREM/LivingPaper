using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using LivingPaper.Domain;

namespace LivingPaper.Application;

// What the model noted about one character in one part of the book.
internal sealed record CharacterNote(string Name, string[] OtherNames, string Role, string Personality, string Speech, string Events, int Importance);

// The notes of one part, as they are saved in the step "part:N". Skipped: the model's content filter refused the part
// even with simple notes, so it was left out; that is saved too, so a retry does not ask again.
internal sealed record PartNotes(int Part, bool Skipped, List<CharacterNote> Characters);

// A character chosen by the merge.
internal sealed record ChosenCharacter(string Name, string ShortDescription);

// Reads what the model answers, and writes what is saved. A model often wraps its JSON in a code fence or a sentence: only
// what lies between the first "{" and the last "}" is read. A reply of the wrong shape is refused, never read as "no
// character": that would hide the error behind an empty result.
internal static class AnalysisNotes
{
    // Each text noted about a character is cut to this many letters, so the notes of a long book stay small.
    private const int NoteFieldLetters = 500;

    private static readonly JsonSerializerOptions StoredOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonDocumentOptions ReadOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    // The notes of a part: an object with a "characters" array (which may be empty: a part without characters is fine).
    public static bool TryReadPart(string reply, out List<CharacterNote> notes)
    {
        notes = [];
        if (!TryReadCharacters(reply, out var items))
        {
            return false;
        }

        foreach (var item in items)
        {
            var name = Text(item, "name", CharacterLimits.Name);
            if (name.Length > 0)
            {
                notes.Add(new CharacterNote(
                    name,
                    [.. Texts(item, "otherNames").Select(other => CharacterLimits.Clip(other, CharacterLimits.Name)).Where(other => other.Length > 0).Take(10)],
                    Text(item, "role", NoteFieldLetters),
                    Text(item, "personality", NoteFieldLetters),
                    Text(item, "speech", NoteFieldLetters),
                    Text(item, "events", NoteFieldLetters),
                    Importance(item)));
            }
        }

        return true;
    }

    // The merge: an object with a "characters" array of names and short descriptions. A character with no name or no
    // description is dropped; a name that comes twice (whatever its case) is kept once, the first being the most important.
    public static bool TryReadMerge(string reply, int max, out List<ChosenCharacter> chosen)
    {
        chosen = [];
        if (!TryReadCharacters(reply, out var items))
        {
            return false;
        }

        foreach (var item in items)
        {
            var name = Text(item, "name", CharacterLimits.Name);
            var description = Text(item, "shortDescription", CharacterLimits.ShortDescription);
            if (name.Length > 0 && description.Length > 0 && !chosen.Exists(other => string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                chosen.Add(new ChosenCharacter(name, description));
            }
        }

        chosen = [.. chosen.Take(max)];
        return true;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, StoredOptions);

    public static T? Deserialize<T>(string json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, StoredOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The notes of every part as the model receives them in the merge, the profiles and the summary. Past maxLetters, each
    // part keeps fewer characters (the most important), so a very long book cannot overflow the model's context.
    public static string Combine(IReadOnlyList<PartNotes> parts, int charactersPerPart, int maxLetters)
    {
        var json = string.Empty;
        foreach (var keep in new[] { charactersPerPart, 10, 7, 5, 3, 2, 1 }.Where(keep => keep <= charactersPerPart).Distinct())
        {
            json = Serialize(new
            {
                parts = parts.Select(part => new
                {
                    part = part.Part,
                    characters = part.Characters.OrderByDescending(note => note.Importance).Take(keep).ToList(),
                }),
            });
            if (json.Length <= maxLetters)
            {
                break;
            }
        }

        return json;
    }

    private static bool TryReadCharacters(string reply, out List<JsonElement> items)
    {
        items = [];
        var first = reply.IndexOf('{', StringComparison.Ordinal);
        var last = reply.LastIndexOf('}');
        if (first < 0 || last <= first)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(reply[first..(last + 1)], ReadOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "characters", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Array)
                {
                    items = [.. property.Value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(item => item.Clone())];
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonElement? Property(JsonElement item, string name)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    // A text, trimmed and cut on a whole word; anything that is not text is empty.
    private static string Text(JsonElement item, string name, int max) =>
        Property(item, name) is { ValueKind: JsonValueKind.String } value
            ? CharacterLimits.Clip(value.GetString()!.Trim(), max)
            : string.Empty;

    private static IEnumerable<string> Texts(JsonElement item, string name) =>
        Property(item, name) is { ValueKind: JsonValueKind.Array } value
            ? value.EnumerateArray().Where(element => element.ValueKind == JsonValueKind.String).Select(element => element.GetString()!.Trim())
            : [];

    // From 1 to 10; 5 when the model gives none, so a character without a note is neither first nor last.
    private static int Importance(JsonElement item)
    {
        var number = Property(item, "importance") switch
        {
            { ValueKind: JsonValueKind.Number } value => value.TryGetDouble(out var parsed) ? parsed : 5,
            { ValueKind: JsonValueKind.String } value when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 5,
        };
        return (int)Math.Clamp(Math.Round(number), 1, 10);
    }
}
