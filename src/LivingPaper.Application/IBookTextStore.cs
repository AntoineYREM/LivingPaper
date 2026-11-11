using LivingPaper.Domain;

namespace LivingPaper.Application;

// The text of a book that was sent and is not analyzed yet. It is kept only until the analysis has finished successfully,
// then deleted: re-analyzing a book means sending it again. It is never put in a book's JSON file, an export or a log.
public interface IBookTextStore
{
    Task SaveAsync(BookId bookId, string text, CancellationToken cancellationToken);

    // Null when there is no text for this book (never saved, or deleted after the analysis).
    Task<string?> ReadAsync(BookId bookId, CancellationToken cancellationToken);

    // Nothing to do when there is no text.
    Task DeleteAsync(BookId bookId, CancellationToken cancellationToken);
}
