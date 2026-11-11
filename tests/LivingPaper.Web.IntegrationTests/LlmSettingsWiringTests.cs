using System.Net;
using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The language model settings wired into the application (PAPER-5): keys encrypted with keys kept in data/keys, and the
// connection test using the prompt of the prompts file. The page itself comes after the design and the translations.
public class LlmSettingsWiringTests
{
    private const string Key = "sk-end-to-end-secret-key-9876";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static LlmProviderDraft Draft(string name = "OpenAI") => new(name, "https://api.openai.com/v1", Key, "gpt-5-nano");

    // Plays the provider: every model answers "pong". Never a real call.
    private sealed class PongChatModelFactory : IChatModelFactory
    {
        public List<(LlmProvider Provider, bool Retry)> Created { get; } = [];

        public List<string> Messages { get; } = [];

        public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true)
        {
            Created.Add((provider, retryPassingFailures));
            return new PongModel(Messages);
        }

        private sealed class PongModel(List<string> messages) : IChatModel
        {
            public IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> chatMessages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> chatMessages, ChatUsageMeter? usage, CancellationToken cancellationToken)
            {
                messages.AddRange(chatMessages.Select(message => message.Content));
                return Task.FromResult("pong");
            }
        }
    }

    private static LivingPaperFactory NewFactory(PongChatModelFactory pong, string? dataDirectory = null) =>
        LivingPaperFactory.With(dataDirectory, services => services.AddSingleton<IChatModelFactory>(pong));

    [Fact]
    public void TheSettingsServices_AreRegistered()
    {
        using var factory = new LivingPaperFactory();

        factory.Services.GetRequiredService<LlmSettingsService>().ShouldNotBeNull();
        factory.Services.GetRequiredService<ISecretProtector>().ShouldNotBeNull();
        factory.Services.GetRequiredService<ILlmSettingsRepository>().ShouldNotBeNull();
        factory.Services.GetRequiredService<ILlmConnectionTester>().ShouldNotBeNull();
        factory.Services.GetRequiredService<LlmSettingsService>().ShouldBeSameAs(factory.Services.GetRequiredService<LlmSettingsService>());
    }

    [Fact]
    public async Task AddingAModel_TestsItWithThePromptOfThePromptsFile_ThenSavesItWithItsKeyEncrypted()
    {
        var pong = new PongChatModelFactory();
        using var factory = NewFactory(pong);
        var service = factory.Services.GetRequiredService<LlmSettingsService>();

        var result = await service.AddAsync(Draft(), Token);

        result.Succeeded.ShouldBeTrue();
        pong.Created.ShouldHaveSingleItem().Retry.ShouldBeFalse(); // The test is not retried: the error shows at once.
        pong.Messages.ShouldBe(["Reply with the single word: pong"]); // The default prompt of llm.connectionTest.
        var settingsFile = await File.ReadAllTextAsync(Path.Combine(factory.DataDirectory, "settings.json"), Token);
        settingsFile.ShouldNotContain(Key);
        settingsFile.ShouldContain("protectedApiKey");
        settingsFile.ShouldContain("gpt-5-nano");
        Directory.GetFiles(Path.Combine(factory.DataDirectory, "keys"), "*.xml").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task TheConnectionTest_UsesAPromptEditedInThePromptsFile()
    {
        var pong = new PongChatModelFactory();
        using var factory = NewFactory(pong);
        var path = Path.Combine(factory.DataDirectory, "prompts.json");
        using var client = factory.CreateClient(); // Starts the application, which creates prompts.json.
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path, Token)).Replace("Reply with the single word: pong", "Say hello, please", StringComparison.Ordinal), Token);

        await factory.Services.GetRequiredService<LlmSettingsService>().AddAsync(Draft(), Token);

        pong.Messages.ShouldBe(["Say hello, please"]);
    }

    [Fact]
    public async Task AModelAndItsKey_SurviveARestartOfTheApplication()
    {
        var pong = new PongChatModelFactory();
        string dataDirectory;
        using (var first = NewFactory(pong))
        {
            dataDirectory = first.DataDirectory;
            await first.Services.GetRequiredService<LlmSettingsService>().AddAsync(Draft(), Token);

            // The same data directory, started again: what a restart is. The first factory still owns the cleaning.
            using var second = NewFactory(pong, dataDirectory);
            var provider = await second.Services.GetRequiredService<LlmSettingsService>().GetAnalysisProviderAsync(Token);

            provider.ShouldNotBeNull();
            provider.ApiKey.ShouldBe(Key); // Decrypted with the keys found again in data/keys.
            provider.Model.ShouldBe("gpt-5-nano");
            (await second.Services.GetRequiredService<LlmSettingsService>().GetAsync(Token)).IsChatEnabled.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task TheKey_NeverReachesTheLogs()
    {
        var pong = new PongChatModelFactory();
        using var factory = NewFactory(pong);
        var service = factory.Services.GetRequiredService<LlmSettingsService>();

        await service.AddAsync(Draft(), Token);
        await service.GetAsync(Token);
        await service.DeleteAsync((await service.GetAsync(Token)).Providers.Single().Id, Token);

        factory.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains(Key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUnusableSettingsFile_DoesNotStopTheApplication_ButIsReportedWhenTheSettingsAreUsed()
    {
        using var factory = new LivingPaperFactory();
        var path = Path.Combine(factory.DataDirectory, "settings.json");
        await File.WriteAllTextAsync(path, "{ SECRET-LOOKING TEXT, not json", Token);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/", Token);
        var exception = await Should.ThrowAsync<SettingsFileException>(() => factory.Services.GetRequiredService<LlmSettingsService>().GetAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        exception.FileName.ShouldBe("settings.json");
        exception.Message.ShouldNotContain("SECRET-LOOKING");
        (await File.ReadAllTextAsync(path, Token)).ShouldBe("{ SECRET-LOOKING TEXT, not json"); // Never replaced.
    }

    [Fact]
    public async Task TheDataDirectory_IsIgnoredByGit_SoNoKeyOrSettingsFileCanBeCommitted()
    {
        var gitignore = await File.ReadAllTextAsync(FindRepositoryFile(".gitignore"), Token);

        gitignore.Split('\n').Select(line => line.Trim()).ShouldContain("data/");
    }

    private static string FindRepositoryFile(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{name} was not found above the test directory.");
    }
}
