using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Evals.Tests;

// The real chat of the application, played with the model the tool chose and the settings of the site left alone.
public class ChatSetupTests
{
    private static readonly LlmProvider Chosen = new("id-2", "Strong", "https://b.example/v1", "key-b", "model-b");
    private static readonly LlmProvider SiteChoice = new("id-1", "Cheap", "https://a.example/v1", "key-a", "model-a");

    private sealed class Prompts : IPromptProvider
    {
        public Task<string> RenderAsync(string key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken) =>
            Task.FromResult($"{key}:{variables.GetValueOrDefault(PromptVariables.CharacterName)}");
    }

    private static InMemorySettings Site() => new(LlmSettings.Restore([SiteChoice, Chosen], SiteChoice.Id, SiteChoice.Id));

    [Fact]
    public async Task ChatOverride_ReturnsTheSettingsWithTheChatModelReplaced()
    {
        var repository = new ChatOverrideSettings(Site(), Chosen.Id);

        var settings = await repository.LoadAsync(CancellationToken.None);

        settings.ChatProvider.ShouldBe(Chosen);
        settings.AnalysisProviderId.ShouldBe(SiteChoice.Id);
    }

    [Fact]
    public async Task ChatOverride_NeverWritesAnything()
    {
        var site = Site();
        var repository = new ChatOverrideSettings(site, Chosen.Id);

        var settings = await repository.LoadAsync(CancellationToken.None);

        await Should.ThrowAsync<NotSupportedException>(() => repository.SaveAsync(settings, CancellationToken.None));

        site.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Replier_AsksTheModelTheToolChose_AndJoinsTheReplyPieces()
    {
        var model = new FakeChatModel(_ => "Je suis Emma.");
        var factory = new FakeChatModelFactory(_ => model);
        var settings = new LlmSettingsService(new ChatOverrideSettings(Site(), Chosen.Id), new NoTester(), new NoCatalog(), new PendingProviderStore(TimeProvider.System));
        var book = new Book(BookId.FromTitle("Madame Bovary", _ => false), "Madame Bovary", "x.epub", DateTimeOffset.UnixEpoch);
        var emma = new Character(1, "Emma Bovary", "short", "profile");
        var replier = new ChatServiceReplier(new CharacterChatService(settings, factory, new Prompts()), book, emma);

        var answer = await replier.ReplyAsync(new EvalQuestion("q-1", "normal", "Hello?", ""), CancellationToken.None);

        answer.Reply.ShouldBe("Je suis Emma.");
        answer.Trace.ShouldBeNull();
        factory.Created.ShouldBe([Chosen]);
        var request = model.Requests.Single();
        request[0].Role.ShouldBe(ChatRole.System);
        request[0].Content.ShouldContain("Emma Bovary");
        request[^1].ShouldBe(new ChatMessage(ChatRole.User, "Hello?"));
    }

    internal sealed class NoTester : ILlmConnectionTester
    {
        public Task<ConnectionTestResult> TestAsync(LlmProvider provider, CancellationToken cancellationToken) =>
            Task.FromResult(ConnectionTestResult.Ok());

        public Task<ConnectionTestResult> TestEmbeddingAsync(LlmProvider provider, CancellationToken cancellationToken) =>
            Task.FromResult(ConnectionTestResult.Ok());
    }

    internal sealed class NoCatalog : IModelCatalog
    {
        public Task<ModelListResult> ListAsync(string baseUrl, string apiKey, CancellationToken cancellationToken) =>
            Task.FromResult(ModelListResult.Failed(ConnectionFailure.NoModelList));
    }
}
