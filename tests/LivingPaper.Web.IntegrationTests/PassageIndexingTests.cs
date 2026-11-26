using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// PAPER-85: the passages of a book on its page and their indexing in the background of the real application, with a real
// SQLite index in a temporary directory and models that never leave the process.
public partial class PassageIndexingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    private sealed class Models : IChatModelFactory, IEmbeddingModelFactory
    {
        public Exception? EmbeddingFailure { get; set; }

        public TaskCompletionSource? Hold { get; set; }

        IChatModel IChatModelFactory.Create(LlmProvider provider, bool retryPassingFailures) => new Chat();

        IEmbeddingModel IEmbeddingModelFactory.Create(LlmProvider provider, bool retryPassingFailures) => new Vectors(this);

        private sealed class Chat : IChatModel
        {
            public IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
            {
                var system = messages.FirstOrDefault(message => message.Role == ChatRole.System)?.Content ?? string.Empty;
                usage?.Add(100, 0, 50);
                return Task.FromResult(
                    system.Contains("You are reading an excerpt", StringComparison.Ordinal)
                        ? """{"characters":[{"name":"Argan","role":"A hypochondriac.","importance":9}],"scenes":[{"summary":"Argan complains.","present":["Argan"],"firstPassage":1,"lastPassage":1}]}"""
                        : system.Contains("most important characters", StringComparison.Ordinal)
                            ? """{"characters":[{"name":"Argan","shortDescription":"The imaginary invalid."}]}"""
                            : "Argan complains about his health.");
            }
        }

        private sealed class Vectors(Models owner) : IEmbeddingModel
        {
            public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, ChatUsageMeter? usage, CancellationToken cancellationToken)
            {
                if (owner.Hold is { } hold)
                {
                    await hold.Task.WaitAsync(cancellationToken);
                }

                if (owner.EmbeddingFailure is { } failure)
                {
                    throw failure;
                }

                usage?.Add(7 * texts.Count, 0, 0);
                return [.. texts.Select(text => new float[] { text.Length, 1 })];
            }
        }
    }

    private static LivingPaperFactory NewFactory(Models models) => LivingPaperFactory.With(configureServices: services =>
    {
        services.AddSingleton<IChatModelFactory>(models);
        services.AddSingleton<IEmbeddingModelFactory>(models);
    });

    private static HttpClient NewClient(LivingPaperFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    // A chat model chosen for analysis and chat, and, unless told otherwise, a model for vectorization.
    private static async Task ChooseModelsAsync(LivingPaperFactory factory, string? vectorization = "bge-m3")
    {
        var settings = factory.Services.GetRequiredService<LlmSettingsService>();
        (await settings.AddAsync(new LlmProviderDraft("Chat", "https://example.com/v1", "k", "chat-model"), Token)).Succeeded.ShouldBeTrue();
        if (vectorization is not null)
        {
            var added = await settings.AddAsync(new LlmProviderDraft(vectorization, "https://example.com/v1", "k", vectorization), Token);
            (await settings.SelectForEmbeddingAsync(added.Added!.Id, Token))!.Success.ShouldBeTrue();
        }
    }

    private static byte[] Epub()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }

            Add("META-INF/container.xml", """<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="content.opf"/></rootfiles></container>""");
            Add("content.opf", """<package xmlns="http://www.idpf.org/2007/opf"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>Le Malade imaginaire</dc:title></metadata><manifest><item id="a" href="a.xhtml"/></manifest><spine><itemref idref="a"/></spine></package>""");
            Add("a.xhtml", "<html><body><p>Argan est malade et Toinette se moque de lui.</p></body></html>");
        }

        return memory.ToArray();
    }

    private static async Task<Book> WaitForAsync(LivingPaperFactory factory, BookId id, Func<Book, bool> condition)
    {
        var books = factory.Services.GetRequiredService<BookService>();
        var scheduler = factory.Services.GetRequiredService<IAnalysisScheduler>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (await books.GetAsync(id, Token) is { } book && !scheduler.IsQueued(id) && condition(book))
            {
                return book;
            }

            await Task.Delay(50, Token);
        }

        throw new TimeoutException("The book did not reach the expected state in 20 seconds.");
    }

    private static async Task<Book> AnalyzedAsync(LivingPaperFactory factory)
    {
        await factory.Services.GetRequiredService<BookAnalysisWorker>().Recovered;
        using var stream = new MemoryStream(Epub());
        var added = (await factory.Services.GetRequiredService<BookService>().AddAsync(stream, "malade.epub", null, replace: false, Token)).Added!;
        return await WaitForAsync(factory, added.Id, book => book.Status == BookStatus.Done);
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

    private static async Task<HttpResponseMessage> ReindexAsync(HttpClient client, BookId id, bool withToken = true)
    {
        var form = new Dictionary<string, string>();
        if (withToken)
        {
            form["__RequestVerificationToken"] = AntiforgeryToken().Match(await client.GetStringAsync("/books/" + id, Token)).Groups[1].Value;
        }

        return await client.PostAsync($"/books/{id}?handler=Reindex", new FormUrlEncodedContent(form), Token);
    }

    [Fact]
    public async Task AnAnalysisWithAModelForVectorization_IndexesTheBook_AndItsPageSaysSo()
    {
        using var factory = NewFactory(new Models());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);

        var book = await AnalyzedAsync(factory);

        var state = await factory.Services.GetRequiredService<IPassageIndex>().GetStateAsync(new BookKey(BookShelf.Library, book.Id.ToString()), Token);
        state.ShouldBe(new PassageIndexState(1, "bge-m3", 2));
        var page = await PageAsync(client, "/books/" + book.Id);
        page.ShouldContain("Passages of the book");
        page.ShouldContain("Indexed: 1 passage, with bge-m3.");
        page.ShouldContain("Tokens for vectorization");
        page.ShouldNotContain("handler=Reindex");
    }

    [Fact]
    public async Task WhenTheIndexingFails_ThePageSaysWhy_AndRetryIndexesTheBook()
    {
        var models = new Models();
        using var factory = NewFactory(models);
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);
        models.EmbeddingFailure = new ChatProviderException(ChatFailureKind.Http, "refused", 401);
        var book = await AnalyzedAsync(factory);

        var failed = await PageAsync(client, "/books/" + book.Id);
        failed.ShouldContain("The passages are not indexed yet.");
        failed.ShouldContain("The key of the model for vectorization was refused.");
        failed.ShouldContain("Retry indexing");
        failed.ShouldNotContain("refused\"");

        models.EmbeddingFailure = null;
        using var response = await ReindexAsync(client, book.Id);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        await WaitForAsync(factory, book.Id, current => current.IndexErrorCode is null && current.IndexTotal == 0);
        var done = await PageAsync(client, response.Headers.Location!.OriginalString);
        done.ShouldContain("The indexing of Le Malade imaginaire has started.");
        done.ShouldContain("Indexed: 1 passage, with bge-m3.");
    }

    [Fact]
    public async Task AfterAnotherModelIsChosen_ThePageOffersToIndexAgain_AndNothingIsRecomputedByItself()
    {
        var models = new Models();
        using var factory = NewFactory(models);
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);
        var book = await AnalyzedAsync(factory);
        var settings = factory.Services.GetRequiredService<LlmSettingsService>();
        var other = (await settings.AddAsync(new LlmProviderDraft("Other", "https://example.com/v1", "k", "other-vectors"), Token)).Added!;
        await settings.SelectForEmbeddingAsync(other.Id, Token);

        var outdated = await PageAsync(client, "/books/" + book.Id);

        outdated.ShouldContain("To be re-indexed: the passages were indexed with another model (bge-m3).");
        outdated.ShouldContain("Re-index");
        factory.Services.GetRequiredService<IAnalysisScheduler>().IsQueued(book.Id).ShouldBeFalse();
        (await ReindexAsync(client, book.Id)).Dispose();
        await WaitForAsync(factory, book.Id, current => current.IndexTotal == 0);
        (await PageAsync(client, "/books/" + book.Id)).ShouldContain("Indexed: 1 passage, with other-vectors.");
    }

    [Fact]
    public async Task WhileTheBookIsIndexed_ThePageShowsHowFarItIs_RefreshesItself_AndTheBookStaysReadable()
    {
        var models = new Models();
        using var factory = NewFactory(models);
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);
        models.EmbeddingFailure = new ChatProviderException(ChatFailureKind.Unreachable, "down");
        var book = await AnalyzedAsync(factory);
        models.EmbeddingFailure = null;
        models.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        (await ReindexAsync(client, book.Id)).Dispose();
        var page = await PageAsync(client, "/books/" + book.Id);
        var progress = await PageAsync(client, $"/books/{book.Id}?handler=Passages");
        models.Hold.SetResult();

        page.ShouldContain("Indexing the passages: 0 of 1");
        page.ShouldContain("data-refresh-url");
        page.ShouldContain("The characters"); // The book is still done.
        progress.ShouldContain("data-state=\"active\"");
        progress.ShouldNotContain("<html");
        await WaitForAsync(factory, book.Id, current => current.IndexTotal == 0);
        (await PageAsync(client, $"/books/{book.Id}?handler=Passages")).ShouldContain("data-state=\"stopped\"");
    }

    [Fact]
    public async Task ABookAnalyzedWithoutPassages_SaysHowToGetThem()
    {
        using var factory = NewFactory(new Models());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory, vectorization: null);
        var book = await AnalyzedAsync(factory);
        var settings = factory.Services.GetRequiredService<LlmSettingsService>();
        var vectors = (await settings.AddAsync(new LlmProviderDraft("bge", "https://example.com/v1", "k", "bge-m3"), Token)).Added!;
        await settings.SelectForEmbeddingAsync(vectors.Id, Token);

        var page = await PageAsync(client, "/books/" + book.Id);

        page.ShouldContain("This book has no passages");
        page.ShouldNotContain("handler=Reindex");
    }

    [Fact]
    public async Task WithoutAModelForVectorization_ThePageSaysNothingAboutPassages()
    {
        using var factory = NewFactory(new Models());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory, vectorization: null);

        var book = await AnalyzedAsync(factory);

        var page = await PageAsync(client, "/books/" + book.Id);
        page.ShouldNotContain("Passages of the book");
        page.ShouldNotContain("Tokens for vectorization");
        File.Exists(Path.Combine(factory.DataDirectory, "rag.db")).ShouldBeFalse();
        using var progress = await client.GetAsync($"/books/{book.Id}?handler=Passages", Token);
        progress.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reindexing_NeedsTheAntiforgeryToken_AndABookThatExists()
    {
        using var factory = NewFactory(new Models());
        using var client = NewClient(factory);
        await ChooseModelsAsync(factory);
        var book = await AnalyzedAsync(factory);

        using var forged = await ReindexAsync(client, book.Id, withToken: false);
        var token = AntiforgeryToken().Match(await client.GetStringAsync("/books/" + book.Id + "/characters/1", Token)).Groups[1].Value;
        using var unknown = await client.PostAsync(
            "/books/no-such-book?handler=Reindex", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }), Token);

        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletingABook_RemovesItsPassagesAndVectors()
    {
        using var factory = NewFactory(new Models());
        await ChooseModelsAsync(factory);
        var book = await AnalyzedAsync(factory);

        await factory.Services.GetRequiredService<BookService>().DeleteAsync(book.Id, Token);

        (await factory.Services.GetRequiredService<IPassageIndex>().GetStateAsync(new BookKey(BookShelf.Library, book.Id.ToString()), Token)).ShouldBeNull();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void EveryReasonOfAFailedIndexing_HasItsText(string language)
    {
        var texts = LivingPaper.Web.Localization.TranslationFile.Parse(
            File.ReadAllText(Path.Combine(RepositoryFiles.Find(Path.Combine("src", "LivingPaper.Web")), "Resources", language + ".json")), language + ".json");

        foreach (var failure in Enum.GetNames<IndexFailure>())
        {
            texts.Keys.ShouldContain("index.failure." + char.ToLowerInvariant(failure[0]) + failure[1..]);
        }
    }
}
