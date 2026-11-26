namespace LivingPaper.Application;

// Makes a story, or a summary of the plot, fit the limit of the book file without losing its end: cutting it at the limit
// would drop how the book ends (PAPER-64). The longest paragraphs (the scenes) are shortened first, between two sentences,
// and keep who is present; the short ones stay whole, and the last one keeps its end. A model that ignores the word budget
// even after being asked again ends up here, without another call.
public static class StoryFitter
{
    // A longer paragraph (a model that writes no scenes apart) is handled as several.
    private const int LongestBlock = 1500;

    // Below this, a scene says nothing: there are too many to keep them all.
    private const int SmallestCap = 60;

    private const string Present = "Present:";

    // The room kept for what happens, next to who is present: without it the list is dropped.
    private const int SmallestTelling = 40;

    private const string Gap = " … ";

    private static readonly char[] SentenceEnds = ['.', '!', '?', ';'];

    // A paragraph, or a piece of a long one, with what stands before it.
    private sealed record Block(string Before, string Text);

    public static string Fit(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max);
        if (text.Length <= max)
        {
            return text;
        }

        var trimmed = text.ReplaceLineEndings("\n").Trim();
        var blocks = Blocks(trimmed);

        // The last block may take a quarter of the room even when the others are cut shorter: it tells the end.
        int LastBudget(int cap) => Math.Min(blocks[^1].Text.Length, Math.Max(cap, max / 4));
        long Total(int cap) =>
            blocks.Sum(block => (long)block.Before.Length) + blocks.SkipLast(1).Sum(block => (long)Math.Min(block.Text.Length, cap)) + LastBudget(cap);

        if (blocks.Count == 0 || Total(SmallestCap) > max)
        {
            return Ends(trimmed, max);
        }

        // The longest paragraph size that fits.
        int low = SmallestCap, high = max;
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            (low, high) = Total(middle) <= max ? (middle, high) : (low, middle - 1);
        }

        return string.Concat(blocks.SkipLast(1).Select(block => block.Before + Start(block.Text, low)))
            + blocks[^1].Before + Tail(blocks[^1].Text, LastBudget(low));
    }

    private static List<Block> Blocks(string text)
    {
        var blocks = new List<Block>();
        foreach (var line in text.Split('\n'))
        {
            var before = blocks.Count == 0 ? string.Empty : "\n";
            var rest = line;
            while (rest.Length > LongestBlock)
            {
                // After the last sentence that fits, else between two words, else anywhere (not inside a character).
                var end = rest.LastIndexOfAny(SentenceEnds, LongestBlock - 1);
                var cut = end >= LongestBlock / 2 ? end + 1 : rest.LastIndexOf(' ', LongestBlock - 1);
                var spaced = cut >= LongestBlock / 2;
                if (!spaced)
                {
                    cut = char.IsLowSurrogate(rest[LongestBlock]) ? LongestBlock - 1 : LongestBlock;
                }

                blocks.Add(new Block(before, rest[..cut].TrimEnd()));
                before = spaced ? " " : string.Empty;
                rest = rest[cut..].TrimStart();
            }

            blocks.Add(new Block(before, rest));
        }

        return blocks;
    }

    // The beginning of a scene, ending on a sentence, followed by who is present.
    private static string Start(string scene, int cap)
    {
        if (scene.Length <= cap)
        {
            return scene;
        }

        var at = scene.LastIndexOf(Present, StringComparison.Ordinal);
        var present = at > 0 && scene.Length - at <= cap - SmallestTelling ? scene[at..] : string.Empty;
        var told = present.Length == 0 ? scene : scene[..at].TrimEnd();
        var room = present.Length == 0 ? cap : cap - present.Length - 1;
        var end = told.LastIndexOfAny(SentenceEnds, Math.Min(room, told.Length) - 1);
        var kept = told.Length <= room ? told
            : end >= room / 2 ? told[..end] + "."
            : Head(told, room);
        return present.Length == 0 ? kept : kept + " " + present;
    }

    // The first characters, up to the end of a word when there is one not far before.
    private static string Head(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        var space = text.LastIndexOf(' ', length);
        var cut = space > length / 2 ? space : length;
        if (cut > 0 && char.IsLowSurrogate(text[cut]))
        {
            cut--;
        }

        return text[..cut].TrimEnd();
    }

    // The last characters, from the start of a sentence or of a word when there is one not far after.
    private static string Tail(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        var start = text.Length - length;
        var sentence = text.IndexOfAny(SentenceEnds, start);
        var space = text.IndexOf(' ', start);
        if (sentence >= 0 && sentence + 2 < text.Length && sentence - start < length / 2 && text[sentence + 1] == ' ')
        {
            start = sentence + 2;
        }
        else if (space >= 0 && space - start < length / 2)
        {
            start = space + 1;
        }

        if (start < text.Length && char.IsLowSurrogate(text[start]))
        {
            start++;
        }

        return text[start..].TrimStart();
    }

    // When the paragraphs cannot all be kept: the beginning and the end.
    private static string Ends(string text, int max)
    {
        if (max < 4 * Gap.Length)
        {
            return Tail(text, max);
        }

        var head = Head(text, (max - Gap.Length) * 2 / 3);
        return head + Gap + Tail(text, max - Gap.Length - head.Length);
    }
}
