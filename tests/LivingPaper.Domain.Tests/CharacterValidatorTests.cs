using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Domain.Tests;

// The rules for changing a character sheet (PAPER-9).
public class CharacterValidatorTests
{
    private static CharacterDraft Valid() => new("Toinette", "A quick-witted maid.", "I am Toinette.\n\nI speak plainly.");

    private static IReadOnlyList<FieldError> Validate(CharacterDraft draft, params string[] otherNames) =>
        CharacterValidator.Validate(draft, otherNames);

    [Fact]
    public void AValidSheet_HasNoError() => Validate(Valid()).ShouldBeEmpty();

    [Fact]
    public void TheProfile_MayHoldLineBreaks_OfEitherKind() =>
        Validate(Valid() with { ChatProfile = "One.\r\nTwo.\rThree.\n\nFour." }).ShouldBeEmpty();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void EveryTextIsRequired(string? empty)
    {
        var errors = Validate(new CharacterDraft(empty, empty, empty));

        errors.ShouldBe(
        [
            new FieldError("name", CharacterErrors.Required),
            new FieldError("shortDescription", CharacterErrors.Required),
            new FieldError("chatProfile", CharacterErrors.Required),
        ]);
    }

    [Fact]
    public void TheLimits_AreThoseOfTheAnalysis_AndExactlyTheLimitIsFine()
    {
        Validate(new CharacterDraft(new string('n', CharacterLimits.Name), new string('d', CharacterLimits.ShortDescription), new string('p', CharacterLimits.ChatProfile))).ShouldBeEmpty();

        Validate(new CharacterDraft(new string('n', CharacterLimits.Name + 1), new string('d', CharacterLimits.ShortDescription + 1), new string('p', CharacterLimits.ChatProfile + 1))).ShouldBe(
        [
            new FieldError("name", CharacterErrors.TooLong),
            new FieldError("shortDescription", CharacterErrors.TooLong),
            new FieldError("chatProfile", CharacterErrors.TooLong),
        ]);
    }

    [Fact]
    public void TheLengthIsThatOfTheTrimmedText() =>
        Validate(Valid() with { Name = "  " + new string('n', CharacterLimits.Name) + "  " }).ShouldBeEmpty();

    [Fact]
    public void ANameAndADescription_CannotHoldALineBreakOrAControlCharacter()
    {
        var errors = Validate(Valid() with { Name = "Toi\nnette", ShortDescription = "A maid.\u0000" });

        errors.ShouldBe(
        [
            new FieldError("name", CharacterErrors.InvalidCharacters),
            new FieldError("shortDescription", CharacterErrors.InvalidCharacters),
        ]);
    }

    [Fact]
    public void TheProfile_CannotHoldAControlCharacterOtherThanALineBreak() =>
        Validate(Valid() with { ChatProfile = "Tab\there" }).ShouldBe([new FieldError("chatProfile", CharacterErrors.InvalidCharacters)]);

    [Theory]
    [InlineData("Argan")]
    [InlineData("  argan ")]
    [InlineData("ARGAN")]
    public void ANameOfAnotherCharacter_IsRefused_IgnoringCaseAndSpaces(string name) =>
        Validate(Valid() with { Name = name }, "Argan", "Béline").ShouldBe([new FieldError("name", CharacterErrors.Taken)]);

    [Fact]
    public void TheErrors_NeverHoldWhatWasTyped() =>
        Validate(new CharacterDraft("secret\nname", "d", "p")).ShouldAllBe(error => !error.Code.Contains("secret", StringComparison.Ordinal));

    [Fact]
    public void ADraft_IsNormalized_SpacesRemoved_LineBreaksAlike()
    {
        var normalized = new CharacterDraft("  Toinette ", " A maid. ", " One.\r\nTwo.\r ").Normalized();

        normalized.ShouldBe(new CharacterDraft("Toinette", "A maid.", "One.\nTwo."));
        new CharacterDraft(null, null, null).Normalized().ShouldBe(new CharacterDraft(string.Empty, string.Empty, string.Empty));
    }
}
