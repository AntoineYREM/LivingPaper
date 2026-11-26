using System.Collections.Concurrent;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Application.Tests;

// The pieces of an analysis, in memory: a prompt provider whose prompts name their key (so the simulated model knows which
// step it is asked for), a store of steps, and a language model that plays the provider.

internal sealed class KeyedPromptProvider : IPromptProvider
{
    public Exception? Failure { get; set; }

    public int Renders { get; private set; }

    // "[analysis.readPart] title=The Lighthouse name=Argan max=10": the model routes on the key and reads the variables.
    public Task<string> RenderAsync(string key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
    {
        Renders++;
        if (Failure is not null)
        {
            return Task.FromException<string>(Failure);
        }

        var values = string.Join(' ', variables.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
        return Task.FromResult($"[{key}] {values}");
    }
}

internal sealed class InMemoryAnalysisStepStore : IAnalysisStepStore
{
    private readonly ConcurrentDictionary<(BookId, string), string> _steps = new();

    public IReadOnlyDictionary<string, string> Of(BookId id) =>
        _steps.Where(pair => pair.Key.Item1 == id).ToDictionary(pair => pair.Key.Item2, pair => pair.Value);

    public Task SaveAsync(BookId bookId, string key, string result, CancellationToken cancellationToken)
    {
        _steps[(bookId, key)] = result;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(BookId bookId, CancellationToken cancellationToken) =>
        Task.FromResult(Of(bookId));

    public Task DeleteAllAsync(BookId bookId, CancellationToken cancellationToken)
    {
        foreach (var key in _steps.Keys.Where(key => key.Item1 == bookId))
        {
            _steps.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }
}

// What the analysis asked the model, and when.
internal sealed record ModelCall(string Step, string System, string User)
{
    public bool IsPart => Step is "analysis.readPart" or "analysis.readPartSimple";
}

// Plays the provider. The handler answers (or throws) for each call; by default the model answers like a good one. Counts
// the calls and how many ran at the same time, and reports tokens to the meter it is given, like a real client.
internal sealed class ScriptedAnalysisModelFactory : IChatModelFactory
{
    private readonly ConcurrentQueue<ModelCall> _calls = new();
    private int _running;
    private int _maxRunning;

    public ScriptedAnalysisModelFactory() => Handler = DefaultReply;

    public IReadOnlyList<ModelCall> Calls => [.. _calls];

    public int MaxRunningAtOnce => Volatile.Read(ref _maxRunning);

    public TimeSpan Delay { get; set; }

    public Func<ModelCall, int, Task<string>> Handler { get; set; }

    public int CallsOf(string step) => _calls.Count(call => call.Step == step);

    public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true) => new Model(this);

    public static string Note(string name, int importance = 8) =>
        $$"""{"name":"{{name}}","otherNames":[],"role":"{{name}} acts.","personality":"personality of {{name}}","speech":"speech of {{name}}","events":"events of {{name}}","importance":{{importance}}}""";

    // The scenes of a part: one, with every character present. The summary names the first one so a test can follow it.
    public static string Scene(string summary, params string[] present) =>
        $$"""{"summary":"{{summary}}","present":[{{string.Join(',', present.Select(name => $"\"{name}\""))}}]}""";

    // A part answered like a good model: the characters and one scene.
    public static string PartReply(params string[] names) =>
        "{\"characters\":[" + string.Join(',', names.Select(name => Note(name))) + "],\"scenes\":[" + Scene("A scene happens.", names) + "]}";

    // A part with no scenes (a model that ignores that part of the prompt).
    public static string PartReplyWithoutScenes(params string[] names) => "{\"characters\":[" + string.Join(',', names.Select(name => Note(name))) + "]}";

    public static string MergeReply(params string[] names) =>
        "{\"characters\":[" + string.Join(',', names.Select(name => $$"""{"name":"{{name}}","shortDescription":"{{name}} is in the story."}""")) + "]}";

    // The default answers of a model that does what the prompts ask.
    public static Task<string> DefaultReply(ModelCall call, int _) => Task.FromResult(call.Step switch
    {
        "analysis.readPart" or "analysis.readPartSimple" => PartReply("Argan", "Toinette"),
        "analysis.mergeCharacters" => MergeReply("Argan", "Toinette"),
        "analysis.characterProfile" => "I am " + NameOf(call.System) + ". My profile.",
        "analysis.bookSummary" => "France, 1673. A satirical comedy.",
        "analysis.storySummary" => "Scene 1. A scene happens. Present: Argan, Toinette.",
        "analysis.plotSummary" => "The plot, in short.",
        _ => "?",
    });

    // "[analysis.characterProfile] bookTitle=T characterName=Argan shortDescription=..." -> "Argan"
    public static string NameOf(string system)
    {
        const string marker = "characterName=";
        var start = system.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = system.IndexOf(" shortDescription=", start, StringComparison.Ordinal);
        return system[start..end];
    }

    private sealed class Model(ScriptedAnalysisModelFactory owner) : IChatModel
    {
        public IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
        {
            var system = messages.Single(message => message.Role == ChatRole.System).Content;
            var user = messages.Single(message => message.Role == ChatRole.User).Content;
            var step = system[1..system.IndexOf(']', StringComparison.Ordinal)];
            var call = new ModelCall(step, system, user);
            owner._calls.Enqueue(call);
            var number = owner._calls.Count(other => other.Step == step);

            var running = Interlocked.Increment(ref owner._running);
            int seen;
            while (running > (seen = Volatile.Read(ref owner._maxRunning)) && Interlocked.CompareExchange(ref owner._maxRunning, running, seen) != seen)
            {
            }

            try
            {
                if (owner.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(owner.Delay, cancellationToken);
                }

                usage?.Add(100, 20, 50);
                return await owner.Handler(call, number);
            }
            finally
            {
                Interlocked.Decrement(ref owner._running);
            }
        }
    }
}

// A scheduler that remembers what it was given: the worker of the application is replaced by this in the tests of the service.
internal sealed class FakeAnalysisScheduler : IAnalysisScheduler
{
    private readonly List<(BookShelf Shelf, BookId Id)> _queued = [];

    // The books of the library waiting here.
    public IReadOnlyList<BookId> Queued => [.. _queued.Where(entry => entry.Shelf == BookShelf.Library).Select(entry => entry.Id)];

    public List<BookId> Cancelled { get; } = [];

    // The books queued to have their passages indexed, those of the examples apart.
    public List<BookId> Indexing { get; } = [];

    public List<BookId> IndexingExamples { get; } = [];

    // The books queued to have the vectors of their file checked.
    public List<BookId> Checks { get; } = [];

    public bool Enqueue(BookId bookId) => Add(BookShelf.Library, bookId);

    public bool EnqueueIndexing(BookId bookId, BookShelf shelf = BookShelf.Library)
    {
        if (!Add(shelf, bookId))
        {
            return false;
        }

        (shelf == BookShelf.Library ? Indexing : IndexingExamples).Add(bookId);
        return true;
    }

    public bool EnqueueCheck(BookId bookId)
    {
        if (!Add(BookShelf.Library, bookId))
        {
            return false;
        }

        Checks.Add(bookId);
        return true;
    }

    public bool IsQueued(BookId bookId, BookShelf shelf = BookShelf.Library) => _queued.Contains((shelf, bookId));

    public Task CancelAsync(BookId bookId, CancellationToken cancellationToken)
    {
        Cancelled.Add(bookId);
        _queued.Remove((BookShelf.Library, bookId));
        return Task.CompletedTask;
    }

    private bool Add(BookShelf shelf, BookId bookId)
    {
        if (_queued.Contains((shelf, bookId)))
        {
            return false;
        }

        _queued.Add((shelf, bookId));
        return true;
    }
}

// A clock the test moves by hand.
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
