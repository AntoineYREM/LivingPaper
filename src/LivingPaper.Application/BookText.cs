namespace LivingPaper.Application;

// What was read from a book file: its title (null when the file does not give one) and its text. In a PDF the pages are
// separated by a form feed ('\f'), so the analysis can cut the book by real pages. The text is never logged.
public sealed record BookText(string? Title, string Text)
{
    public override string ToString() => $"BookText {{ Title = {Title}, Length = {Text.Length} }}";
}
