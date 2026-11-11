using LivingPaper.Domain;

namespace LivingPaper.Application;

// The use case "analyze a book": finds the characters of a book and writes the summary of its atmosphere, with the language
// model chosen for the analysis in the settings (see AnalysisRun for the steps). It is run by a background service, one
// book at a time, so the user can leave the page.
public sealed class BookAnalyzer(
    IBookRepository books,
    IBookTextStore texts,
    IAnalysisStepStore steps,
    LlmSettingsService settings,
    IChatModelFactory models,
    IPromptProvider prompts,
    TimeProvider clock,
    AnalysisOptions options)
{
    // Analyzes the book if it is waiting, or resumes it after a failure that was set back to waiting: the steps saved by an
    // earlier try are not asked again. A book that is not there, or not waiting, is left alone.
    // Returns the reason the analysis stopped (the book is then "failed" and says why), or null when it is done or ignored.
    // Throws OperationCanceledException when stopped: the book stays "running".
    public async Task<AnalysisFailure?> AnalyzeAsync(BookId id, CancellationToken cancellationToken)
    {
        var book = await books.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (book is null || book.Status != BookStatus.Waiting)
        {
            return null;
        }

        using var run = new AnalysisRun(book, texts, steps, books, settings, models, prompts, clock, options);
        return await run.ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }
}
