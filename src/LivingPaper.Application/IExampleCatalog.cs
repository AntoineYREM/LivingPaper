using LivingPaper.Domain;

namespace LivingPaper.Application;

// The example books shipped with the application: public-domain books already analyzed, so the application can be tried
// without a key. Read only: nothing here can be changed or deleted from the interface. Each one is in the format of a book
// of the library (and of an export), and is checked like any book read from a file.
public interface IExampleCatalog
{
    // The examples that are valid, by title, and the files that were ignored with the reason (a short technical text, never a
    // passage of the file). Read once and kept: the examples do not change while the application runs.
    Task<BookListing> ListAsync(CancellationToken cancellationToken);

    // Null when there is no example with this id.
    Task<Book?> GetAsync(BookId id, CancellationToken cancellationToken);
}
