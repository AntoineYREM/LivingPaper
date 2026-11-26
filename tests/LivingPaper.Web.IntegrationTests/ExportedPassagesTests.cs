using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// PAPER-88: the passages and vectors that travel with a book, and the index of an example, on the real application with a
// real SQLite index, its background queue, and models that never leave the process.
public partial class ExportedPassagesTests
{
    private const string Warning = "This file holds the text of the book, which may be protected by copyright.";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    private sealed class Models : IChatModelFactory, IEmbeddingModelFactory
    {
        public List<string> Vectorized { get; } = [];

        public List<string> Systems { get; } = [];

        public TaskCompletionSource? Hold { get; set; }

        IChatModel IChatModelFactory.Create(LlmProvider provider, bool retryPassingFailures) => new Chat(this);

        IEmbeddingModel IEmbeddingModelFactory.Create(LlmProvider provider, bool retryPassingFailures) => new Vectors(this);

        private sealed class Chat(Models owner) : IChatModel
        {
            public async IAsyncEnumerable<string> StreamReplyAsync(
                IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                owner.Systems.Add(messages[0].Content);
                await Task.Yield();
                yield return "Oh!";
            }

            public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
                Task.FromResult("pong");
        }

        private sealed class Vectors(Models owner) : IEmbeddingModel
        {
            // The vector of a text tells its length: what a file holds is then that of the model, or not.
            public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, ChatUsageMeter? usage, CancellationToken cancellationToken)
            {
                if (owner.Hold is { } hold)
                {
                    await hold.Task.WaitAsync(cancellationToken);
                }

                lock (owner.Vectorized)
                {
                    owner.Vectorized.AddRange(texts);
                }

                return [.. texts.Select(text => MadeOf(text))];
            }
        }
    }

    private static float[] MadeOf(string text) => [text.Length, 1];

    private static LivingPaperFactory NewFactory(Models models, IBookArchive? archive = null) => LivingPaperFactory.With(
        configureServices: services =>
        {
            services.AddSingleton<IChatModelFactory>(models);
            services.AddSingleton<IEmbeddingModelFactory>(models);
            if (archive is not null)
            {
                services.AddSingleton(archive);
            }
        });

    private static HttpClient NewClient(LivingPaperFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task ChooseModelsAsync(LivingPaperFactory factory, Models? models = null, string? vectorization = "bge-m3")
    {
        await factory.Services.GetRequiredService<BookAnalysisWorker>().Recovered;
        var settings = factory.Services.GetRequiredService<LlmSettingsService>();
        (await settings.AddAsync(new LlmProviderDraft("Chat", "https://example.com/v1", "k", "chat-model"), Token)).Succeeded.ShouldBeTrue();
        if (vectorization is not null)
        {
            await ChooseVectorizationAsync(factory, vectorization);
        }

        models?.Vectorized.Clear();
    }

    private static async Task ChooseVectorizationAsync(LivingPaperFactory factory, string model)
    {
        var settings = factory.Services.GetRequiredService<LlmSettingsService>();
        var added = await settings.AddAsync(new LlmProviderDraft(model, "https://example.com/v1", "k", model), Token);
        (await settings.SelectForEmbeddingAsync(added.Added!.Id, Token))!.Success.ShouldBeTrue();
    }

    private static Book Analyzed(string title = "Madame Bovary")
    {
        var book = new Book(BookId.FromTitle(title, _ => false), title, "bovary.epub", DateTimeOffset.UtcNow)
        {
            Status = BookStatus.Done,
            StorySummary = "Scene 1. The whole story.",
            PlotSummary = "A country wife dreams of another life.",
        };
        book.Characters.Add(new Character(1, "Emma Bovary", "The heroine.", "I am Emma."));
        return book;
    }

    private static IReadOnlyList<StoredPassage> Passages(int count = 3) =>
        [.. Enumerable.Range(1, count).Select(number => new StoredPassage(number, $"Passage number {number} of the novel.", []))];

    private static PassageSet Set(bool ofTheModel = true) => new(
        Passages(), "bge-m3", [.. Passages().Select(passage => ofTheModel ? MadeOf(passage.Text) : [1f, 40f])]);

    private static BookKey Key(BookId id, BookShelf shelf = BookShelf.Library) => new(shelf, id.ToString());

    private static IPassageIndex Index(LivingPaperFactory factory) => factory.Services.GetRequiredService<IPassageIndex>();

    private static async Task<Book> InLibraryAsync(LivingPaperFactory factory, int passages = 3, int? vectorSize = null)
    {
        var book = Analyzed();
        await factory.Services.GetRequiredService<IBookRepository>().SaveAsync(book, Token);
        if (passages > 0)
        {
            await Index(factory).SavePassagesAsync(Key(book.Id), Passages(passages), Token);
            await Index(factory).SaveVectorsAsync(
                Key(book.Id),
                "bge-m3",
                [.. Passages(passages).Select(passage => vectorSize is { } size ? Enumerable.Repeat(0.5f, size).ToArray() : MadeOf(passage.Text))],
                Token);
        }

        return book;
    }

    private static async Task<string> PageAsync(HttpClient client, string path, string? acceptLanguage = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        using var response = await client.SendAsync(request, Token);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
    }

    private static async Task<string> TokenOfAsync(HttpClient client, string page) =>
        AntiforgeryToken().Match(await client.GetStringAsync(page, Token)).Groups[1].Value;

    private static async Task<HttpResponseMessage> ImportAsync(HttpClient client, byte[] file, bool replace = false)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(await TokenOfAsync(client, "/import")), "__RequestVerificationToken" },
        };
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new("application/json");
        content.Add(part, "file", "book.json");
        if (replace)
        {
            content.Add(new StringContent("true"), "replace");
        }

        return await client.PostAsync("/import", content, Token);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string tokenPage, Dictionary<string, string>? form = null)
    {
        form ??= [];
        form["__RequestVerificationToken"] = await TokenOfAsync(client, tokenPage);
        return await client.PostAsync(path, new FormUrlEncodedContent(form), Token);
    }

    private static async Task IdleAsync(LivingPaperFactory factory, BookId id, BookShelf shelf = BookShelf.Library)
    {
        var scheduler = factory.Services.GetRequiredService<IAnalysisScheduler>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (scheduler.IsQueued(id, shelf))
        {
            (DateTime.UtcNow < deadline).ShouldBeTrue("the queue did not finish in 20 seconds");
            await Task.Delay(50, Token);
        }
    }

    private static byte[] FileOf(Book book, PassageSet set) => new JsonBookArchive().ExportWithPassages(book, set).Content;

    private static async Task PutExampleAsync(LivingPaperFactory factory, Book book, PassageSet set) =>
        await File.WriteAllBytesAsync(Path.Combine(factory.ExamplesDirectory, book.Id + ".json"), FileOf(book, set), Token);

    // ---- The export ----

    [Fact]
    public async Task ABookWithPassages_IsExportedWithThem_AndItsPageAndTheLibraryWarn()
    {
        using var factory = NewFactory(new Models());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);
        var book = await InLibraryAsync(factory);

        var page = await PageAsync(client, "/books/" + book.Id);
        var library = await PageAsync(client, "/");
        var file = await client.GetByteArrayAsync($"/books/{book.Id}/export", Token);

        page.ShouldContain(Warning);
        page.ShouldContain("and its passages");
        page.ShouldNotContain("not the text of the book");
        library.ShouldContain(Warning);
        var read = new JsonBookArchive().Parse(file);
        read.Passages!.Passages.Count.ShouldBe(3);
        read.Passages.EmbeddingModel.ShouldBe("bge-m3");
    }

    [Fact]
    public async Task ABookWithoutPassages_HasNoWarning()
    {
        using var factory = NewFactory(new Models());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);
        var book = await InLibraryAsync(factory, passages: 0);

        var page = await PageAsync(client, "/books/" + book.Id);

        page.ShouldContain("not the text of the book");
        page.ShouldNotContain(Warning);
        (await PageAsync(client, "/")).ShouldNotContain(Warning);
        File.Exists(Path.Combine(factory.DataDirectory, "rag.db")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1_000_000, "too large to be exported with its vectors")]
    [InlineData(3_000, "too large to be exported with its passages")]
    public async Task ABookTooLargeToBeImported_SaysWhatItsExportLeavesOut(int limit, string message)
    {
        using var factory = NewFactory(new Models(), new JsonBookArchive(maxBytes: limit));
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);
        var book = await InLibraryAsync(factory, passages: 2000, vectorSize: 256);

        (await PageAsync(client, "/books/" + book.Id)).ShouldContain(message);
    }

    // ---- The import ----

    [Fact]
    public async Task ExportedThenImported_TheBookIsIndexedAgain_WithoutAnyNewVector()
    {
        var models = new Models();
        using var factory = NewFactory(models);
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory, models);
        var book = await InLibraryAsync(factory);
        var file = await client.GetByteArrayAsync($"/books/{book.Id}/export", Token);
        await factory.Services.GetRequiredService<BookService>().DeleteAsync(book.Id, Token);

        using var response = await ImportAsync(client, file);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        await IdleAsync(factory, book.Id);
        (await PageAsync(client, "/books/" + book.Id)).ShouldContain("Indexed: 3 passages, with bge-m3.");
        models.Vectorized.ShouldBe(["Passage number 1 of the novel."]);
    }

    [Fact]
    public async Task WhileTheVectorsAreChecked_ThePageSaysSo_AndRefreshesItself()
    {
        var models = new Models();
        using var factory = NewFactory(models);
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);
        models.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var book = Analyzed();

        (await ImportAsync(client, FileOf(book, Set()))).Dispose();
        var page = await PageAsync(client, "/books/" + book.Id);
        models.Hold.SetResult();

        page.ShouldContain("Checking the vectors of the file…");
        page.ShouldContain("data-refresh-url");
        page.ShouldNotContain("handler=Reindex");
        await IdleAsync(factory, book.Id);
        (await PageAsync(client, "/books/" + book.Id)).ShouldContain("Indexed: 3 passages, with bge-m3.");
    }

    [Fact]
    public async Task VectorsThatAreNotThoseOfTheModel_AreSaidSo_AndTheButtonMakesNewOnes()
    {
        var models = new Models();
        using var factory = NewFactory(models);
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory, models);
        var book = Analyzed();
        (await ImportAsync(client, FileOf(book, Set(ofTheModel: false)))).Dispose();
        await IdleAsync(factory, book.Id);

        var page = await PageAsync(client, "/books/" + book.Id);

        page.ShouldContain("To be re-indexed: the vectors that came with this book are not those your model makes.");
        page.ShouldContain("Re-index");
        models.Vectorized.Count.ShouldBe(1); // Nothing is computed before the user asks.
        (await PostAsync(client, $"/books/{book.Id}?handler=Reindex", "/books/" + book.Id)).Dispose();
        await IdleAsync(factory, book.Id);
        (await PageAsync(client, "/books/" + book.Id)).ShouldContain("Indexed: 3 passages, with bge-m3.");
        models.Vectorized.Count.ShouldBe(5); // One more for the check, and the three passages.
    }

    [Fact]
    public async Task ImportedWithoutAModelForVectorization_TheVectorsAreCheckedWhenTheUserAsks()
    {
        var models = new Models();
        using var factory = NewFactory(models);
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory, models: null, vectorization: null);
        var book = Analyzed();
        (await ImportAsync(client, FileOf(book, Set()))).Dispose();
        await IdleAsync(factory, book.Id);
        (await PageAsync(client, "/books/" + book.Id)).ShouldNotContain("Passages of the book");

        await ChooseVectorizationAsync(factory, "bge-m3");
        models.Vectorized.Clear();
        var page = await PageAsync(client, "/books/" + book.Id);
        page.ShouldContain("The vectors of this book are not checked yet.");
        page.ShouldContain("Index the passages");
        (await PostAsync(client, $"/books/{book.Id}?handler=Reindex", "/books/" + book.Id)).Dispose();
        await IdleAsync(factory, book.Id);

        (await PageAsync(client, "/books/" + book.Id)).ShouldContain("Indexed: 3 passages, with bge-m3.");
        models.Vectorized.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AFileLargerThanOneMegabyte_BecauseOfItsPassages_IsImported()
    {
        using var factory = NewFactory(new Models());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory, models: null, vectorization: null);
        var set = new PassageSet([.. Enumerable.Range(1, 1500).Select(number => new StoredPassage(number, new string('a', 1900), []))], null, []);
        var file = FileOf(Analyzed(), set);
        file.Length.ShouldBeGreaterThan(2 * 1024 * 1024);

        using var response = await ImportAsync(client, file);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await Index(factory).GetStateAsync(Key(Analyzed().Id), Token))!.Passages.ShouldBe(1500);
    }

    [Fact]
    public async Task AFileWithAWrongVector_IsRefused_NamingTheField()
    {
        using var factory = NewFactory(new Models());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);
        var file = JsonNode.Parse(FileOf(Analyzed(), Set()))!.AsObject();
        file["passageIndex"]!["passages"]![0]!["vector"] = "AACAPw==";

        using var response = await ImportAsync(client, JsonSerializer.SerializeToUtf8Bytes(file));
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        page.ShouldContain("This file is not a valid book: passageIndex.passages[0].vector.");
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenThePassagesCannotBeKept_TheBookIsImported_AndThePageSaysSo()
    {
        using var factory = NewFactory(new Models());
        await File.WriteAllTextAsync(Path.Combine(factory.DataDirectory, "rag.db"), "This is not a database, whatever its name says.", Token);
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);
        var book = Analyzed();

        using var response = await ImportAsync(client, FileOf(book, Set()));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var page = await PageAsync(client, response.Headers.Location!.OriginalString);
        page.ShouldContain("Madame Bovary was imported. Its passages were not kept");
    }

    [Fact]
    public async Task TheImportPage_SaysHowLargeAFileMayBe()
    {
        using var factory = NewFactory(new Models());
        using var client = NewClient(factory);

        (await PageAsync(client, "/import")).ShouldContain("30 MB at most");
    }

    // ---- The examples ----

    [Fact]
    public async Task AnExampleWithPassages_IsIndexedOnDemand_ThenItsCharactersUseThem()
    {
        var models = new Models();
        using var factory = NewFactory(models);
        var example = Analyzed();
        await PutExampleAsync(factory, example, Set());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory, models);

        var before = await PageAsync(client, "/books/" + example.Id);
        before.ShouldContain("The passages are not indexed yet.");
        before.ShouldContain("Index the passages");
        before.ShouldContain(Warning);

        using var response = await PostAsync(client, $"/books/{example.Id}?handler=Reindex", "/books/" + example.Id);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        await IdleAsync(factory, example.Id, BookShelf.Examples);

        var after = await PageAsync(client, "/books/" + example.Id);
        after.ShouldContain("Indexed: 3 passages, with bge-m3.");
        after.ShouldNotContain("handler=Reindex");
        models.Vectorized.ShouldBe(["Passage number 1 of the novel."]);
        (await Index(factory).GetStateAsync(Key(example.Id), Token)).ShouldBeNull(); // Nothing under the library.

        var chat = $"/books/{example.Id}/characters/1/chat";
        using var request = new HttpRequestMessage(HttpMethod.Post, chat + "?handler=Message")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { messages = new[] { new { role = "user", content = "Hello" } } }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("RequestVerificationToken", await TokenOfAsync(client, chat));
        using var reply = await client.SendAsync(request, Token);
        await reply.Content.ReadAsStringAsync(Token);
        models.Systems.ShouldHaveSingleItem().ShouldContain("of the novel.");
    }

    [Fact]
    public async Task WithoutAModelForVectorization_AnExampleSaysNothingAboutPassages_ButItsExportStillWarns()
    {
        using var factory = NewFactory(new Models());
        var example = Analyzed();
        await PutExampleAsync(factory, example, Set());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory, models: null, vectorization: null);

        var page = await PageAsync(client, "/books/" + example.Id);

        page.ShouldNotContain("Passages of the book");
        page.ShouldContain(Warning);
        var file = await client.GetByteArrayAsync($"/books/{example.Id}/export", Token);
        new JsonBookArchive().Parse(file).Passages!.Passages.Count.ShouldBe(3);
    }

    [Fact]
    public async Task AnExampleAddedToTheLibrary_KeepsItsPassages_AndTheyAreChecked()
    {
        var models = new Models();
        using var factory = NewFactory(models);
        var example = Analyzed();
        await PutExampleAsync(factory, example, Set());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory, models);

        using var response = await PostAsync(client, "/examples?handler=Add", "/examples", new Dictionary<string, string> { ["id"] = example.Id.ToString() });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var copy = (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldHaveSingleItem();
        await IdleAsync(factory, copy.Id);
        (await PageAsync(client, "/books/" + copy.Id)).ShouldContain("Indexed: 3 passages, with bge-m3.");
        models.Vectorized.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheNewWords_AreThereInFrenchToo()
    {
        using var factory = NewFactory(new Models());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);
        var book = await InLibraryAsync(factory);

        (await PageAsync(client, "/books/" + book.Id, "fr")).ShouldContain("protégé par le droit d’auteur");
    }
}
