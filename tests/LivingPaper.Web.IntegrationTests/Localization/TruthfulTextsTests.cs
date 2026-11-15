using System.Text.Json;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests.Localization;

// PAPER-58 and PAPER-60: no sentence of the interface promises what the application does not do.
public class TruthfulTextsTests
{
    private static Dictionary<string, string> Load(string language)
    {
        var path = RepositoryFiles.Find($"src/LivingPaper.Web/Resources/{language}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }

    // Reading the examples works without a key, the chat does not: the lead says exactly that.
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void TheExamplesLead_DoesNotPromiseToTryTheApplicationWithoutAKey(string language)
    {
        var lead = Load(language)["examples.lead"];

        lead.ShouldNotContain("try LivingPaper", Case.Insensitive);
        lead.ShouldNotContain("essayer LivingPaper", Case.Insensitive);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void TheSettingsLead_DoesNotPromiseThatAnyProviderThatSpeaksTheOpenAiApiWillDo(string language)
    {
        var lead = Load(language)["settings.lead"];

        lead.ShouldNotContain("will do", Case.Insensitive);
        lead.ShouldNotContain("convient", Case.Insensitive);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("ROADMAP.md")]
    [InlineData("AGENTS.md")]
    public void TheDocumentation_DoesNotPromiseToTryTheApplicationWithoutAKey(string file)
    {
        var text = File.ReadAllText(RepositoryFiles.Find(file));

        text.ShouldNotContain("tried without a key", Case.Insensitive);
        text.ShouldNotContain("try it without a key", Case.Insensitive);
        text.ShouldNotContain("try the application without a key", Case.Insensitive);
        text.ShouldNotContain("try LivingPaper without a key", Case.Insensitive);
    }
}
