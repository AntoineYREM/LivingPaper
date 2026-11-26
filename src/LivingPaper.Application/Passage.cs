namespace LivingPaper.Application;

// One passage of a book, as it is searched and given to a character.
// Number: from 1, in the order of the book. Part: the index (from 0) of the part of the analysis it comes from.
// Repeated: the end of the passage before, so that nothing is lost at a cut ("" for the first passage of the book).
// Body: what this passage adds. The text is never logged.
public sealed record Passage(int Number, int Part, string Repeated, string Body)
{
    // What is searched and read: never longer than PassageCutter.MaxLength.
    public string Text => Repeated.Length == 0 ? Body : Repeated + " " + Body;

    public override string ToString() => $"Passage {{ Number = {Number}, Part = {Part}, Length = {Text.Length} }}";
}
