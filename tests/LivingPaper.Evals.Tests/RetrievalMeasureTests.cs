using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Persistence;
using Shouldly;

namespace LivingPaper.Evals.Tests;

// PAPER-87: the answers of an indexed book are measured with the real chat, the real retriever and a real SQLite index; the
// models are fakes. What was searched, what was found and what it cost are kept with each try.
public class RetrievalMeasureTests
{
    private static readonly LlmProvider Chat = new("chat", "Chat", "https://a.example/v1", "k", "chat-model");
    private static readonly LlmProvider Vectors = new("vectors", "Vectors", "http://localhost:11434/v1", string.Empty, "bge-m3");

    private sealed class Prompts : IPromptProvider
    {
        public Task<string> RenderAsync(string key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken) =>
            Task.FromResult(key == PromptKeys.ChatPassages ? variables[PromptVariables.Passages] : variables.GetValueOrDefault("story", key));
    }

    private sealed class Embeddings : IEmbeddingModelFactory
    {
        public Exception? Failure { get; set; }

        public IEmbeddingModel Create(LlmProvider provider, bool retryPassingFailures = true) => new Model(this);

        private sealed class Model(Embeddings owner) : IEmbeddingModel
        {
            // East for a text about the ball, north for anything else.
            public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
                owner.Failure is { } failure
                    ? Task.FromException<IReadOnlyList<float[]>>(failure)
                    : Task.FromResult<IReadOnlyList<float[]>>(
                        [.. texts.Select(text => text.Contains("waltz", StringComparison.OrdinalIgnoreCase) ? new float[] { 1, 0 } : [0, 1])]);
        }
    }

    // Counts 100 input tokens for a reply and 10 for a search query, as a provider would report them.
    private sealed class Model : IChatModel
    {
        public List<IReadOnlyList<ChatMessage>> Replies { get; } = [];

        public async IAsyncEnumerable<string> StreamReplyAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatUsageMeter? usage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Replies.Add(messages);
            usage!.Add(100, 0, 20);
            await Task.Yield();
            yield return "Oh, the ball!";
        }

