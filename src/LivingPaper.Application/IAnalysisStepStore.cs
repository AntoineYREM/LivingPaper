using LivingPaper.Domain;

namespace LivingPaper.Application;

// The AI's answer to each step of an analysis ("part:3", "merge", "profile:4"), saved as soon as the step succeeds so
// that retrying an analysis starts again at the step that failed, without paying again for the others.
// The steps contain parts of the analysis, never the book's text, and are deleted when the analysis ends.
public interface IAnalysisStepStore
{
    Task SaveAsync(BookId bookId, string key, string result, CancellationToken cancellationToken);

    // Empty when nothing was saved or when the saved steps cannot be read (the analysis then simply starts over).
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(BookId bookId, CancellationToken cancellationToken);

    Task DeleteAllAsync(BookId bookId, CancellationToken cancellationToken);
}
