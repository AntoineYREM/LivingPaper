using System.Text;
using Shouldly;

namespace LivingPaper.Application.Tests;

// Cutting the text of a book into the passages that are searched and given to a character (docs/specs/rag.md, section 4).
public class PassageCutterTests
{
    // 100 characters with the space or the line break that follows.
    private static string Sentence(int number) => $"Sentence {number:D4} tells how Emma crossed the quiet garden and looked at the sky above the town.";

    private static string Sentences(int first, int count, string separator = " ") =>
        string.Join(separator, Enumerable.Range(first, count).Select(Sentence));

    private static IReadOnlyList<Passage> Epub(params string[] parts) => PassageCutter.Cut(parts, linesAreParagraphs: true);

    private static IReadOnlyList<Passage> Pdf(params string[] parts) => PassageCutter.Cut(parts, linesAreParagraphs: false);

    private static string WithoutSpaces(IEnumerable<string> texts) => new([.. string.Concat(texts).Where(letter => !char.IsWhiteSpace(letter))]);

    private static void ShouldKeepEveryRuleOfSize(IReadOnlyList<Passage> passages)
    {
        passages.ShouldAllBe(passage => passage.Text.Length <= PassageCutter.MaxLength);
        passages.ShouldAllBe(passage => passage.Repeated.Length <= PassageCutter.MaxRepeatedLength);
        passages.ShouldAllBe(passage => passage.Body.Length > 0);
    }

    // ---- Short and empty texts ----

    [Theory]
    [InlineData("")]
    [InlineData("   \n \n  ")]
    public void NoText_GivesNoPassage(string text)
    {
        Epub(text).ShouldBeEmpty();
        Pdf(text).ShouldBeEmpty();
        PassageCutter.Cut([], linesAreParagraphs: true).ShouldBeEmpty();
    }

    [Fact]
    public void AShortText_IsOnePassage_WithNothingRepeated()
    {
        var passage = Epub("  Emma opened the window.\nThe town was asleep.  ").ShouldHaveSingleItem();

        passage.Number.ShouldBe(1);
        passage.Part.ShouldBe(0);
        passage.Repeated.ShouldBeEmpty();
        passage.Body.ShouldBe("Emma opened the window.\nThe town was asleep.");
        passage.Text.ShouldBe(passage.Body);
    }

    // ---- Where it cuts ----

    [Fact]
    public void ALongText_IsCutBetweenParagraphs_NearTheTargetLength()
    {
        // Twelve paragraphs of four sentences (about 400 characters each).
        var paragraphs = Enumerable.Range(0, 12).Select(index => Sentences(index * 4, 4)).ToList();

        var passages = Epub(string.Join('\n', paragraphs));

        ShouldKeepEveryRuleOfSize(passages);
        passages.Count.ShouldBeGreaterThan(2);
        passages.ShouldAllBe(passage => passage.Body.Split('\n', StringSplitOptions.None).All(paragraphs.Contains)); // No paragraph is cut.
        passages.SkipLast(1).ShouldAllBe(passage => passage.Text.Length >= 1100 && passage.Text.Length <= 1900);
    }

    [Fact]
    public void AVeryLongParagraph_IsCutBetweenSentences()
    {
        var passages = Epub(Sentences(0, 80));

        ShouldKeepEveryRuleOfSize(passages);
        passages.Count.ShouldBeGreaterThan(3);
        passages.ShouldAllBe(passage => passage.Body.StartsWith("Sentence ", StringComparison.Ordinal));
        passages.ShouldAllBe(passage => passage.Body.EndsWith("town.", StringComparison.Ordinal));
    }

    [Fact]
    public void APdf_IsCutBetweenSentences_NotWhereAPageEnds()
    {
        // A page break falls in the middle of a sentence every seventh word: in a PDF a line break is no paragraph.
        var words = Sentences(0, 60).Split(' ');
        var text = string.Join(' ', words.Select((word, index) => index % 7 == 6 ? word + "\n" : word));

        var passages = Pdf(text);

        ShouldKeepEveryRuleOfSize(passages);
        passages.ShouldAllBe(passage => passage.Body.EndsWith("town.", StringComparison.Ordinal));
        passages.ShouldAllBe(passage => !passage.Text.Contains('\n'));
    }

    [Fact]
    public void ATextWithNoPunctuation_IsCutBetweenWords()
    {
        var words = Enumerable.Range(0, 1200).Select(number => $"word{number:D4}").ToList();

        var passages = Epub(string.Join(' ', words));

        ShouldKeepEveryRuleOfSize(passages);
        passages.SelectMany(passage => passage.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ShouldBe(words);
    }

    [Fact]
    public void ATextWithNoSpaceAtAll_IsCutAnywhere_ButNeverInTheMiddleOfACharacter()
    {
        // Each of these characters takes two places in the string.
        var text = "a" + string.Concat(Enumerable.Repeat("\U00020000", 4000));

        var passages = Epub(text);

        ShouldKeepEveryRuleOfSize(passages);
        string.Concat(passages.Select(passage => passage.Body)).ShouldBe(text);
        foreach (var passage in passages)
        {
            Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(passage.Text)).ShouldBe(passage.Text); // No half of a character.
        }
    }

    [Fact]
    public void ChineseAndJapaneseSentences_EndAtTheirOwnPunctuation_WithoutASpace()
    {
        var sentence = "「" + new string('字', 96) + "。」"; // 99 characters.

        var passages = Epub(string.Concat(Enumerable.Repeat(sentence, 60)));

        ShouldKeepEveryRuleOfSize(passages);
        passages.ShouldAllBe(passage => passage.Body.EndsWith("。」", StringComparison.Ordinal));
        passages.ShouldAllBe(passage => passage.Body.StartsWith('「'));
    }

