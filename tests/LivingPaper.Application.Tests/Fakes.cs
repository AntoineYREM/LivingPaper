using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Application.Tests;

// A language model that answers with what the test says. Streaming is not used by the code under test.
internal sealed class FakeChatModel(Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<string>> reply) : IChatModel
{
    public IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
        reply(messages, cancellationToken);
}

internal sealed class FakeChatModelFactory : IChatModelFactory
{
    public List<(LlmProvider Provider, bool Retry)> Created { get; } = [];

    public List<IReadOnlyList<ChatMessage>> Sent { get; } = [];

    public Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<string>> Reply { get; set; } = (_, _) => Task.FromResult("pong");

    public Exception? CreateFailure { get; set; }

    public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true)
    {
        if (CreateFailure is not null)
        {
            throw CreateFailure;
        }

        Created.Add((provider, retryPassingFailures));
        return new FakeChatModel((messages, cancellationToken) =>
        {
            Sent.Add(messages);
            return Reply(messages, cancellationToken);
        });
    }
}

internal sealed class FakePromptProvider(string text = "Reply with the single word: pong", Exception? failure = null) : IPromptProvider
{
    public List<(string Key, IReadOnlyDictionary<string, string> Variables)> Requests { get; } = [];

    public Task<string> RenderAsync(string key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
    {
        Requests.Add((key, variables));
        return failure is null ? Task.FromResult(text) : Task.FromException<string>(failure);
    }
}

// Keeps the settings like a file would: what is loaded is a copy, so a change only counts once it is saved.
internal sealed class InMemoryLlmSettingsRepository : ILlmSettingsRepository
{
    private List<LlmProvider> _providers = [];
    private string? _analysisId;
    private string? _chatId;
    private List<string> _unreadable = [];

    public int Saves { get; private set; }

    public Exception? LoadFailure { get; set; }

    public Func<Task>? BeforeSave { get; set; }

    public Task<LlmSettings> LoadAsync(CancellationToken cancellationToken) =>
        LoadFailure is null
            ? Task.FromResult(LlmSettings.Restore(_providers, _analysisId, _chatId, _unreadable))
            : Task.FromException<LlmSettings>(LoadFailure);

    public async Task SaveAsync(LlmSettings settings, CancellationToken cancellationToken)
    {
        if (BeforeSave is not null)
        {
            await BeforeSave();
        }

        _providers = [.. settings.Providers];
        _analysisId = settings.AnalysisProviderId;
        _chatId = settings.ChatProviderId;
        _unreadable = [.. settings.UnreadableKeyProviderIds];
        Saves++;
    }

    public void Seed(LlmSettings settings)
    {
        _providers = [.. settings.Providers];
        _analysisId = settings.AnalysisProviderId;
        _chatId = settings.ChatProviderId;
        _unreadable = [.. settings.UnreadableKeyProviderIds];
    }
}

internal sealed class FakeConnectionTester(Func<LlmProvider, Task<ConnectionTestResult>>? test = null) : ILlmConnectionTester
{
    public List<LlmProvider> Tested { get; } = [];

    public Task<ConnectionTestResult> TestAsync(LlmProvider provider, CancellationToken cancellationToken)
    {
        Tested.Add(provider);
        return test is null ? Task.FromResult(ConnectionTestResult.Ok()) : test(provider);
    }
}

internal sealed class FakeModelCatalog(Func<string, string, ModelListResult>? list = null) : IModelCatalog
{
    public List<(string BaseUrl, string ApiKey)> Asked { get; } = [];

    public Task<ModelListResult> ListAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        Asked.Add((baseUrl, apiKey));
        return Task.FromResult(list is null ? ModelListResult.Found(["gpt-5-nano", "gpt-5", "gpt-4o"]) : list(baseUrl, apiKey));
    }
}
