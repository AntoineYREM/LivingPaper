using System.Net;
using LivingPaper.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The prompts file at the start of the application (PAPER-15).
public class PromptsStartupTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly IReadOnlyDictionary<string, string> NoVariables = new Dictionary<string, string>();

    [Fact]
    public async Task FirstStart_CreatesThePromptsFileInTheDataDirectory()
    {
        using var factory = new LivingPaperFactory();

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        File.Exists(Path.Combine(factory.DataDirectory, "prompts.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task PromptProvider_IsRegistered_AndUsesAnEditOfTheFileWithoutARestart()
    {
        using var factory = new LivingPaperFactory();
        using var client = factory.CreateClient();
        var provider = factory.Services.GetRequiredService<IPromptProvider>();
        var path = Path.Combine(factory.DataDirectory, "prompts.json");

        var before = await provider.RenderAsync(PromptKeys.ConnectionTest, NoVariables, Token);
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path, Token)).Replace(before, "ping, please", StringComparison.Ordinal), Token);
        var after = await provider.RenderAsync(PromptKeys.ConnectionTest, NoVariables, Token);

        before.ShouldNotBeNullOrWhiteSpace();
        after.ShouldBe("ping, please");
    }

    [Fact]
    public async Task AnInvalidPromptsFile_DoesNotStopTheApplication_ItIsLoggedAndReportedWhenAPromptIsUsed()
    {
        using var factory = new LivingPaperFactory();
        var path = Path.Combine(factory.DataDirectory, "prompts.json");
        await File.WriteAllTextAsync(path, "{ SECRET-LOOKING TEXT, not json", Token);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/", Token);
        var provider = factory.Services.GetRequiredService<IPromptProvider>();
        var exception = await Should.ThrowAsync<PromptFileException>(() => provider.RenderAsync(PromptKeys.ConnectionTest, NoVariables, Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await File.ReadAllTextAsync(path, Token)).ShouldBe("{ SECRET-LOOKING TEXT, not json"); // Never overwritten.
        exception.FileName.ShouldBe("prompts.json");
        var errors = factory.Logs.Entries.Where(entry => entry.Level == LogLevel.Error).Select(entry => entry.Message).ToList();
        errors.ShouldContain(message => message.Contains("prompts.json", StringComparison.Ordinal));
        errors.ShouldAllBe(message => !message.Contains("SECRET-LOOKING", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUpdate_AddsAMissingPromptToTheExistingFile_WithoutChangingTheOthers()
    {
        using var factory = new LivingPaperFactory();
        var path = Path.Combine(factory.DataDirectory, "prompts.json");
        await File.WriteAllTextAsync(
            path,
            "{ \"schemaVersion\": 1, \"prompts\": { \"llm.connectionTest\": { \"description\": \"mine\", \"template\": \"my own test message\" } } }",
            Token);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/", Token);
        var provider = factory.Services.GetRequiredService<IPromptProvider>();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await provider.RenderAsync(PromptKeys.ConnectionTest, NoVariables, Token)).ShouldBe("my own test message");
        (await provider.RenderAsync(PromptKeys.BookSummary, new Dictionary<string, string> { [PromptVariables.BookTitle] = "x" }, Token)).ShouldNotBeNullOrWhiteSpace();
    }
}