    [Theory]
    [InlineData("M. Bovary")]
    [InlineData("Mme. Homais")]
    [InlineData("Mr. Darcy")]
    [InlineData("Dr. Larivière")]
    [InlineData("J. R. R. Tolkien")]
    public void ThePointOfATitleOrAnInitial_DoesNotEndASentence(string name)
    {
        // Long sentences that hold the name, so that the points of the name fall where a cut is looked for.
        var sentence = "Then " + string.Concat(Enumerable.Repeat($"{name} spoke to {name} again and ", 8)) + "left the room.";

        var passages = Epub(string.Join(' ', Enumerable.Repeat(sentence, 20)));

        ShouldKeepEveryRuleOfSize(passages);
        passages.ShouldAllBe(passage => passage.Body.StartsWith("Then ", StringComparison.Ordinal));
        passages.ShouldAllBe(passage => passage.Body.EndsWith("left the room.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("« Oui. » ")]
    [InlineData("“Yes.” ")]
    [InlineData("(He left!) ")]
    [InlineData("Vraiment ? ")]
    [InlineData("Alors… ")]
    public void ASentence_CanEndWithAClosingQuoteOrABracket(string ending)
    {
        // The only places where a sentence ends are the ones under test.
        var sentence = "Il dit " + string.Concat(Enumerable.Repeat("encore et encore ", 12)) + "puis " + ending;

        var passages = Epub(string.Concat(Enumerable.Repeat(sentence, 20)));

        ShouldKeepEveryRuleOfSize(passages);
        passages.ShouldAllBe(passage => passage.Body.StartsWith("Il dit ", StringComparison.Ordinal));
        passages.ShouldAllBe(passage => passage.Body.EndsWith(ending.Trim(), StringComparison.Ordinal));
    }

    [Fact]
    public void AQuestionFollowedByASmallLetter_DoesNotEndTheSentence()
    {
        var sentence = "Il demanda " + string.Concat(Enumerable.Repeat("encore : pourquoi ? dit-il, puis ", 6)) + "il se tut.";

        var passages = Epub(string.Join(' ', Enumerable.Repeat(sentence, 20)));

        passages.ShouldAllBe(passage => passage.Body.EndsWith("il se tut.", StringComparison.Ordinal));
    }

    // ---- What is repeated ----

    [Fact]
    public void EachPassage_StartsAgainWithTheLastSentenceOfThePreviousOne()
    {
        var passages = Epub(Sentences(0, 60));

        passages.Count.ShouldBeGreaterThan(2);
        foreach (var (previous, next) in passages.Zip(passages.Skip(1)))
        {
            next.Repeated.ShouldStartWith("Sentence ");
            previous.Body.ShouldEndWith(next.Repeated);
            next.Repeated.Length.ShouldBe(Sentence(0).Length); // One sentence, whole.
            next.Text.ShouldBe(next.Repeated + " " + next.Body);
        }
    }

    [Fact]
    public void WhenTheLastSentenceIsTooLong_OnlyItsLastWordsAreRepeated()
    {
        var sentence = "It begins " + string.Concat(Enumerable.Range(0, 70).Select(number => $"and word{number:D2} ")) + "ends here.";

        var passages = Epub(string.Join(' ', Enumerable.Repeat(sentence, 12)));

        ShouldKeepEveryRuleOfSize(passages);
        foreach (var (previous, next) in passages.Zip(passages.Skip(1)))
        {
            next.Repeated.Length.ShouldBeInRange(PassageCutter.MaxRepeatedLength - 15, PassageCutter.MaxRepeatedLength);
            previous.Body.ShouldEndWith(" " + next.Repeated); // It starts at the start of a word.
        }
    }

    // ---- The parts of the analysis ----

    [Fact]
    public void Passages_AreNumberedThroughTheBook_AndStayInsideTheirPart()
    {
        string[] parts = [Sentences(0, 40), "  ", Sentences(40, 3), Sentences(43, 40)];

        var passages = Epub(parts);

        passages.Select(passage => passage.Number).ShouldBe(Enumerable.Range(1, passages.Count));
        passages.Select(passage => passage.Part).Distinct().ShouldBe([0, 2, 3]);
        foreach (var part in new[] { 0, 2, 3 })
        {
            WithoutSpaces(passages.Where(passage => passage.Part == part).Select(passage => passage.Body)).ShouldBe(WithoutSpaces([parts[part]]));
        }

        var firstOfThird = passages.First(passage => passage.Part == 2);
        firstOfThird.Repeated.ShouldBe(Sentence(39)); // The thread goes on from one part to the next.
    }

    // ---- What must always hold ----

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void AnyText_LosesNothing_KeepsTheLimits_AndIsAlwaysCutTheSameWay(int seed, bool linesAreParagraphs)
    {
        var random = new Random(seed);
        string[] pieces = ["a", "word", "Emma", "M.", ".", "!", "?", "…", " ", " ", " ", " ", "\n", "»", "«", "\"", "。", "字", "\U0001F600", new('x', 900)];
        var parts = Enumerable.Range(0, 4)
            .Select(_ => string.Concat(Enumerable.Range(0, 3000).Select(_ => pieces[random.Next(pieces.Length - (random.Next(200) == 0 ? 0 : 1))])))
            .ToList();

        var passages = PassageCutter.Cut(parts, linesAreParagraphs);

        ShouldKeepEveryRuleOfSize(passages);
        WithoutSpaces(passages.Select(passage => passage.Body)).ShouldBe(WithoutSpaces(parts));
        PassageCutter.Cut(parts, linesAreParagraphs).ShouldBe(passages);
    }
}
