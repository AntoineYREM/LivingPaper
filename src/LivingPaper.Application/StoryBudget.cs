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

    public static int Words(int textLength, AnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bookWords = Math.Max(0, textLength) / LettersPerWord;
        var words = options.MinStoryWords + (bookWords / BookWordsPerStoryWord);
        return Math.Clamp(words, options.MinStoryWords, Math.Max(options.MinStoryWords, options.MaxStoryWords));
    }
}
