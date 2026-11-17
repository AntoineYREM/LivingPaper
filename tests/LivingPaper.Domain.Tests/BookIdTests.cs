using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Domain.Tests;

public class BookIdTests
{
    [Fact]
    public void New_ProducesA32CharacterLowerCaseHexadecimalText()
    {
        var text = BookId.New().ToString();

        text.Length.ShouldBe(32);
        text.ShouldBe(text.ToLowerInvariant());
        text.ShouldAllBe(c => Uri.IsHexDigit(c));
    }

    [Fact]
    public void New_ProducesDifferentIds()
    {
        BookId.New().ShouldNotBe(BookId.New());
    }

    [Fact]
    public void TryParse_AcceptsWhatToStringProduced()
    {
        var id = BookId.New();

        BookId.TryParse(id.ToString(), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
    }

    [Theory]
    [InlineData("Cyrano de Bergerac", "cyrano-de-bergerac")]
    [InlineData("  Le Malade imaginaire ", "le-malade-imaginaire")]
    [InlineData("Les Misérables", "les-miserables")]
    [InlineData("Alice's Adventures in Wonderland", "alice-s-adventures-in-wonderland")]
    [InlineData("Around the World in 80 Days!!!", "around-the-world-in-80-days")]
    [InlineData("日本語", "book")]
    [InlineData("", "book")]
    [InlineData("---", "book")]
    public void FromTitle_WritesALowerCaseSlug(string title, string expected)
    {
        BookId.FromTitle(title, _ => false).ToString().ShouldBe(expected);
    }

    [Fact]
    public void FromTitle_CutsAVeryLongTitle_WithoutEndingOnAHyphen()
    {
        var id = BookId.FromTitle(string.Join(' ', Enumerable.Repeat("word", 100)), _ => false).ToString();

        id.Length.ShouldBeLessThanOrEqualTo(60);
        id.ShouldNotEndWith("-");
        BookId.TryParse(id, out _).ShouldBeTrue();
    }

    [Fact]
    public void FromTitle_AddsANumber_WhenTheSlugIsTaken()
    {
        var taken = new HashSet<string> { "cyrano", "cyrano-2" };

        BookId.FromTitle("Cyrano", taken.Contains).ToString().ShouldBe("cyrano-3");
    }

    [Fact]
    public void FromTitle_KeepsTheNumberedIdValid_WhenTheTitleIsLong()
    {
        var taken = new HashSet<string> { new('a', 60) };

        var id = BookId.FromTitle(new string('a', 200), taken.Contains).ToString();

        id.ShouldEndWith("-2");
        BookId.TryParse(id, out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("cyrano-de-bergerac")]
    [InlineData("a")]
    [InlineData("1984")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public void TryParse_AcceptsASlug_AndAnOldIdentifier(string text)
    {
        BookId.TryParse(text, out var id).ShouldBeTrue();
        id.ToString().ShouldBe(text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\secret")]
    [InlineData("a/b")]
    [InlineData("-cyrano")]
    [InlineData("cyrano-")]
    [InlineData("cyrano--de-bergerac")]
    [InlineData("cyrano_de_bergerac")]
    [InlineData("Cyrano")]
    [InlineData("cyrano.json")]
    [InlineData("cyrano.steps")]
    [InlineData("cyrano de bergerac")]
    [InlineData("accentué")]
    [InlineData("00000000000000000000000000000000")]
    public void TryParse_RejectsAnythingThatCouldNotBeAFileNameOfOurs(string? text)
    {
        BookId.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryParse_RejectsAnIdentifierOver80Characters()
    {
        BookId.TryParse(new string('a', 81), out _).ShouldBeFalse();
        BookId.TryParse(new string('a', 80), out _).ShouldBeTrue();
    }

    [Fact]
    public void IsOldIdentifier_IsTrueOnlyForAGeneratedHexadecimalId()
    {
        BookId.New().IsOldIdentifier.ShouldBeTrue();
        BookId.FromTitle("Cyrano", _ => false).IsOldIdentifier.ShouldBeFalse();
    }
}
