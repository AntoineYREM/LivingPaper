using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// PAPER-86: the chat of an indexed book on the real application, with a real SQLite index and the default prompts, and
// models that never leave the process.
public partial class ChatPassagesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    private sealed class Models : IChatModelFactory, IEmbeddingModelFactory
    {
        public Exception? RewriteFailure { get; set; }

        public Exception? EmbeddingFailure { get; set; }

        public List<string> Queries { get; } = [];

        public List<string> Systems { get; } = [];

        IChatModel IChatModelFactory.Create(LlmProvider provider, bool retryPassingFailures) => new Chat(this);

        IEmbeddingModel IEmbeddingModelFactory.Create(LlmProvider provider, bool retryPassingFailures) => new Vectors(this);

        private sealed class Chat(Models owner) : IChatModel
        {
            public async IAsyncEnumerable<string> StreamReplyAsync(
                IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                owner.Systems.Add(messages[0].Content);
                await Task.Yield();
                yield return "Oh, the ball!";
            }

            // The connection test of the settings (no system message), and the rewriting of the query.
            public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
                messages.Count == 1 ? Task.FromResult("pong")
                : owner.RewriteFailure is { } failure ? Task.FromException<string>(failure)
                : Task.FromResult("the waltz with the Viscount");
        }

        private sealed class Vectors(Models owner) : IEmbeddingModel
        {
            // East for a text about the ball, north for anything else.
            public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, ChatUsageMeter? usage, CancellationToken cancellationToken)
            {
                if (owner.EmbeddingFailure is { } failure)
                {
                    return Task.FromException<IReadOnlyList<float[]>>(failure);
                }

                owner.Queries.AddRange(texts);
                return Task.FromResult<IReadOnlyList<float[]>>(
                    [.. texts.Select(text => text.Contains("waltz", StringComparison.OrdinalIgnoreCase) ? new float[] { 1, 0 } : [0, 1])]);
            }
        }
    }

    private static LivingPaperFactory NewFactory(Models models, IReadOnlyDictionary<string, string>? settings = null) => LivingPaperFactory.With(
        configureServices: services =>
        {
            services.AddSingleton<IChatModelFactory>(models);
            services.AddSingleton<IEmbeddingModelFactory>(models);
            services.AddSingleton<IAnalysisScheduler>(new RecordingScheduler());
        },
        settings: settings);

    private static HttpClient NewClient(LivingPaperFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    // A book with its story and three passages, indexed with the model chosen for vectorization.
    private static async Task<Book> SeedAsync(LivingPaperFactory factory)
    {
        await factory.Services.GetRequiredService<BookAnalysisWorker>().Recovered;
        var settings = factory.Services.GetRequiredService<LlmSettingsService>();
        (await settings.AddAsync(new LlmProviderDraft("Chat", "https://example.com/v1", "k", "chat-model"), Token)).Succeeded.ShouldBeTrue();
        var vectors = (await settings.AddAsync(new LlmProviderDraft("Vectors", "https://example.com/v1", "k", "bge-m3"), Token)).Added!;
        (await settings.SelectForEmbeddingAsync(vectors.Id, Token))!.Success.ShouldBeTrue();

        var book = new Book(BookId.New(), "Madame Bovary", "bovary.epub", DateTimeOffset.UtcNow)
        {
            Status = BookStatus.Done,
            StorySummary = "Scene 1. The whole story.",
            PlotSummary = "A country wife dreams of another life.",
        };
        book.Characters.Add(new Character(1, "Emma Bovary", "The heroine.", "I am Emma."));
        await factory.Services.GetRequiredService<IBookRepository>().SaveAsync(book, Token);

        var index = factory.Services.GetRequiredService<IPassageIndex>();
        var key = new BookKey(BookShelf.Library, book.Id.ToString());
        await index.SavePassagesAsync(
            key,
            [
                new StoredPassage(1, "Elle choisit le Vicomte, et le violon recommença.", [new PassageScene("Emma waltzes.", ["Emma", "Vicomte"])]),
                new StoredPassage(2, "Charles entra au collège.", []),
                new StoredPassage(3, "Les comices agricoles.", []),
            ],
            Token);
        await index.SaveVectorsAsync(key, "bge-m3", [[1, 0], [0, 1], [-1, 0]], Token);
        return book;
    }

    private static async Task<List<JsonElement>> SayAsync(HttpClient client, Book book, string message)
    {
        var page = $"/books/{book.Id}/characters/1/chat";
        var token = AntiforgeryToken().Match(await client.GetStringAsync(page, Token)).Groups[1].Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, page + "?handler=Message")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { messages = new[] { new { role = "user", content = message } } }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await client.SendAsync(request, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Token);
        return [.. body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.Clone())];
    }

    private static IEnumerable<string> Kinds(IEnumerable<JsonElement> events) =>
        events.Select(item => item.TryGetProperty("code", out var code) ? $"{item.GetProperty("type").GetString()}:{code.GetString()}" : item.GetProperty("type").GetString()!);

    [Fact]
    public async Task TheCharacterGetsThePlotAndTheClosestPassages_WithTheDefaultPrompts()
    {
        var models = new Models();
        using var factory = NewFactory(models, new Dictionary<string, string> { ["RAG_PASSAGES"] = "1" });
        using var client = NewClient(factory);
        var book = await SeedAsync(factory);
        models.Queries.Clear();

        var events = await SayAsync(client, book, "Tu te souviens du bal ?");

        Kinds(events).ShouldBe(["text", "done"]);
        models.Queries.ShouldBe(["the waltz with the Viscount"]);
        var system = models.Systems.ShouldHaveSingleItem();
        system.ShouldContain("A country wife dreams of another life.");
        system.ShouldContain("[Scene: Emma waltzes. | Present: Emma, Vicomte]\nElle choisit le Vicomte");
        system.ShouldContain("You lived the passages where Emma Bovary is present");
        system.ShouldNotContain("Charles entra au collège.");
        system.ShouldNotContain("The whole story");
        system.ShouldNotContain("{passages}");
        system.ShouldNotContain("{plotSummary}");
    }

    [Fact]
    public async Task TheLastMode_SearchesWithTheMessageItself()
    {
        var models = new Models();
        using var factory = NewFactory(models, new Dictionary<string, string> { ["RAG_QUERY_MODE"] = "last" });
        using var client = NewClient(factory);
        var book = await SeedAsync(factory);
        models.Queries.Clear();

        await SayAsync(client, book, "Tu te souviens du bal ?");

        models.Queries.ShouldBe(["Tu te souviens du bal ?"]);
    }

    [Fact]
    public async Task WhenTheRewritingFails_TheReplyStillComes_WithANoticeBeforeIt()
    {
        var models = new Models { RewriteFailure = new ChatProviderException(ChatFailureKind.Http, "secret", 500) };
        using var factory = NewFactory(models);
        using var client = NewClient(factory);
        var book = await SeedAsync(factory);

        var events = await SayAsync(client, book, "Tu te souviens du bal ?");

        Kinds(events).ShouldBe(["notice:rewriteFailed", "text", "done"]);
        factory.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains("bal", StringComparison.OrdinalIgnoreCase) && !entry.Message.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenTheSearchFails_TheCharacterUsesTheStory_AndTheReaderIsTold()
    {
        var models = new Models();
        using var factory = NewFactory(models);
        using var client = NewClient(factory);
        var book = await SeedAsync(factory);
        models.EmbeddingFailure = new ChatProviderException(ChatFailureKind.Unreachable, "down");

        var events = await SayAsync(client, book, "Tu te souviens du bal ?");

        Kinds(events).ShouldBe(["notice:searchFailed", "text", "done"]);
        models.Systems.ShouldHaveSingleItem().ShouldContain("Scene 1. The whole story.");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task ThePageHoldsTheWordsOfEveryNotice(string language)
    {
        using var factory = NewFactory(new Models());
        using var client = NewClient(factory);
        var book = await SeedAsync(factory);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);

        var raw = await client.GetStringAsync($"/books/{book.Id}/characters/1/chat", Token);

        var json = Regex.Match(raw, @"<script type=""application/json"" id=""chat-texts"">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        var notices = JsonDocument.Parse(json).RootElement.GetProperty("notices");
        foreach (var notice in Enum.GetNames<ChatNotice>())
        {
            notices.GetProperty(char.ToLowerInvariant(notice[0]) + notice[1..]).GetString().ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData("RAG_QUERY_MODE", "magic")]
    [InlineData("RAG_PASSAGES", "0")]
    [InlineData("RAG_PASSAGES", "11")]
    [InlineData("RAG_PASSAGES", "three")]
    public void ASettingThatCannotBeUsed_StopsTheApplication_NamingIt(string variable, string value)
    {
        using var factory = NewFactory(new Models(), new Dictionary<string, string> { [variable] = value });

        var exception = Should.Throw<Exception>(() => factory.CreateClient());

        exception.ToString().ShouldContain(variable);
    }
}