        public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
        {
            usage!.Add(10, 0, 5);
            return Task.FromResult("the waltz with the Viscount");
        }
    }

    private static Book TheBook()
    {
        var book = new Book(BookId.FromTitle("Madame Bovary", _ => false), "Madame Bovary", "x.epub", DateTimeOffset.UnixEpoch)
        {
            Status = BookStatus.Done,
            StorySummary = "The whole story.",
        };
        book.Characters.Add(new Character(1, "Emma Bovary", "short", "profile"));
        return book;
    }

    private static async Task<SqlitePassageIndex> IndexAsync(TempFolder folder)
    {
        var index = new SqlitePassageIndex(Path.Combine(folder.Path, "rag.db"));
        var key = new BookKey(BookShelf.Library, "madame-bovary");
        await index.SavePassagesAsync(
            key,
            [
                new StoredPassage(1, "Elle choisit le Vicomte,\net le violon recommença.", []),
                new StoredPassage(2, "Charles entra au collège.", []),
            ],
            CancellationToken.None);
        await index.SaveVectorsAsync(key, "bge-m3", [[1, 0], [0, 1]], CancellationToken.None);
        return index;
    }

    private static ChatServiceReplier Replier(SqlitePassageIndex index, Embeddings embeddings, Model model, QueryMode mode = QueryMode.Rewrite)
    {
        var site = new InMemorySettings(LlmSettings.Restore([Chat], Chat.Id, Chat.Id));
        var settings = new LlmSettingsService(new ChatOverrideSettings(site, Chat.Id, Vectors), new ChatSetupTests.NoTester(), new ChatSetupTests.NoCatalog(), new PendingProviderStore(TimeProvider.System));
        var recorder = new RetrievalRecorder(new FakeChatModelFactory(_ => model), embeddings, index);
        var retriever = new PassageRetriever(recorder, settings, recorder, recorder, new Prompts(), new RagOptions { Mode = mode, Passages = 1 });
        var book = TheBook();
        return new ChatServiceReplier(new CharacterChatService(settings, recorder, new Prompts(), retriever), book, book.Characters[0], recorder);
    }

    [Fact]
    public async Task ChatOverride_AddsTheModelForVectorization_WithoutSavingIt()
    {
        var site = new InMemorySettings(LlmSettings.Restore([Chat], Chat.Id, Chat.Id));

        var settings = await new ChatOverrideSettings(site, Chat.Id, Vectors).LoadAsync(CancellationToken.None);

        settings.EmbeddingProvider.ShouldBe(Vectors);
        settings.ChatProvider.ShouldBe(Chat);
        site.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task TheTryKeepsTheQueryThePassagesAndTheCostOfTheReply()
    {
        using var folder = new TempFolder();
        var model = new Model();

        var answer = await Replier(await IndexAsync(folder), new Embeddings(), model)
            .ReplyAsync(new EvalQuestion("f-1", "fact", "Do you remember the ball?", "Yes.") { PassageQuotes = ["le Vicomte, et le violon"] }, CancellationToken.None);

        answer.Reply.ShouldBe("Oh, the ball!");
        var trace = answer.Trace!;
        trace.Query.ShouldBe("the waltz with the Viscount");
        trace.Passages.ShouldBe([1]);
        trace.Distances!.Single().ShouldBe(0, 0.001);
        trace.ExpectedFound.ShouldBe(true);
        trace.Notices.ShouldBeNull();
        trace.ReplyTokens.Input.ShouldBe(100);
        trace.QueryTokens!.Input.ShouldBe(10);
        trace.Seconds.ShouldBeGreaterThanOrEqualTo(0);
        model.Replies.Single()[0].Content.ShouldContain("Elle choisit le Vicomte");
    }

    [Fact]
    public async Task APhraseThatIsNotInThePassagesFound_IsReportedAsMissed()
    {
        using var folder = new TempFolder();

        var answer = await Replier(await IndexAsync(folder), new Embeddings(), new Model(), QueryMode.Last)
            .ReplyAsync(new EvalQuestion("f-1", "fact", "Your wedding?", "x") { PassageQuotes = ["Elle choisit le Vicomte"] }, CancellationToken.None);

        answer.Trace!.Query.ShouldBe("Your wedding?");
        answer.Trace.Passages.ShouldBe([2]);
        answer.Trace.ExpectedFound.ShouldBe(false);
        answer.Trace.QueryTokens.ShouldBeNull();
    }

    [Fact]
    public async Task TheEarlierConversationIsSentBeforeTheQuestion()
    {
        using var folder = new TempFolder();
        var model = new Model();
        var question = new EvalQuestion("f-1", "follow-up", "And then?", "x")
        {
            Conversation = [new ConversationTurn(false, "Where?"), new ConversationTurn(true, "At the ball.")],
        };

        var answer = await Replier(await IndexAsync(folder), new Embeddings(), model, QueryMode.Recent).ReplyAsync(question, CancellationToken.None);

        answer.Trace!.Query.ShouldBe("Where?\nAt the ball.\nAnd then?");
        model.Replies.Single().Skip(1).Select(message => (message.Role, message.Content))
            .ShouldBe([(ChatRole.User, "Where?"), (ChatRole.Assistant, "At the ball."), (ChatRole.User, "And then?")]);
    }

    [Fact]
    public async Task ASearchThatFails_IsKeptAsANotice_WithNoPassageAndNothingFound()
    {
        using var folder = new TempFolder();
        var embeddings = new Embeddings { Failure = new ChatProviderException(ChatFailureKind.Unreachable, "down") };

        var answer = await Replier(await IndexAsync(folder), embeddings, new Model())
            .ReplyAsync(new EvalQuestion("f-1", "fact", "The ball?", "x") { PassageQuotes = ["le Vicomte"] }, CancellationToken.None);

        answer.Trace!.Notices.ShouldBe(["SearchFailed"]);
        answer.Trace.Passages.ShouldBeNull();
        answer.Trace.ExpectedFound.ShouldBe(false);
    }

    [Fact]
    public async Task WithTheFullStory_TheTryHasTheCostOfTheReplyAndNoSearch()
    {
        var site = new InMemorySettings(LlmSettings.Restore([Chat], Chat.Id, Chat.Id));
        var settings = new LlmSettingsService(new ChatOverrideSettings(site, Chat.Id), new ChatSetupTests.NoTester(), new ChatSetupTests.NoCatalog(), new PendingProviderStore(TimeProvider.System));
        var recorder = new RetrievalRecorder(new FakeChatModelFactory(_ => new Model()));
        var book = TheBook();
        var replier = new ChatServiceReplier(new CharacterChatService(settings, recorder, new Prompts()), book, book.Characters[0], recorder);

        var answer = await replier.ReplyAsync(new EvalQuestion("f-1", "fact", "The ball?", "x") { PassageQuotes = ["le Vicomte"] }, CancellationToken.None);

        answer.Trace!.Query.ShouldBeNull();
        answer.Trace.Passages.ShouldBeNull();
        answer.Trace.ExpectedFound.ShouldBeNull();
        answer.Trace.ReplyTokens.Input.ShouldBe(100);
    }

    [Fact]
    public void QuotesAreFoundWhateverTheSpacesAndTheLineBreaks()
    {
        PassageQuotes.Appears("la  valse\ndu   Vicomte", "Après la valse du Vicomte.").ShouldBeTrue();
        PassageQuotes.Appears("la valse", "La Valse").ShouldBeTrue();
        PassageQuotes.Appears("la valse", "le violon").ShouldBeFalse();
    }
}
