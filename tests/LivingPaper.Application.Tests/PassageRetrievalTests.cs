using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

// PAPER-86: with an indexed book, the chat looks for the passages that answer the last message and gives them to the
// character in place of the whole story; whatever fails, the character still answers.
public sealed class PassageRetrievalTests : IDisposable
{
    private readonly InMemoryLlmSettingsRepository _settingsRepository = new();
    private readonly LlmSettingsService _settings;
    private readonly ChatAndRewriteFactory _models = new();
    private readonly FakeEmbeddingModelFactory _embeddings = new();
    private readonly FakePassageIndex _index = new();
    private readonly KeyedPromptProvider _prompts = new();
    private readonly Book _book = new(BookId.New(), "Madame Bovary", "bovary.epub", DateTimeOffset.UtcNow)
    {
        Status = BookStatus.Done,
        StorySummary = "Scene 1. The whole story.",
        PlotSummary = "The plot in short.",
    };

    private readonly Character _character = new(1, "Emma Bovary", "The heroine.", "I am Emma.");
    // Most tests of this class are about the writing of the query: they ask for it, whatever the default is.
    private RagOptions _options = new() { Mode = QueryMode.Rewrite, Passages = 3 };

    public PassageRetrievalTests()
    {
        Choose("bge-m3");
        _settings = new LlmSettingsService(_settingsRepository, new FakeConnectionTester(), new FakeModelCatalog(), new PendingProviderStore(TimeProvider.System));
        _index.Search = (_, count) => [.. new[] { Found(79, 0.3), Found(2, 0.4), Found(102, 0.5), Found(4, 0.6) }.Take(count)];
    }

    public void Dispose() => _settings.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private void Choose(string? embeddingModel)
    {
        var providers = new List<LlmProvider> { new("p1", "Chat", "https://api.openai.com/v1", "sk-chat-secret-key", "chat-model") };
        if (embeddingModel is not null)
        {
            providers.Add(new LlmProvider("e1", "Vectors", "http://localhost:11434/v1", string.Empty, embeddingModel));
        }

        _settingsRepository.Seed(LlmSettings.Restore(providers, null, "p1", embeddingProviderId: embeddingModel is null ? null : "e1"));
    }

    private static FoundPassage Found(int number, double distance) => new(
        new StoredPassage(number, $"Text of passage {number}.", [new PassageScene($"Scene of {number}.", ["Emma"])]), distance);

    private BookKey Key => new(BookShelf.Library, _book.Id.ToString());

    private async Task IndexBookAsync(string model = "bge-m3")
    {
        await _index.SavePassagesAsync(Key, [new StoredPassage(1, "A passage.", [])], Token);
        await _index.SaveVectorsAsync(Key, model, [[1, 0]], Token);
    }

    private List<ChatNotice> Notices { get; } = [];

    private async Task<string> ChatAsync(params ChatMessage[] conversation)
    {
        var retriever = new PassageRetriever(_index, _settings, _embeddings, _models, _prompts, _options);
        var service = new CharacterChatService(_settings, _models, _prompts, retriever);
        await foreach (var _ in service.ReplyAsync(
            _book, _character, conversation.Length > 0 ? conversation : [User("Do you remember the ball?")], Notices.Add, Token))
        {
        }

        return _models.Streamed.Single()[0].Content;
    }

    private static ChatMessage User(string text) => new(ChatRole.User, text);

    private static ChatMessage Said(string text) => new(ChatRole.Assistant, text);

    private string Query => _embeddings.Sent.ShouldHaveSingleItem().ShouldHaveSingleItem();

    private static void ShouldChatWithTheStory(string system)
    {
        system.ShouldContain("story=[chat.story]");
        system.ShouldNotContain("[chat.passages]");
    }

    // ---- When the chat goes as before ----

