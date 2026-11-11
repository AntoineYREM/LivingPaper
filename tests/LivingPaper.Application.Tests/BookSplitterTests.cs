using Shouldly;

namespace LivingPaper.Application.Tests;

// Cutting a book into the parts the model reads one by one (PAPER-8).
public class BookSplitterTests
{
    [Fact]
    public void APdf_IsCutEveryNRealPages_AndThePagesStayTogether()
    {
        var text = string.Join('\f', Enumerable.Range(1, 7).Select(page => $"page {page}"));

        var parts = BookSplitter.Split(text, pagesPerPart: 3, charactersPerPage: 2000);

        parts.Count.ShouldBe(3);
        parts[0].ShouldBe("page 1\npage 2\npage 3");
        parts[2].ShouldBe("page 7");
    }

    [Fact]
    public void ThePagesOfAPdf_EmptyAtTheEnd_DoNotMakeAnEmptyPart()
    {
        // A PDF text ends with a form feed, and a blank page may follow.
        var parts = BookSplitter.Split("page 1\fpage 2\f\f\f", pagesPerPart: 2, charactersPerPage: 2000);

        parts.ShouldBe(["page 1\npage 2"]);
    }

    [Fact]
    public void AnEpub_HasNoPages_ItIsCutEveryNTimesTheLettersOfAPage_AtALineBreak()
    {
        // 30 paragraphs of 100 letters, 2 "pages" of 1,000 letters per part.
        var text = string.Join('\n', Enumerable.Range(1, 30).Select(index => $"{index:00} {new string('a', 97)}"));

        var parts = BookSplitter.Split(text, pagesPerPart: 2, charactersPerPage: 1000);

        parts.Count.ShouldBeGreaterThanOrEqualTo(2);
        parts.ShouldAllBe(part => part.Length <= 2000);
        string.Concat(parts).Replace("\n", string.Empty, StringComparison.Ordinal).ShouldBe(text.Replace("\n", string.Empty, StringComparison.Ordinal));
        parts.Take(parts.Count - 1).ShouldAllBe(part => part.EndsWith('\n'), "a part ends at a line break so a sentence is rarely cut");
    }

    [Fact]
    public void ASmallBook_IsOnePart() =>
        BookSplitter.Split("A short book.", pagesPerPart: 40, charactersPerPage: 2000).ShouldBe(["A short book."]);

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("\f\f")]
    public void NothingToRead_GivesNoPart(string text) =>
        BookSplitter.Split(text, pagesPerPart: 40, charactersPerPage: 2000).ShouldBeEmpty();

    [Fact]
    public void ANumberOfPagesBelowOne_IsTakenAsOne() =>
        BookSplitter.Split("a\fb\fc", pagesPerPart: 0, charactersPerPage: 0).Count.ShouldBe(3);
}
