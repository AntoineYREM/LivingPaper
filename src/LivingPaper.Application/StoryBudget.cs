namespace LivingPaper.Application;

// How many words the story of a book, scene by scene, may take (the {maxWords} of the analysis.storySummary prompt). It grows with
// the length of the book, between a minimum and a maximum: a short play is told in about a thousand words, a long novel in
// three thousand at most, because the story is sent to the model with every message of a chat.
public static class StoryBudget
{
    // A word of English or French takes about 6 letters with its space.
    private const int LettersPerWord = 6;

    // One more word of story for each this many words of the book.
    private const int BookWordsPerStoryWord = 50;

    // A model does not count its words exactly: up to this much over the budget is accepted.
    public const double Tolerance = 1.1;

    // A story over the budget is asked again with a limit this much tighter each time (the model tends to write more than it is told).
    public const double Tightening = 0.8;

    // How many words a text has: the pieces separated by spaces or line breaks.
    public static int CountWords(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public static int Words(int textLength, AnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bookWords = Math.Max(0, textLength) / LettersPerWord;
        var words = options.MinStoryWords + (bookWords / BookWordsPerStoryWord);
        return Math.Clamp(words, options.MinStoryWords, Math.Max(options.MinStoryWords, options.MaxStoryWords));
    }
}
