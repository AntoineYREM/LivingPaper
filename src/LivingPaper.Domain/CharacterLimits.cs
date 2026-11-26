namespace LivingPaper.Domain;

// The longest text accepted for each field. The analysis cuts what the AI writes to these lengths: a long-winded model
// would otherwise make the next step fail, and a book file stays small and safe to import.
public static class CharacterLimits
{
    public const int Name = 100;
    public const int ShortDescription = 600;
    public const int Summary = 500;
    public const int ChatProfile = 4000;

    // The story of the book, scene by scene, written by the analysis: at most 3000 words (about 7 letters a word with the
    // names and the spaces), with room to spare.
    public const int StorySummary = 24000;

    // The short summary of the plot: about 500 words, with room to spare.
    public const int PlotSummary = 5000;

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
