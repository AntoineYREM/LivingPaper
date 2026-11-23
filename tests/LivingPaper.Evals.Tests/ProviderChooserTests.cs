using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Evals.Tests;

public class ProviderChooserTests
{
    private static readonly LlmProvider Cheap = new("id-1", "Cheap", "https://a.example/v1", "key-a", "model-a");
    private static readonly LlmProvider Strong = new("id-2", "Strong", "https://b.example/v1", "key-b", "model-b");

    private static InMemorySettings Settings(params LlmProvider[] providers) => new(LlmSettings.Restore(providers, null, null));

    private static ProviderChooser Chooser(FakeTerminal terminal, InMemorySettings settings) =>
        new(terminal, settings, new ProviderAdder(terminal, ServiceFor(settings)));

    private static LlmSettingsService ServiceFor(InMemorySettings settings) =>
        new(settings, new NoTester(), new NoCatalog(), new PendingProviderStore(TimeProvider.System));

    private sealed class NoTester : ILlmConnectionTester
    {
        public Task<ConnectionTestResult> TestAsync(LlmProvider provider, CancellationToken cancellationToken) =>
            Task.FromResult(ConnectionTestResult.Ok());
    }

    private sealed class NoCatalog : IModelCatalog
    {
        public Task<ModelListResult> ListAsync(string baseUrl, string apiKey, CancellationToken cancellationToken) =>
            Task.FromResult(ModelListResult.Failed(ConnectionFailure.NoModelList));
    }

    [Fact]
    public async Task ChooseAsync_FindsAModelByNameWithoutAskingAnything()
    {
        var terminal = new FakeTerminal(interactive: false);

        var chosen = await Chooser(terminal, Settings(Cheap, Strong)).ChooseAsync("chat", "strong", CancellationToken.None);

        chosen.ShouldBe(Strong);
        terminal.Output.ShouldBeEmpty();
    }

    [Fact]
    public async Task ChooseAsync_RefusesAnUnknownName_ListingTheNames()
    {
        var act = () => Chooser(new FakeTerminal(false), Settings(Cheap, Strong)).ChooseAsync("judge", "Nope", CancellationToken.None);

        var error = await Should.ThrowAsync<EvalException>(act);
        error.Message.ShouldContain("Cheap");
        error.Message.ShouldContain("Strong");
        error.Message.ShouldNotContain("key-a");
    }

    [Fact]
    public async Task ChooseAsync_RefusesAModelWhoseKeyCannotBeRead()
    {
        var settings = new InMemorySettings(LlmSettings.Restore([Cheap], null, null, unreadableKeyProviderIds: [Cheap.Id]));

        var act = () => Chooser(new FakeTerminal(false), settings).ChooseAsync("chat", "Cheap", CancellationToken.None);

        (await Should.ThrowAsync<EvalException>(act)).Message.ShouldContain("key");
    }

    [Fact]
    public async Task ChooseAsync_WithNoTerminalAndNoName_ExplainsHowToChoose()
    {
        var act = () => Chooser(new FakeTerminal(false), Settings(Cheap)).ChooseAsync("chat", null, CancellationToken.None);

        var error = await Should.ThrowAsync<EvalException>(act);
        error.Message.ShouldContain("--chat");
        error.Message.ShouldContain("EVAL_CHAT_PROVIDER");
    }

    [Fact]
    public async Task ChooseAsync_OffersTheListAndTakesTheNumberTyped()
    {
        var terminal = new FakeTerminal(true, "2");

        var chosen = await Chooser(terminal, Settings(Cheap, Strong)).ChooseAsync("judge", null, CancellationToken.None);

        chosen.ShouldBe(Strong);
        terminal.Output.ShouldContain(line => line.Contains("1) Cheap", StringComparison.Ordinal) && line.Contains("model-a", StringComparison.Ordinal));
        terminal.Output.ShouldContain(line => line.Contains("2) Strong", StringComparison.Ordinal));
        terminal.Output.ShouldNotContain(line => line.Contains("key-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChooseAsync_SaysSoWhenTheListIsEmpty_SoAWrongDataFolderIsNoticed()
    {
        var terminal = new FakeTerminal(true, "1", "Local", "http://localhost:11434/v1", string.Empty, "llama3", "1");

        await Chooser(terminal, Settings()).ChooseAsync("chat", null, CancellationToken.None);

        terminal.Output.ShouldContain(line => line.Contains("DATA_DIRECTORY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChooseAsync_DoesNotWarnWhenThereAreModels()
    {
        var terminal = new FakeTerminal(true, "1");

        await Chooser(terminal, Settings(Cheap)).ChooseAsync("chat", null, CancellationToken.None);

        terminal.Output.ShouldNotContain(line => line.Contains("DATA_DIRECTORY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChooseAsync_AsksAgainAfterAnInvalidAnswer()
    {
        var terminal = new FakeTerminal(true, "9", "abc", "1");

        var chosen = await Chooser(terminal, Settings(Cheap, Strong)).ChooseAsync("chat", null, CancellationToken.None);

        chosen.ShouldBe(Cheap);
    }

    [Fact]
    public async Task ChooseAsync_StopsWhenTheInputEnds()
    {
        var act = () => Chooser(new FakeTerminal(true), Settings(Cheap)).ChooseAsync("chat", null, CancellationToken.None);

        await Should.ThrowAsync<EvalException>(act);
    }

    [Fact]
    public async Task ChooseAsync_CanAddAModelWhenTheListIsEmpty()
    {
        var settings = Settings();
        // No list of models at the provider: name, address and key are typed, then the model, which is tested and saved.
        var terminal = new FakeTerminal(true, "1", "Local", "http://localhost:11434/v1", string.Empty, "llama3", "1");

        var chosen = await Chooser(terminal, settings).ChooseAsync("chat", null, CancellationToken.None);

        chosen.Name.ShouldBe("Local");
        chosen.Model.ShouldBe("llama3");
        settings.Current.Providers.Select(provider => provider.Name).ShouldBe(["Local"]);
    }

    [Fact]
    public async Task ChooseAsync_NeverChangesWhatTheSiteChose()
    {
        var settings = new InMemorySettings(LlmSettings.Restore([Cheap, Strong], analysisProviderId: Cheap.Id, chatProviderId: Cheap.Id));

        await Chooser(new FakeTerminal(false), settings).ChooseAsync("chat", "Strong", CancellationToken.None);

        settings.Saves.ShouldBe(0);
        settings.Current.ChatProviderId.ShouldBe(Cheap.Id);
    }
}