    [Theory]
    [InlineData("no model for vectorization")]
    [InlineData("no passages")]
    [InlineData("no vectors")]
    [InlineData("another model")]
    [InlineData("unusable index")]
    public async Task WithoutAnIndexForTheModelChosen_TheChatGoesAsBefore_WithoutANotice(string situation)
    {
        switch (situation)
        {
            case "no model for vectorization":
                await IndexBookAsync();
                Choose(null);
                break;
            case "no vectors":
                await _index.SavePassagesAsync(Key, [new StoredPassage(1, "A passage.", [])], Token);
                break;
            case "another model":
                await IndexBookAsync("other-model");
                break;
            case "unusable index":
                await IndexBookAsync();
                _index.Problem = PassageIndexProblem.FileUnusable;
                break;
        }

        ShouldChatWithTheStory(await ChatAsync());

        Notices.ShouldBeEmpty();
        _embeddings.Sent.ShouldBeEmpty();
        _models.Rewrites.ShouldBeEmpty();
    }

    // PAPER-88: vectors that came with a file are not used before they are checked.
    [Fact]
    public async Task VectorsThatWereNotChecked_AreNotSearched()
    {
        await IndexBookAsync();
        _index.Unchecked.Add(Key);

        ShouldChatWithTheStory(await ChatAsync());

        Notices.ShouldBeEmpty();
        _embeddings.Sent.ShouldBeEmpty();
    }

    // PAPER-88: an example is searched in its own index, never in that of a book of the library with the same address.
    [Theory]
    [InlineData(BookShelf.Examples, true)]
    [InlineData(BookShelf.Library, false)]
    public async Task ThePassagesAreThoseOfTheShelfOfTheBook(BookShelf shelf, bool found)
    {
        var example = new BookKey(BookShelf.Examples, _book.Id.ToString());
        await _index.SavePassagesAsync(example, [new StoredPassage(1, "A passage.", [])], Token);
        await _index.SaveVectorsAsync(example, "bge-m3", [[1, 0]], Token);
        var retriever = new PassageRetriever(_index, _settings, _embeddings, _models, _prompts, _options);

        await foreach (var _ in new CharacterChatService(_settings, _models, _prompts, retriever)
            .ReplyAsync(_book, _character, [User("Hi")], shelf, Notices.Add, Token))
        {
        }

        _models.Streamed.Single()[0].Content.Contains("[chat.passages]", StringComparison.Ordinal).ShouldBe(found);
    }

    [Fact]
    public async Task AChatServiceWithNoRetriever_ChatsWithTheStory()
    {
        await IndexBookAsync();

        await foreach (var _ in new CharacterChatService(_settings, _models, _prompts).ReplyAsync(_book, _character, [User("Hi")], Token))
        {
        }

        ShouldChatWithTheStory(_models.Streamed.Single()[0].Content);
    }

    [Fact]
    public async Task TheSameModelUnderItsLatestTag_IsTheSameModel()
    {
        await IndexBookAsync("BGE-M3:latest");

        (await ChatAsync()).ShouldContain("[chat.passages]");
    }

    // ---- The passages in the prompt ----

