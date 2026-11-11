using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Domain.Tests;

public class PromptTemplateTests
{
    private static Dictionary<string, string> Values(params (string Name, string Value)[] values) =>
        values.ToDictionary(pair => pair.Name, pair => pair.Value);

    [Fact]
    public void Render_ReplacesEveryOccurrenceOfAVariable()
    {
        var template = PromptTemplate.Parse("You are {characterName}. Say hello as {characterName}.");

        template.Render(Values(("characterName", "Argan"))).ShouldBe("You are Argan. Say hello as Argan.");
    }

    [Fact]
    public void Render_ReplacesSeveralVariables()
    {
        var template = PromptTemplate.Parse("{characterName} in {bookTitle}");

        template.Render(Values(("characterName", "Toinette"), ("bookTitle", "Le Malade imaginaire"))).ShouldBe("Toinette in Le Malade imaginaire");
    }

    [Fact]
    public void Render_LeavesJsonBracesAlone()
    {
        const string text = "Answer only with this JSON: {\"characters\":[{\"name\":\"...\"}]} for {bookTitle}. Empty: {} and { spaced }.";

        PromptTemplate.Parse(text).Render(Values(("bookTitle", "Dracula")))
            .ShouldBe("Answer only with this JSON: {\"characters\":[{\"name\":\"...\"}]} for Dracula. Empty: {} and { spaced }.");
    }

    [Fact]
    public void Render_NeverReplacesAgainInsideAValue()
    {
        // A character profile that quotes a placeholder must come out as written, not be expanded.
        var template = PromptTemplate.Parse("Profile: {profile} / Name: {characterName}");

        template.Render(Values(("profile", "I say {characterName} a lot"), ("characterName", "Argan")))
            .ShouldBe("Profile: I say {characterName} a lot / Name: Argan");
    }

    [Fact]
    public void Render_KeepsTheTextWithoutVariableUnchanged()
    {
        PromptTemplate.Parse("Reply with the single word: pong").Render(Values()).ShouldBe("Reply with the single word: pong");
    }

    [Fact]
    public void Render_ThrowsWhenAValueIsMissing_NamingTheVariableButNeverAValue()
    {
        var template = PromptTemplate.Parse("{characterName} and {bookTitle}");

        var exception = Should.Throw<ArgumentException>(() => template.Render(Values(("characterName", "secret value"))));

        exception.Message.ShouldContain("bookTitle");
        exception.Message.ShouldNotContain("secret value");
    }

    [Fact]
    public void Render_IgnoresValuesThatTheTemplateDoesNotUse()
    {
        PromptTemplate.Parse("Hello").Render(Values(("unused", "x"))).ShouldBe("Hello");
    }

    [Fact]
    public void VariableNames_ListsEachVariableOnce_InOrderOfAppearance()
    {
        var template = PromptTemplate.Parse("{b} {a} {b} {c1} {\"json\": 1}");

        template.VariableNames.ShouldBe(["b", "a", "c1"]);
    }

    [Fact]
    public void VariableNames_IsEmpty_WhenThereIsNoVariable()
    {
        PromptTemplate.Parse("{} { } {1abc} {a-b} {a b}").VariableNames.ShouldBeEmpty();
    }

    [Fact]
    public void Text_IsTheOriginalTemplate()
    {
        PromptTemplate.Parse("Hi {name}").Text.ShouldBe("Hi {name}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void Parse_RejectsAnEmptyTemplate(string text)
    {
        Should.Throw<ArgumentException>(() => PromptTemplate.Parse(text));
    }

    [Fact]
    public void FindUnknownVariables_ReturnsTheVariablesThatAreNotAllowed()
    {
        var template = PromptTemplate.Parse("{bookTitle} {oops} {characterName} {oops} {other}");

        template.FindUnknownVariables(["bookTitle", "characterName"]).ShouldBe(["oops", "other"]);
    }

    [Fact]
    public void FindUnknownVariables_IsCaseSensitive()
    {
        PromptTemplate.Parse("{BookTitle}").FindUnknownVariables(["bookTitle"]).ShouldBe(["BookTitle"]);
    }

    [Fact]
    public void FindUnknownVariables_IsEmpty_WhenEverythingIsAllowed()
    {
        PromptTemplate.Parse("{bookTitle}").FindUnknownVariables(["bookTitle", "characterName"]).ShouldBeEmpty();
    }
}
