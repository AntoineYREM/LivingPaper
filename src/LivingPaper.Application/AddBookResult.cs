using LivingPaper.Domain;

namespace LivingPaper.Application;

// The outcome of sending a book: the book that was created, or the reason it was refused. Replaced is true when an
// earlier book of the same title was replaced (the "Replace" box).
public sealed record AddBookResult(Book? Added, BookRejection? Rejection, bool Replaced = false)
{
    public bool Succeeded => Added is not null;
}
