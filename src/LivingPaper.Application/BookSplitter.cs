namespace LivingPaper.Application;

// Cuts the text of a book into the parts the model reads one at a time. A PDF's pages are separated by a form feed
// (see BookTextExtractor): it is cut every pagesPerPart real pages. An EPUB has no pages: it is cut every
// pagesPerPart x charactersPerPage letters, at a line break when one is close, so a sentence is rarely cut in two.
public static class BookSplitter
{
    public static IReadOnlyList<string> Split(string text, int pagesPerPart, int charactersPerPage)
    {
        ArgumentNullException.ThrowIfNull(text);
        pagesPerPart = Math.Max(1, pagesPerPart);
        charactersPerPage = Math.Max(1, charactersPerPage);

        IEnumerable<string> parts = text.Contains('\f', StringComparison.Ordinal)
            ? text.Split('\f').Chunk(pagesPerPart).Select(pages => string.Join('\n', pages.Where(page => page.Trim().Length > 0)))
            : SplitByLength(text, (long)pagesPerPart * charactersPerPage);

        return [.. parts.Where(part => part.Trim().Length > 0)];
    }

    private static IEnumerable<string> SplitByLength(string text, long length)
    {
        var size = (int)Math.Min(length, int.MaxValue / 2);
        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(start + size, text.Length);
            if (end < text.Length)
            {
                // Look back for a line break, but not further than a tenth of the part.
                var lineBreak = text.LastIndexOf('\n', end - 1, end - start);
                if (lineBreak > start + (size * 9 / 10))
                {
                    end = lineBreak + 1;
                }
            }

            yield return text[start..end];
            start = end;
        }
    }
}
