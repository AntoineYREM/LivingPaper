using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Domain.Tests;

public class CharacterLimitsTests
{
    [Fact]
    public void Clip_ReturnsTheTextUnchanged_WhenItFits()
    {
        CharacterLimits.Clip("A short text", 50).ShouldBe("A short text");
    }

    [Fact]
    public void Clip_ReturnsTheTextUnchanged_WhenItHasExactlyTheMaximumLength()
    {
        CharacterLimits.Clip("12345", 5).ShouldBe("12345");
    }

    [Fact]
    public void Clip_CutsAfterTheLastWholeWordThatFits()
    {
        CharacterLimits.Clip("one two three four", 12).ShouldBe("one two");
    }

    [Fact]
    public void Clip_CutsInsideTheWord_WhenThereIsNoSpaceToCutAt()
    {
        CharacterLimits.Clip("abcdefghij", 4).ShouldBe("abcd");
    }

    [Fact]
    public void Clip_ReturnsAnEmptyText_WhenTheTextIsEmpty()
    {
        CharacterLimits.Clip("", 10).ShouldBeEmpty();
    }

    [Fact]
    public void Clip_NeverReturnsMoreThanTheMaximum()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 500));

        CharacterLimits.Clip(text, CharacterLimits.ShortDescription).Length.ShouldBeLessThanOrEqualTo(CharacterLimits.ShortDescription);
    }
}
