namespace LivingPaper.Evals;

// A phrase of the book, as a question gives it, against the text of a passage: the case, the line breaks and the number of spaces do
// not count, since a book file breaks its lines where it likes.
internal static class PassageQuotes
{
    public static bool Appears(string quote, string text) => Flatten(text).Contains(Flatten(quote), StringComparison.OrdinalIgnoreCase);

    // The typographic apostrophe counts as the plain one: books use either. Split(null) also splits at no-break spaces, frequent
    // before French punctuation.
    private static string Flatten(string text) =>
        string.Join(' ', text.Replace('’', '\'').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
