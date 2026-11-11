using System.Runtime.CompilerServices;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

// A character plays itself from its sheet (PAPER-10): the system prompt, what is sent to the model, the reply piece by piece,
// and every way it can fail.
public sealed class CharacterChatServiceTests : IDisposable
{
    private readonly InMemoryLlmSettingsRepository _settingsRepository = new();
    private readonly LlmSettingsService _settings;
    private readonly StreamingModelFactory _models = new();
    private readonly KeyedPromptProvider _prompts = new();
    private readonly Book _book = new(BookId.New(), "Le Malade imaginaire", "malade.pdf", DateTimeOffset.UtcNow) { Status = BookStatus.Done };
    private readonly Character _character = new(1, "Toinette", "The maid.", "I am Toinette. I speak plainly.");

    public CharacterChatServiceTests()
    {
        var provider = new LlmProvider("p1", "OpenAI", "https://api.openai.com/v1", "sk-chat-secret-key", "gpt-5-nano");
        _settingsRepository.Seed(LlmSettings.Restore([provider], null, "p1")); // A model for the chat, none for the analysis.
        _settings = new LlmSettingsService(_settingsRepository, new FakeConnectionTester());
    }

    public void Dispose() => _settings.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CharacterChatService NewService() => new(_settings, _models, _prompts);

    private async Task<List<string>> ReplyAsync(IReadOnlyList<ChatMessage>? conversation = null)
    {
        var pieces = new List<string>();
        await foreach (var piece in NewService().ReplyAsync(_book, _character, conversation ?? [new ChatMessage(ChatRole.User, "Bonjour !")], Token))
        {
            pieces.Add(piece);
        }

        return pieces;
    }

    private async Task<ChatFailure> FailureOfAsync(IReadOnlyList<ChatMessage>? conversation = null) =>
        (await Should.ThrowAsync<ChatReplyException>(() => ReplyAsync(conversation))).Failure;

    // ---- The reply ----

    [Fact]
    public async Task TheReply_ComesPieceByPiece_AsTheModelWritesIt()
    {
        _models.Chunks = ["Ah, ", "Monsieur ", "!"];

        (await ReplyAsync()).ShouldBe(["Ah, ", "Monsieur ", "!"]);
    }

    [Fact]
    public async Task TheSystemPrompt_IsThePromptOfThePromptsFile_WithTheBookTheNameAndTheProfile()
    {
        await ReplyAsync();

        var system = _models.Sent.Single()[0];
        system.Role.ShouldBe(ChatRole.System);
        system.Content.ShouldBe("[chat.system] bookTitle=Le Malade imaginaire characterName=Toinette profile=I am Toinette. I speak plainly.");
    }

    [Fact]
    public async Task TheUsedModel_IsTheOneChosenForTheChat_NotTheOneForTheAnalysis()
    {
        await ReplyAsync();

        _models.Providers.ShouldHaveSingleItem().Id.ShouldBe("p1");
    }

    [Fact]
    public async Task TheConversation_IsSentAfterTheSystemPrompt_InOrder()
    {
        await ReplyAsync([new ChatMessage(ChatRole.User, "One"), new ChatMessage(ChatRole.Assistant, "Two"), new ChatMessage(ChatRole.User, "Three")]);

        _models.Sent.Single().Select(message => (message.Role, message.Content)).ShouldBe(
        [
            (ChatRole.System, "[chat.system] bookTitle=Le Malade imaginaire characterName=Toinette profile=I am Toinette. I speak plainly."),
            (ChatRole.User, "One"),
            (ChatRole.Assistant, "Two"),
            (ChatRole.User, "Three"),
        ]);
    }

    [Fact]
    public async Task OnlyTheLastTwentyMessages_AreSentToTheModel()
    {
        var conversation = Enumerable.Range(1, 41).Select(index => new ChatMessage(index % 2 == 1 ? ChatRole.User : ChatRole.Assistant, $"message {index}")).ToList();

        await ReplyAsync(conversation);

        var sent = _models.Sent.Single();
        sent.Count.ShouldBe(1 + ConversationLimits.MaxSentMessages);
        sent[1].Content.ShouldBe("message 22");
        sent[^1].Content.ShouldBe("message 41");
    }

    [Fact]
    public async Task PassingFailuresAreRetriedByTheClient_AsForAnyOtherCall()
    {
        await ReplyAsync();

        _models.Providers.Count.ShouldBe(1);
        _models.RetryFlags.ShouldBe([true]);
    }

    [Fact]
    public async Task ThePromptsFile_IsReadAgainAtEveryMessage()
    {
        await ReplyAsync();
        await ReplyAsync();

        _prompts.Renders.ShouldBe(2);
    }

    // ---- Failures ----

