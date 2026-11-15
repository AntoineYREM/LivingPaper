using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

public class PromptKeysTests
{
    private static readonly string[] NineKeys =
    [
        "analysis.bookSummary", "analysis.characterProfile", "analysis.mergeCharacters", "analysis.readPart",
        "analysis.readPartSimple", "analysis.storySummary", "chat.story", "chat.system", "llm.connectionTest",
    ];

    [Fact]
    public void All_ListsTheNinePromptsOfTheApplication()
    {
        PromptKeys.All.Order().ShouldBe(NineKeys);
    }

    [Theory]
    [InlineData("analysis.readPart", new[] { "bookTitle" })]
    [InlineData("analysis.readPartSimple", new[] { "bookTitle" })]
    [InlineData("analysis.mergeCharacters", new[] { "bookTitle", "maxCharacters" })]
    [InlineData("analysis.characterProfile", new[] { "bookTitle", "characterName", "shortDescription" })]
    [InlineData("analysis.bookSummary", new[] { "bookTitle" })]
    [InlineData("analysis.storySummary", new[] { "bookTitle", "maxWords" })]
    [InlineData("chat.system", new[] { "bookTitle", "characterName", "profile", "story" })]
    [InlineData("chat.story", new[] { "bookTitle", "characterName", "storySummary" })]
    [InlineData("llm.connectionTest", new string[0])]
    public void VariablesOf_ReturnsTheVariablesAPromptMayUse(string key, string[] expected)
    {
        PromptKeys.VariablesOf(key).Order().ShouldBe(expected.Order());
    }

    [Fact]
    public void VariablesOf_ThrowsForAnUnknownKey()
    {
        Should.Throw<ArgumentException>(() => PromptKeys.VariablesOf("analysis.nothing"));
    }

    [Fact]
    public void IsKnown_RecognizesOnlyTheNineKeys()
    {
        PromptKeys.All.ShouldAllBe(key => PromptKeys.IsKnown(key));
        PromptKeys.IsKnown("analysis.nothing").ShouldBeFalse();
        PromptKeys.IsKnown("").ShouldBeFalse();
    }

    [Fact]
    public void EveryVariableName_IsRecognizedAsAVariableByTheTemplateSyntax()
    {
        // A variable that the template parser would not see as one could never be used in a prompt.
        foreach (var name in PromptKeys.All.SelectMany(PromptKeys.VariablesOf).Distinct())
        {
            PromptTemplate.Parse("{" + name + "}").VariableNames.ShouldBe([name]);
        }
    }
}
