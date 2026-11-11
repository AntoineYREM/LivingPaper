using LivingPaper.Domain;

namespace LivingPaper.Application;

public sealed record BookListing(IReadOnlyList<Book> Books, IReadOnlyList<UnreadableBook> Unreadable);

// A stored book that could not be read. Reason is a short technical text, never a passage of the book.
public sealed record UnreadableBook(string StorageName, string Reason);
