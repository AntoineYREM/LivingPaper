using LivingPaper.Domain;

namespace LivingPaper.Application;

// Where analyzed books are kept. The V0 adapter writes one JSON file per book; another backend can replace it.
public interface IBookRepository
{
    // Creates the book or replaces it entirely. Throws ArgumentException when a text is longer than allowed.
    Task SaveAsync(Book book, CancellationToken cancellationToken);

    // Null when there is no such book. Throws BookUnreadableException when the book exists but cannot be read.
    Task<Book?> GetAsync(BookId id, CancellationToken cancellationToken);

    // Every readable book, most recently sent first. A book that cannot be read is reported in the result, it never
    // makes the whole list fail.
    Task<BookListing> ListAsync(CancellationToken cancellationToken);

    // Deletes the book and its intermediate analysis steps. False when there was no such book.
    Task<bool> DeleteAsync(BookId id, CancellationToken cancellationToken);
}
