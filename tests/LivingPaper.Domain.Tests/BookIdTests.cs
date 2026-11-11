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

    [Fact]
    public void TryParse_AcceptsUpperCase_AndNormalizesIt()
    {
        var id = BookId.New();

        BookId.TryParse(id.ToString().ToUpperInvariant(), out var parsed).ShouldBeTrue();
        parsed.ToString().ShouldBe(id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\secret")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("01234567-89ab-cdef-0123-456789abcdef")]
    [InlineData("{01234567-89ab-cdef-0123-456789abcdef}")]
    [InlineData("0123456789abcdef0123456789abcdef.json")]
    [InlineData("00000000000000000000000000000000")]
    public void TryParse_RejectsAnythingThatIsNotA32CharacterHexadecimalId(string? text)
    {
        BookId.TryParse(text, out _).ShouldBeFalse();
    }
}
