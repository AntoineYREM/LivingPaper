namespace LivingPaper.Application;

// Cuts the parts of a book (see BookSplitter) into passages of about TargetLength characters, never more than MaxLength
// (docs/specs/rag.md, section 4). A cut falls at the end of a paragraph, else of a sentence, else between two words, and
// each passage starts again with the last sentence of the one before. A passage never straddles two parts, and the same
// text is always cut the same way.
public static class PassageCutter
{
    public const int TargetLength = 1500;

    public const int MaxLength = 2000;

    // A longer last sentence is not repeated whole: only its last words.
    public const int MaxRepeatedLength = 200;

    // A cut is not looked for closer to the start than this: it would leave a passage too short to mean much.
    private const int MinLength = TargetLength / 2;

    // How far back the end of a sentence is looked for: a bound, so that a broken text made of thousands of spaces or
    // quotes is still cut quickly.
    private const int LookBack = 100;

    private const string ClosingMarks = "»”’)]}」』）】";
    private const string SentenceEnds = ".!?…";

    // These end a sentence even with no space after them (Chinese and Japanese).
    private const string WideSentenceEnds = "。！？";

    // A point after one of these, or after a single capital letter (an initial, "M."), does not end a sentence.
    private static readonly HashSet<string> Titles =
        new(StringComparer.Ordinal) { "Mr", "Mrs", "Ms", "Dr", "St", "Ste", "Mme", "Mlle", "Mgr", "Prof", "Jr", "Sr" };

    // How good a place to cut is, from the worst to the best.
    private enum Boundary
    {
        None,
        Word,
        Sentence,
        Paragraph,
    }

    // linesAreParagraphs: false for a PDF, where a line break only marks the end of a page.
    public static IReadOnlyList<Passage> Cut(IReadOnlyList<string> parts, bool linesAreParagraphs)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var passages = new List<Passage>();
        var repeated = string.Empty;
        for (var part = 0; part < parts.Count; part++)
        {
            var text = parts[part].ReplaceLineEndings("\n");
            if (!linesAreParagraphs)
            {
                text = text.Replace('\n', ' ');
            }

            text = text.Trim();
            var start = 0;
            while (start < text.Length)
            {
                var taken = repeated.Length == 0 ? 0 : repeated.Length + 1;
                var end = EndOfPassage(text, start, TargetLength - taken, MaxLength - taken);
                var body = text[start..end].Trim();
                if (body.Length > 0)
                {
                    passages.Add(new Passage(passages.Count + 1, part, repeated, body));
                    repeated = EndOf(body);
                }

                start = end;
                while (start < text.Length && char.IsWhiteSpace(text[start]))
                {
                    start++;
                }
            }
        }

        return passages;
    }

    // The best kind of place between MinLength and max, and of that kind the closest to the target.
    private static int EndOfPassage(string text, int start, int target, int max)
    {
        if (text.Length - start <= max)
        {
            return text.Length; // What is left fits: a short remainder stays with its passage.
        }

        var best = Boundary.None;
        var cut = start + target;
        for (var end = start + MinLength; end <= start + max; end++)
        {
            var kind = BoundaryAt(text, end);
            if (kind > best || (kind == best && kind != Boundary.None && Math.Abs(end - start - target) < Math.Abs(cut - start - target)))
            {
                (best, cut) = (kind, end);
            }
        }

        // No place at all (no space for thousands of characters): cut at the target, but not in the middle of a character.
        return best == Boundary.None && char.IsLowSurrogate(text[cut]) ? cut - 1 : cut;
    }

    // What a cut just before text[at] would be.
    private static Boundary BoundaryAt(string text, int at)
    {
        if (char.IsWhiteSpace(text[at]) || ClosesAt(text, at))
        {
            return Boundary.None; // A passage starts with neither a space nor the closing quote of the sentence before.
        }

        var last = at - 1;
        var floor = Math.Max(0, at - LookBack);
        var spaced = false;
        while (last >= floor && char.IsWhiteSpace(text[last]))
        {
            if (text[last] == '\n')
            {
                return Boundary.Paragraph;
            }

            spaced = true;
            last--;
        }

        // Back over the closing quotes and brackets, and the space French puts before "»".
        while (last >= floor && (char.IsWhiteSpace(text[last]) || ClosingMarks.Contains(text[last], StringComparison.Ordinal) || text[last] is '"' or '\''))
        {
            last--;
        }

        if (last < floor)
        {
            return spaced ? Boundary.Word : Boundary.None;
        }

        if (WideSentenceEnds.Contains(text[last], StringComparison.Ordinal))
        {
            return Boundary.Sentence;
        }

        if (!spaced)
        {
            return Boundary.None;
        }

        // "Why? she said" goes on: a small letter does not start a sentence.
        return SentenceEnds.Contains(text[last], StringComparison.Ordinal)
            && !char.IsLower(text[at])
            && !(text[last] == '.' && FollowsATitle(text, last))
                ? Boundary.Sentence
                : Boundary.Word;
    }

    // A straight quote closes only when nothing is stuck to its right.
    private static bool ClosesAt(string text, int at) =>
        ClosingMarks.Contains(text[at], StringComparison.Ordinal)
        || (text[at] is '"' or '\'' && (at + 1 == text.Length || char.IsWhiteSpace(text[at + 1])));

    private static bool FollowsATitle(string text, int point)
    {
        var start = point;
        while (start > 0 && char.IsLetter(text[start - 1]))
        {
            start--;
        }

        var word = text[start..point];
        return (word.Length == 1 && char.IsUpper(word[0])) || Titles.Contains(word);
    }

    // What the next passage repeats: the last sentence of this one, or its last words when the sentence is too long.
    private static string EndOf(string body)
    {
        var earliest = Math.Max(0, body.Length - MaxRepeatedLength);
        for (var at = body.Length - 1; at >= Math.Max(1, earliest); at--)
        {
            if (BoundaryAt(body, at) >= Boundary.Sentence)
            {
                return body[at..];
            }
        }

        if (earliest == 0)
        {
            return body;
        }

        for (var at = earliest; at < body.Length; at++)
        {
            if (char.IsWhiteSpace(body[at - 1]) && !char.IsWhiteSpace(body[at]))
            {
                return body[at..];
            }
        }

        // A very long word: its last characters.
        return body[(char.IsLowSurrogate(body[earliest]) ? earliest + 1 : earliest)..];
    }
}
