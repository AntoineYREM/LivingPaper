namespace LivingPaper.Domain;

// The longest text accepted for each field. The analysis cuts what the AI writes to these lengths: a long-winded model
// would otherwise make the next step fail, and a book file stays small and safe to import.
public static class CharacterLimits
{
    public const int Name = 100;
    public const int ShortDescription = 600;
    public const int Summary = 500;
    public const int ChatProfile = 3000;

    // Cuts after the last whole word that fits, so the text never ends in the middle of a word.
    public static string Clip(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= max)
        {
            return text;
        }

        var lastSpace = text.LastIndexOf(' ', max - 1, max);
        return text[..(lastSpace > 0 ? lastSpace : max)].TrimEnd();
    }
}
