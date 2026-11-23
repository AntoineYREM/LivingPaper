using System.Runtime.CompilerServices;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Evals.Tests;

// A language model that answers from a function, and remembers what it was asked and which usage meter it was given.
internal sealed class FakeChatModel(Func<IReadOnlyList<ChatMessage>, string> answer) : IChatModel
{
    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public List<ChatUsageMeter?> Meters { get; } = [];

    public async IAsyncEnumerable<string> StreamReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatUsageMeter? usage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Requests.Add(messages);
        Meters.Add(usage);
        var text = answer(messages);
        yield return text[..(text.Length / 2)];
        await Task.Yield();
        yield return text[(text.Length / 2)..];
    }

    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
    {
        Requests.Add(messages);
        Meters.Add(usage);
        return Task.FromResult(answer(messages));
    }
}

internal sealed class FakeChatModelFactory(Func<LlmProvider, IChatModel> create) : IChatModelFactory
{
    public List<LlmProvider> Created { get; } = [];

    public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true)
    {
        Created.Add(provider);
        return create(provider);
    }
}

internal sealed class InMemorySettings(LlmSettings settings) : ILlmSettingsRepository
{
    public int Saves { get; private set; }

    public LlmSettings Current { get; private set; } = settings;

    public Task<LlmSettings> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(LlmSettings.Restore(Current.Providers, Current.AnalysisProviderId, Current.ChatProviderId, Current.UnreadableKeyProviderIds));

    public Task SaveAsync(LlmSettings settings, CancellationToken cancellationToken)
    {
        Saves++;
        Current = settings;
        return Task.CompletedTask;
    }
}

internal sealed class FakeTerminal(bool interactive, params string?[] answers) : ITerminal
{
    private readonly Queue<string?> _answers = new(answers);

    public bool IsInteractive { get; } = interactive;

    public List<string> Output { get; } = [];

    public void WriteLine(string text = "") => Output.Add(text);

    public string? ReadLine(string prompt) => _answers.Count > 0 ? _answers.Dequeue() : null;

    public string? ReadSecret(string prompt) => ReadLine(prompt);
}

internal static class RepoFiles
{
    public static string? Find(string relativePath) => Evals.RepoFiles.Find(relativePath, AppContext.BaseDirectory);
}
