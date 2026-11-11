namespace LivingPaper.Application;

// How the analysis cuts a book and how hard it works. The defaults suit most models; the application reads the first
// two from the environment (ANALYSIS_PAGES_PER_PART, ANALYSIS_PARALLEL_CALLS) for a small local model or a strict provider.
public sealed record AnalysisOptions
{
    // About 20,000 tokens a part: fits the models with a small context window and keeps each call short.
    public int PagesPerPart { get; init; } = 40;

    // An EPUB has no pages: this many letters stand for one.
    public int CharactersPerPage { get; init; } = 2000;

    // Calls to the model at the same time (the parts, then the profiles).
    public int MaxParallelCalls { get; init; } = 3;

    // The number of characters kept: the {maxCharacters} of the merge prompt.
    public int MaxCharacters { get; init; } = 10;

    // A reply that cannot be read is asked for again this many times before the analysis stops.
    public int InvalidReplyRetries { get; init; } = 2;

    // Each part keeps its most important characters. Past this many letters of notes, the merge, the profiles and the
    // summary receive fewer characters per part, so a very long book never overflows the model's context.
    public int MaxCharactersPerPart { get; init; } = 15;

    public int MaxNotesLetters { get; init; } = 150_000;

    // A running analysis writes its progress at least this often, so a long call is not taken for a stopped one.
    public TimeSpan HeartbeatEvery { get; init; } = TimeSpan.FromMinutes(1);

    // A running analysis not written for this long is shown as interrupted and can be retried.
    public TimeSpan InterruptedAfter { get; init; } = TimeSpan.FromMinutes(10);
}
