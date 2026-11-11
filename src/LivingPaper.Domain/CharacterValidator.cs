namespace LivingPaper.Domain;

// The rules a character sheet must follow when the user changes it. The limits are those the analysis already applies to
// what the model writes (CharacterLimits), so a sheet edited by hand is never longer than one written by the model.
public static class CharacterValidator
{
    // otherNames: the names of the other characters of the book, to refuse a duplicate (ignoring case and the spaces around).
    // The errors come in the order of the form: name, short description, profile.
    public static IReadOnlyList<FieldError> Validate(CharacterDraft draft, IEnumerable<string> otherNames)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(otherNames);
        var normalized = draft.Normalized();
        var errors = new List<FieldError>();

        Add(errors, "name", CheckText(normalized.Name!, CharacterLimits.Name, allowLineBreaks: false) ?? CheckTaken(normalized.Name!, otherNames));
        Add(errors, "shortDescription", CheckText(normalized.ShortDescription!, CharacterLimits.ShortDescription, allowLineBreaks: false));
        Add(errors, "chatProfile", CheckText(normalized.ChatProfile!, CharacterLimits.ChatProfile, allowLineBreaks: true));
        return errors;
    }

    private static void Add(List<FieldError> errors, string field, string? code)
    {
        if (code is not null)
        {
            errors.Add(new FieldError(field, code));
        }
    }

    private static string? CheckText(string text, int max, bool allowLineBreaks)
    {
        if (text.Length == 0)
        {
            return CharacterErrors.Required;
        }

        if (text.Length > max)
        {
            return CharacterErrors.TooLong;
        }

        return text.Any(character => char.IsControl(character) && !(allowLineBreaks && character == '\n'))
            ? CharacterErrors.InvalidCharacters
            : null;
    }

    private static string? CheckTaken(string name, IEnumerable<string> otherNames) =>
        otherNames.Any(other => string.Equals(other?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            ? CharacterErrors.Taken
            : null;
}