    [Fact]
    public async Task WithNoModelChosenForTheChat_TheReplyFailsAtOnce()
    {
        _settingsRepository.Seed(LlmSettings.Restore([new LlmProvider("p1", "OpenAI", "https://api.openai.com/v1", "k", "m")], "p1", null));

        (await FailureOfAsync()).ShouldBe(ChatFailure.NoModel);
        _models.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnusableSettingsFile_IsNoModel()
    {
        _settingsRepository.LoadFailure = new SettingsFileException("settings.json", "not valid");

        (await FailureOfAsync()).ShouldBe(ChatFailure.NoModel);
    }

    [Fact]
    public async Task AnUnusablePromptsFile_IsReported()
    {
        _prompts.Failure = new PromptFileException("prompts.json", PromptKeys.ChatSystem, "empty");

        (await FailureOfAsync()).ShouldBe(ChatFailure.PromptFile);
        _models.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ChatFailureKind.Unreachable, null, ChatFailure.ModelUnreachable)]
    [InlineData(ChatFailureKind.TimedOut, null, ChatFailure.ModelUnreachable)]
    [InlineData(ChatFailureKind.StreamCut, null, ChatFailure.ModelUnreachable)]
    [InlineData(ChatFailureKind.Http, 503, ChatFailure.ModelUnreachable)]
    [InlineData(ChatFailureKind.Http, 429, ChatFailure.ModelUnreachable)]
    [InlineData(ChatFailureKind.Http, 401, ChatFailure.ModelRefused)]
    [InlineData(ChatFailureKind.Http, 404, ChatFailure.ModelRefused)]
    [InlineData(ChatFailureKind.InvalidResponse, null, ChatFailure.InvalidReply)]
    [InlineData(ChatFailureKind.EmptyReply, null, ChatFailure.InvalidReply)]
    public async Task AFailureOfTheModel_AtTheStart_IsMappedToACode(ChatFailureKind kind, int? status, ChatFailure expected)
    {
        _models.FailAtStart = new ChatProviderException(kind, "the provider said: sk-chat-secret-key", status);

        var failure = await Should.ThrowAsync<ChatReplyException>(() => ReplyAsync());

        failure.Failure.ShouldBe(expected);
        failure.Message.ShouldNotContain("sk-chat-secret-key");
    }

    [Fact]
    public async Task AFailureInTheMiddleOfTheReply_StopsItWithACode_AfterTheFirstPiecesWereGiven()
    {
        _models.Chunks = ["First ", "second "];
        _models.FailAfterChunks = new ChatProviderException(ChatFailureKind.StreamCut, "cut", null);
        var pieces = new List<string>();

        var failure = await Should.ThrowAsync<ChatReplyException>(async () =>
        {
            await foreach (var piece in NewService().ReplyAsync(_book, _character, [new ChatMessage(ChatRole.User, "Hi")], Token))
            {
                pieces.Add(piece);
            }
        });

        pieces.ShouldBe(["First ", "second "]);
        failure.Failure.ShouldBe(ChatFailure.ModelUnreachable);
    }

    [Fact]
    public async Task TheContentFilter_IsItsOwnCode()
    {
        _models.FailAtStart = new ContentFilteredException();

        (await FailureOfAsync()).ShouldBe(ChatFailure.ContentFiltered);
    }

    [Fact]
    public async Task AnAddressTheClientRefuses_IsNotAnUnexpectedError()
    {
        _models.CreateFailure = new ArgumentException("not an absolute address");

        (await FailureOfAsync()).ShouldBe(ChatFailure.ModelUnreachable);
    }

    [Fact]
    public async Task CancellingTheReply_StopsIt_WithoutAFailure()
    {
        using var stop = new CancellationTokenSource();
        _models.Chunks = ["a", "b", "c"];
        _models.OnChunk = () => stop.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in NewService().ReplyAsync(_book, _character, [new ChatMessage(ChatRole.User, "Hi")], stop.Token))
            {
            }
        });
    }

    // ---- Is the chat available ----

    [Fact]
    public async Task TheChat_IsAvailable_WhenAModelIsChosenForIt_AndNotOtherwise()
    {
        (await NewService().IsAvailableAsync(Token)).ShouldBeTrue();

        _settingsRepository.Seed(LlmSettings.Restore([new LlmProvider("p1", "OpenAI", "https://api.openai.com/v1", "k", "m")], "p1", null));
        (await NewService().IsAvailableAsync(Token)).ShouldBeFalse();

        _settingsRepository.LoadFailure = new SettingsFileException("settings.json", "not valid");
        (await NewService().IsAvailableAsync(Token)).ShouldBeFalse();
    }

    // The provider of the tests: gives the chunks it is told to, or fails the way it is told to.
    private sealed class StreamingModelFactory : IChatModelFactory
    {
        public IReadOnlyList<string> Chunks { get; set; } = ["Hello."];

        public List<IReadOnlyList<ChatMessage>> Sent { get; } = [];

        public List<LlmProvider> Providers { get; } = [];

        public List<bool> RetryFlags { get; } = [];

        public Exception? FailAtStart { get; set; }

        public Exception? FailAfterChunks { get; set; }

        public Exception? CreateFailure { get; set; }

        public Action? OnChunk { get; set; }

        public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true)
        {
            if (CreateFailure is not null)
            {
                throw CreateFailure;
            }

            Providers.Add(provider);
            RetryFlags.Add(retryPassingFailures);
            return new Model(this);
        }

        private sealed class Model(StreamingModelFactory owner) : IChatModel
        {
            public async IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                owner.Sent.Add(messages);
                await Task.Yield();
                if (owner.FailAtStart is not null)
                {
                    throw owner.FailAtStart;
                }

                foreach (var chunk in owner.Chunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    owner.OnChunk?.Invoke();
                    yield return chunk;
                }

                if (owner.FailAfterChunks is not null)
                {
                    throw owner.FailAfterChunks;
                }
            }

            public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }
    }
}
