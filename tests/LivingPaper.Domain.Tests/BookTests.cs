using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Domain.Tests;

public class BookTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static Book NewBook() => new(BookId.New(), "Le Malade imaginaire", "malade.pdf", Now);

    [Fact]
    public void NewBook_IsWaiting_WithNothingAnalyzedYet()
    {
        var book = NewBook();

        book.Status.ShouldBe(BookStatus.Waiting);
        book.SentAt.ShouldBe(Now);
        book.UpdatedAt.ShouldBe(Now);
        book.FinishedAt.ShouldBeNull();
        book.Summary.ShouldBeNull();
        book.Characters.ShouldBeEmpty();
        book.ErrorCode.ShouldBeNull();
    }

    [Fact]
    public void FindLimitViolations_ReturnsNothing_ForAWellFormedBook()
    {
        var book = NewBook();
        book.Summary = "France, 1673. A comedy.";
        book.Characters.Add(new Character(1, "Argan", "A hypochondriac.", "I am Argan."));

        book.FindLimitViolations().ShouldBeEmpty();
    }

    [Fact]
    public void FindLimitViolations_ReportsEveryTooLongField()
    {
        var book = new Book(BookId.New(), new string('t', BookLimits.Title + 1), new string('f', BookLimits.FileName + 1), Now)
        {
            Summary = new string('s', CharacterLimits.Summary + 1),
            ErrorDetail = new string('e', BookLimits.ErrorDetail + 1),
        };
        book.Characters.Add(new Character(1, new string('n', CharacterLimits.Name + 1), new string('d', CharacterLimits.ShortDescription + 1), new string('p', CharacterLimits.ChatProfile + 1)));

        book.FindLimitViolations().ShouldBe(
        [
            "title", "fileName", "summary", "errorDetail",
            "characters[0].name", "characters[0].shortDescription", "characters[0].chatProfile",
        ]);
    }

    [Fact]
    public void FindLimitViolations_ReportsTooManyCharacters()
    {
        var book = NewBook();
        for (var position = 1; position <= BookLimits.MaxCharacters + 1; position++)
        {
            book.Characters.Add(new Character(position, "Name", "Description", "Profile"));
        }

        book.FindLimitViolations().ShouldContain("characters");
    }

    [Fact]
    public void FindLimitViolations_AcceptsTextsOfExactlyTheMaximumLength()
    {
        var book = NewBook();
        book.Summary = new string('s', CharacterLimits.Summary);
        book.Characters.Add(new Character(1, new string('n', CharacterLimits.Name), new string('d', CharacterLimits.ShortDescription), new string('p', CharacterLimits.ChatProfile)));

        book.FindLimitViolations().ShouldBeEmpty();
    }
}