    [Fact]
    public async Task TheCharacterGetsTheShortPlot_AndThePassagesFound_InTheOrderOfTheBook_InPlaceOfTheStory()
    {
        await IndexBookAsync();

        var system = await ChatAsync();

        system.ShouldStartWith("[chat.system] bookTitle=Madame Bovary characterName=Emma Bovary profile=I am Emma. story=[chat.passages] ");
        system.ShouldContain("plotSummary=The plot in short.");
        system.ShouldContain(
            "passages=[Scene: Scene of 2. | Present: Emma]\nText of passage 2.\n\n[Scene: Scene of 79. | Present: Emma]\nText of passage 79.\n\n[Scene: Scene of 102. | Present: Emma]\nText of passage 102.");
        system.ShouldNotContain("passage 4."); // Three were asked for.
        system.ShouldNotContain("The whole story");
        Notices.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheNumberOfPassages_IsASetting()
    {
        await IndexBookAsync();
        _options = new RagOptions { Mode = QueryMode.Rewrite, Passages = 1 };

        var system = await ChatAsync();

        system.ShouldContain("passage 79.");
        system.ShouldNotContain("passage 2.");
    }

    [Fact]
    public async Task ABookWithNoPlotNorStory_GetsThePassagesAlone()
    {
        await IndexBookAsync();
        _book.PlotSummary = null;
        _book.StorySummary = null;

        var system = await ChatAsync();

        system.ShouldContain("[chat.passages]");
        system.ShouldEndWith("plotSummary=");
    }

    // ---- The query ----

    private static readonly ChatMessage[] Conversation =
    [
        User("one"), Said("two"), User("three"), Said("four"), User("Do you remember the ball?"),
    ];

    // PAPER-94: the earlier messages drown a new subject, so the last message is searched on its own too
    // (docs/experiments/admitting-not-knowing.md).
    private const string Recent = "two\nthree\nfour\nDo you remember the ball?";
    private const string LastMessage = "Do you remember the ball?";

    // The counts asked of the index, by the text that was searched (the fake vector of a text starts with its length).
    private List<(string Search, int Count)> SearchTwice(int[] recentFound, int[] lastFound)
    {
        var asked = new List<(string, int)>();
        _index.Search = (vector, count) =>
        {
            var recent = (int)vector[0] == Recent.Length;
            asked.Add((recent ? "recent" : "last", count));
            return [.. (recent ? recentFound : lastFound).Select(number => Found(number, 0.3)).Take(count)];
        };
        return asked;
    }

    private static List<int> PassagesIn(string system) =>
        [.. Regex.Matches(system, @"Text of passage (\d+)\.").Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))];

    [Fact]
    public async Task ByDefault_TheRecentMessagesAndTheLastOneAreSearched_InOneCall_ForOneAndThreePassages()
    {
        await IndexBookAsync();
        _options = new RagOptions();
        var asked = SearchTwice([99, 98], [79, 2, 102, 4, 5]);

        var system = await ChatAsync(Conversation);

        _embeddings.Sent.ShouldHaveSingleItem().ShouldBe([Recent, LastMessage]);
        asked.ShouldBe([("recent", 1), ("last", 4)]);
        PassagesIn(system).ShouldBe([2, 79, 99, 102]); // One of the recent messages, three of the last one, in the order of the book.
        _models.Rewrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task APassageThatBothSearchesFind_IsGivenOnce_AndTheNextOfTheLastMessageTakesItsPlace()
    {
        await IndexBookAsync();
        _options = new RagOptions();
        SearchTwice([2], [79, 2, 102, 4, 5]);

        PassagesIn(await ChatAsync(Conversation)).ShouldBe([2, 4, 79, 102]);
    }

    [Fact]
    public async Task WithOnePassage_TheLastMessageAloneIsSearched()
    {
        await IndexBookAsync();
        _options = new RagOptions { Passages = 1 };
        var asked = SearchTwice([99], [79, 2]);

        var system = await ChatAsync(Conversation);

        Query.ShouldBe(LastMessage);
        asked.ShouldBe([("last", 1)]);
        PassagesIn(system).ShouldBe([79]);
    }

    [Fact]
    public async Task TheFirstMessage_IsSearchedOnce_ForAllThePassages()
    {
        await IndexBookAsync();
        _options = new RagOptions();
        var asked = SearchTwice([99], [79, 2, 102, 4, 5]);

        var system = await ChatAsync(User(LastMessage));

        Query.ShouldBe(LastMessage);
        asked.ShouldBe([("last", 4)]);
        PassagesIn(system).ShouldBe([2, 4, 79, 102]);
    }

    [Fact]
    public async Task TheRewriteMode_HasTheChatModelWriteTheQuery_FromTheRecentMessages()
    {
        await IndexBookAsync();
        _models.Rewrite = _ => Task.FromResult("  \"the ball at the castle,\n the Viscount\"  ");

        await ChatAsync(Conversation);

        var rewrite = _models.Rewrites.ShouldHaveSingleItem();
        rewrite[0].ShouldBe(new ChatMessage(ChatRole.System, "[chat.searchQuery] bookTitle=Madame Bovary characterName=Emma Bovary"));
        rewrite[1].Content.ShouldBe("Emma Bovary: two\nReader: three\nEmma Bovary: four\nReader: Do you remember the ball?");
        Query.ShouldBe("the ball at the castle, the Viscount");
        _models.RetryFlags.ShouldContain(false); // The rewriting is not retried: the reader is waiting.
        _embeddings.Created.ShouldHaveSingleItem().Retry.ShouldBeFalse();
    }

    [Fact]
    public async Task TheRecentMode_SearchesWithTheMessageAndTheThreeBeforeIt()
    {
        await IndexBookAsync();
        _options = new RagOptions { Mode = QueryMode.Recent };

        await ChatAsync(Conversation);

        _embeddings.Sent.ShouldHaveSingleItem().ShouldBe([Recent, LastMessage]);
        _models.Rewrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheLastMode_SearchesWithTheMessageAlone()
    {
        await IndexBookAsync();
        _options = new RagOptions { Mode = QueryMode.Last };

        await ChatAsync(Conversation);

        Query.ShouldBe("Do you remember the ball?");
        _models.Rewrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task ALongQuery_IsLimited()
    {
        await IndexBookAsync();
        _models.Rewrite = _ => Task.FromResult(string.Concat(Enumerable.Repeat("ball ", 500)));

        await ChatAsync();

        Query.Length.ShouldBeLessThanOrEqualTo(PassageRetriever.QueryLetters);
    }

    // ---- When something fails ----

    [Theory]
    [InlineData("unreachable")]
    [InlineData("filtered")]
    [InlineData("empty")]
    [InlineData("slow")]
    [InlineData("prompt")]
    public async Task ARewritingThatFails_SearchesWithTheRecentMessages_AndSaysSo(string failure)
    {
        await IndexBookAsync();
        _options = new RagOptions { Mode = QueryMode.Rewrite, RewriteTimeout = TimeSpan.FromMilliseconds(100) };
        _models.Rewrite = failure switch
        {
            "unreachable" => _ => throw new ChatProviderException(ChatFailureKind.Unreachable, "down"),
            "filtered" => _ => throw new ContentFilteredException(),
            "empty" => _ => Task.FromResult("  \"\"  "),
            "slow" => async token => { await Task.Delay(TimeSpan.FromSeconds(30), token); return "late"; },
            _ => _ => throw new PromptFileException("prompts.json", PromptKeys.SearchQuery, "broken"),
        };

        var system = await ChatAsync(Conversation);

        _embeddings.Sent.ShouldHaveSingleItem().ShouldBe([Recent, LastMessage]);
        system.ShouldContain("[chat.passages]");
        Notices.ShouldBe([ChatNotice.RewriteFailed]);
    }

    [Theory]
    [InlineData("unreachable")]
    [InlineData("refused")]
    [InlineData("address")]
    [InlineData("slow")]
    [InlineData("index")]
    [InlineData("nothing")]
    [InlineData("bug")]
    public async Task ASearchThatFails_SendsTheWholeStory_AndSaysSo(string failure)
    {
        await IndexBookAsync();
        _options = new RagOptions { Mode = QueryMode.Rewrite, Passages = 3, SearchTimeout = TimeSpan.FromMilliseconds(100) };
        switch (failure)
        {
            case "unreachable":
                _embeddings.Reply = (_, _) => throw new ChatProviderException(ChatFailureKind.Unreachable, "down");
                break;
            case "refused":
                _embeddings.Reply = (_, _) => throw new ChatProviderException(ChatFailureKind.Http, "no", 401);
                break;
            case "address":
                _embeddings.CreateFailure = new ArgumentException("bad");
                break;
            case "slow":
                _embeddings.Reply = async (_, token) => { await Task.Delay(TimeSpan.FromSeconds(30), token); return []; };
                break;
            case "index":
                _index.Search = (_, _) => throw new PassageIndexException("locked");
                break;
            case "nothing":
                _index.Search = (_, _) => [];
                break;
            default:
                _index.Search = (_, _) => throw new InvalidOperationException("unexpected");
                break;
        }

        ShouldChatWithTheStory(await ChatAsync());

        Notices.ShouldBe([ChatNotice.SearchFailed]);
    }

    [Fact]
    public async Task BothFailures_AreSaid()
    {
        await IndexBookAsync();
        _models.Rewrite = _ => throw new ChatProviderException(ChatFailureKind.Unreachable, "down");
        _embeddings.Reply = (_, _) => throw new ChatProviderException(ChatFailureKind.Unreachable, "down");

        await ChatAsync();

        Notices.ShouldBe([ChatNotice.RewriteFailed, ChatNotice.SearchFailed]);
    }

    [Fact]
    public async Task AReaderWhoLeaves_StopsTheSearch_WithoutAReply()
    {
        await IndexBookAsync();
        using var leave = CancellationTokenSource.CreateLinkedTokenSource(Token);
        _models.Rewrite = async token =>
        {
            await leave.CancelAsync();
            token.ThrowIfCancellationRequested();
            return "never";
        };
        var retriever = new PassageRetriever(_index, _settings, _embeddings, _models, _prompts, _options);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in new CharacterChatService(_settings, _models, _prompts, retriever).ReplyAsync(_book, _character, [User("Hi")], leave.Token))
            {
            }
        });

        _models.Streamed.ShouldBeEmpty();
    }

    // ---- What is traced ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSearch_IsTraced_WithTheTextOfTheQueryOnlyWhenContentIsCaptured(bool capture)
    {
        await IndexBookAsync();
        _options = new RagOptions { Mode = QueryMode.Rewrite, Passages = 3, CaptureContent = capture };
        _models.Rewrite = _ => Task.FromResult("the secret query");
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LlmTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (spans)
                {
                    spans.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        await ChatAsync();

        Activity span;
        lock (spans)
        {
            span = spans.Last(activity => activity.OperationName == "passage-search" && (string?)activity.GetTagItem("book.id") == _book.Id.ToString());
        }

        span.GetTagItem("langfuse.observation.metadata.mode").ShouldBe("rewrite");
        span.GetTagItem("langfuse.observation.metadata.passages").ShouldBe("79,2,102");
        span.GetTagItem("langfuse.observation.metadata.distances").ShouldBe("0.300,0.400,0.500");
        string.Join(' ', span.TagObjects.Select(tag => tag.Value?.ToString())).Contains("the secret query", StringComparison.Ordinal).ShouldBe(capture);
        string.Join(' ', span.TagObjects.Select(tag => tag.Value?.ToString())).ShouldNotContain("Text of passage");
    }

    // The chat provider: streams a reply, and answers the rewriting of the query as the test says.
    private sealed class ChatAndRewriteFactory : IChatModelFactory
    {
        public List<IReadOnlyList<ChatMessage>> Streamed { get; } = [];

        public List<IReadOnlyList<ChatMessage>> Rewrites { get; } = [];

        public List<bool> RetryFlags { get; } = [];

        public Func<CancellationToken, Task<string>> Rewrite { get; set; } = _ => Task.FromResult("the ball");

        public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true)
        {
            RetryFlags.Add(retryPassingFailures);
            return new Model(this);
        }

        private sealed class Model(ChatAndRewriteFactory owner) : IChatModel
        {
            public async IAsyncEnumerable<string> StreamReplyAsync(
                IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                owner.Streamed.Add(messages);
                await Task.Yield();
                yield return "Yes.";
            }

            public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
            {
                owner.Rewrites.Add(messages);
                return owner.Rewrite(cancellationToken);
            }
        }
    }
}
