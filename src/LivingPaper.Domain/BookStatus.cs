namespace LivingPaper.Domain;

// Where a book's analysis is. Stored as text in the book's JSON file, so the file stays readable and portable.
public enum BookStatus
{
    Waiting,
    Running,
    Failed,
    Done,
}
