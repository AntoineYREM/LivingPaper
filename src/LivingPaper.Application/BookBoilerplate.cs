using System.Text.RegularExpressions;

namespace LivingPaper.Application;

// The text of a book from Project Gutenberg is wrapped in its own pages: a header, and after the book a license, a request for
// donations and information about the Project. They are not the book, and the analysis would read them as scenes of its story.
// Gutenberg marks where the book starts and ends with a line "*** START OF THE PROJECT GUTENBERG EBOOK TITLE ***" and one that
// starts with "*** END OF THE PROJECT GUTENBERG EBOOK": what is outside of them is left out. A text without these markers is
// untouched.
public static partial class BookBoilerplate
{
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var start = StartMarker().Match(text);
        var from = start.Success ? start.Index + start.Length : 0;
        var end = EndMarker().Match(text, from);
        var to = end.Success ? end.Index : text.Length;
        var book = text[from..to];

        // Markers that leave nothing are not trusted: the whole text is read, as before.
        return book.Trim().Length == 0 ? text : book;
    }

    [GeneratedRegex(@"\*{3}\s*START OF (?:THE|THIS) PROJECT GUTENBERG EBOOK[^\r\n]*?\*{3}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StartMarker();

    [GeneratedRegex(@"\*{3}\s*END OF (?:THE|THIS) PROJECT GUTENBERG EBOOK[^\r\n]*?\*{3}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EndMarker();
}
