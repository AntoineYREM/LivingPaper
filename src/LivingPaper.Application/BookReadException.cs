namespace LivingPaper.Application;

// A book file cannot be read. The message is in English for the logs and holds nothing from the file; the reason is what
// the interface translates.
public sealed class BookReadException(BookRejection rejection) : Exception($"The book file was refused: {rejection}.")
{
    public BookRejection Rejection { get; } = rejection;
}
