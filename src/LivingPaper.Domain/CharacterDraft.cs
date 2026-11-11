namespace LivingPaper.Domain;

// What the user typed to change a character sheet, before it is validated. Any field can be missing.
public sealed record CharacterDraft(string? Name, string? ShortDescription, string? ChatProfile)
{
    // The same fields with the spaces around them removed and missing fields as empty text. The profile keeps its line
    // breaks (its paragraphs), written the same way whatever the browser or the system sends ("\r\n" becomes "\n").
    public CharacterDraft Normalized() => new(
        Name?.Trim() ?? string.Empty,
        ShortDescription?.Trim() ?? string.Empty,
        (ChatProfile ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim());
}
