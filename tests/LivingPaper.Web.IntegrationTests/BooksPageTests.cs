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

// Sending, listing and deleting books on the home page (PAPER-7), with the real readers of EPUB and PDF files.
public partial class BooksPageTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    // The analysis is not what these tests are about: the queue is replaced by one that only remembers, so a book stays
    // waiting for its analysis and nothing runs in the background (the analysis has its own tests).
    private static LivingPaperFactory NewFactory() =>
        LivingPaperFactory.With(configureServices: services => services.AddSingleton<IAnalysisScheduler>(new RecordingScheduler()));

    private static HttpClient NewClient(LivingPaperFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<string> GetAsync(HttpClient client, string path = "/", string? acceptLanguage = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        using var response = await client.SendAsync(request, Token);
        return await response.Content.ReadAsStringAsync(Token);
    }

    private static async Task<HttpResponseMessage> SendBookAsync(HttpClient client, byte[]? file, string fileName = "lighthouse.epub", string? title = null, bool replace = false, bool withToken = true, string? acceptLanguage = null)
    {
        using var content = new MultipartFormDataContent();
        if (withToken)
        {
            content.Add(new StringContent(AntiforgeryToken().Match(await GetAsync(client)).Groups[1].Value), "__RequestVerificationToken");
        }

        if (file is not null)
        {
            var part = new ByteArrayContent(file);
            part.Headers.ContentType = new("application/octet-stream");
            content.Add(part, "file", fileName);
        }

        if (title is not null)
        {
            content.Add(new StringContent(title), "title");
        }

        if (replace)
        {
            content.Add(new StringContent("true"), "replace");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Add") { Content = content };
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        return await client.SendAsync(request, Token);
    }

    private static async Task<HttpResponseMessage> PostDeleteAsync(HttpClient client, string id)
    {
        var token = AntiforgeryToken().Match(await GetAsync(client)).Groups[1].Value;
        return await client.PostAsync("/?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = id, ["__RequestVerificationToken"] = token }), Token);
    }

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    // A small EPUB made in memory: no real book is committed.
    private static byte[] Epub(string title = "The Lighthouse", string text = "Chapter one: Lina arrives at the lighthouse.")
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "META-INF/container.xml", """<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="content.opf"/></rootfiles></container>""");
            Add(zip, "content.opf", $"""<package xmlns="http://www.idpf.org/2007/opf"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>{title}</dc:title></metadata><manifest><item id="a" href="a.xhtml"/></manifest><spine><itemref idref="a"/></spine></package>""");
            Add(zip, "a.xhtml", $"<html><body><p>{text}</p></body></html>");
        }

        return memory.ToArray();
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static async Task<string> AddBookAsync(HttpClient client, string title = "The Lighthouse")
    {
        using var response = await SendBookAsync(client, Epub(title), title: title);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return await GetAsync(client);
    }

    // ---- The page ----

    [Fact]
    public async Task WithNoBook_ThePageSaysSo_AndOffersTheForm()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var library = Decoded(await GetAsync(client));
        var html = Decoded(await GetAsync(client, "/Add"));

        library.ShouldContain("Your library is empty");
        Regex.IsMatch(library, @"<a[^>]*href=""/Add""[^>]*>Add a book</a>").ShouldBeTrue();
        html.ShouldContain("enctype=\"multipart/form-data\"");
        html.ShouldContain("type=\"file\"");
        html.ShouldContain("name=\"file\"");
        html.ShouldContain("name=\"title\"");
        html.ShouldContain("name=\"replace\"");
        html.ShouldContain("20 MB");
        library.ShouldContain("Chat with the characters of your books.");
    }

    [Fact]
    public async Task EveryFieldHasAVisibleLabel_AndTheFileFieldOnlyOffersEpubAndPdf()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var html = await GetAsync(client, "/Add");

        foreach (var field in new[] { "book-file", "book-title", "book-replace" })
        {
            html.ShouldContain($"for=\"{field}\"");
            html.ShouldContain($"id=\"{field}\"");
        }

        Regex.IsMatch(html, @"<input[^>]*type=""file""[^>]*accept="".epub,.pdf[^""]*""").ShouldBeTrue();
    }

    [Fact]
    public async Task EveryFormOfThePage_CarriesTheAntiforgeryToken()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await SeedAsync(factory, book => { book.Status = BookStatus.Failed; book.ErrorCode = "NoModel"; });

        foreach (var (path, minimum) in new[] { ("/", 2), ("/Add", 1) }) // The library: retry and delete. The form of the page Add.
        {
            var html = await GetAsync(client, path);
            var forms = Regex.Matches(html, @"<form\b[^>]*method=""post""[\s\S]*?</form>", RegexOptions.IgnoreCase).Select(match => match.Value).Where(form => !form.Contains("/Language", StringComparison.Ordinal)).ToList();

            forms.Count.ShouldBeGreaterThanOrEqualTo(minimum, path);
            forms.ShouldAllBe(form => form.Contains("__RequestVerificationToken", StringComparison.Ordinal), path);
        }
    }

    [Fact]
    public async Task WhenAnalysisIsOff_ThePageWarnsThatBooksWillWait_AndLinksToTheSettings()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        foreach (var path in new[] { "/", "/Add" })
        {
            var html = Decoded(await GetAsync(client, path));

            html.ShouldContain("Analysis is off", customMessage: path);
            Regex.IsMatch(html, @"<a[^>]*href=""/Settings""[^>]*>Open the settings</a>").ShouldBeTrue(path);
        }
    }

    // ---- Sending a book ----

    [Fact]
    public async Task ABookSent_IsReadAtOnce_ListedWaiting_AndItsTextIsKeptApart()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await SendBookAsync(client, Epub("Le Malade imaginaire", "Argan est malade."), "malade.epub");
        var page = Decoded(await GetAsync(client, response.Headers.Location!.OriginalString)); // The page of the book.
        var library = Decoded(await GetAsync(client));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var books = await factory.Services.GetRequiredService<BookService>().ListAsync(Token);
        var book = books.Books.ShouldHaveSingleItem();
        response.Headers.Location!.OriginalString.ShouldBe("/Book/" + book.Id);
        page.ShouldContain("Le Malade imaginaire was added");
        page.ShouldContain("Waiting for analysis");
        library.ShouldContain("Waiting for analysis");
        library.ShouldContain("malade.epub");
        book.Status.ShouldBe(BookStatus.Waiting);
        (await File.ReadAllTextAsync(Path.Combine(factory.DataDirectory, "uploads", book.Id + ".txt"), Token)).ShouldBe("Argan est malade.");
        (await File.ReadAllTextAsync(Path.Combine(factory.DataDirectory, "books", book.Id + ".json"), Token)).ShouldNotContain("Argan est malade");
    }

    [Fact]
    public async Task TheTitleTyped_IsTheTitleOfTheBook()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await SendBookAsync(client, Epub("File title"), title: "My title");
        var html = Decoded(await GetAsync(client));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        html.ShouldContain("My title");
    }

    [Fact]
    public async Task AFileRenamedToEpub_ButNotOne_IsRefusedByItsContent()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await SendBookAsync(client, Encoding.UTF8.GetBytes("Just some text, renamed."), "book.epub");
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Error: This file is neither an EPUB nor a PDF");
        Regex.IsMatch(html, @"<input[^>]*id=""book-file""[^>]*aria-invalid=""true""").ShouldBeTrue();
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task ABrokenEpub_ASentNothing_AndAScannedLookingBook_AreRefusedWithAClearMessage()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var damaged = await SendBookAsync(client, [(byte)'P', (byte)'K', 3, 4, 1, 2, 3, 4, 5, 6]);
        using var nothing = await SendBookAsync(client, null);
        using var empty = await SendBookAsync(client, Epub("Album", string.Empty));

        Decoded(await damaged.Content.ReadAsStringAsync(Token)).ShouldContain("damaged or incomplete");
        Decoded(await nothing.Content.ReadAsStringAsync(Token)).ShouldContain("No file was sent");
        Decoded(await empty.Content.ReadAsStringAsync(Token)).ShouldContain("holds no readable text");
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFileOverTheLimit_IsRefused_WithItsLimitInTheMessage()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var big = new byte[(int)BookService.MaxBookBytes + 1];
        big[0] = (byte)'P';
        big[1] = (byte)'K';
        big[2] = 3;
        big[3] = 4;

        using var response = await SendBookAsync(client, big);
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        html.ShouldContain("larger than 20 MB");
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheSameTitleTwice_AsksToTickReplace_AndReplaceReplacesTheBook()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await AddBookAsync(client, "Le Malade imaginaire");
        var service = factory.Services.GetRequiredService<BookService>();
        var firstId = (await service.ListAsync(Token)).Books.Single().Id;

        using var refused = await SendBookAsync(client, Epub("Le Malade imaginaire"), title: "le malade IMAGINAIRE");
        var refusedHtml = Decoded(await refused.Content.ReadAsStringAsync(Token));
        using var replaced = await SendBookAsync(client, Epub("Le Malade imaginaire"), title: "Le Malade imaginaire", replace: true);
        var html = Decoded(await GetAsync(client, replaced.Headers.Location!.OriginalString)); // The page of the new book.

        refusedHtml.ShouldContain("already there");
        refusedHtml.ShouldContain("tick");
        refusedHtml.ShouldContain("value=\"le malade IMAGINAIRE\""); // What was typed is shown again.
        html.ShouldContain("replaced the earlier book");
        var books = (await service.ListAsync(Token)).Books;
        books.ShouldHaveSingleItem().Id.ShouldNotBe(firstId);
        File.Exists(Path.Combine(factory.DataDirectory, "books", firstId + ".json")).ShouldBeFalse();
        File.Exists(Path.Combine(factory.DataDirectory, "uploads", firstId + ".txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryToken_IsRefused_AndSavesNothing()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await SendBookAsync(client, Epub(), withToken: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheTextOfABook_NeverReachesTheLogs()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        (await SendBookAsync(client, Epub("Secret Title", "A very particular sentence of the book."))).Dispose();
        (await SendBookAsync(client, Encoding.UTF8.GetBytes("A very particular sentence of the book."))).Dispose();

        factory.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains("very particular sentence", StringComparison.Ordinal));
    }

    // ---- Deleting a book ----

    [Fact]
    public async Task Deleting_AsksForConfirmation_WithoutScript()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var html = Decoded(await AddBookAsync(client));

        html.ShouldContain("<details");
        html.ShouldContain("This deletes the book");
        html.ShouldContain("Confirm the deletion");
        html.ShouldNotContain("onclick");
    }

    [Fact]
    public async Task ConfirmingTheDeletion_RemovesTheBookItsFileAndItsText()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await AddBookAsync(client);
        var id = (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.Single().Id;

        using var response = await PostDeleteAsync(client, id.ToString());
        var html = Decoded(await GetAsync(client, response.Headers.Location!.OriginalString));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        html.ShouldContain("The Lighthouse was deleted");
        html.ShouldContain("Your library is empty");
        File.Exists(Path.Combine(factory.DataDirectory, "books", id + ".json")).ShouldBeFalse();
        File.Exists(Path.Combine(factory.DataDirectory, "uploads", id + ".txt")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("../../etc/passwd")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public async Task DeletingSomethingThatIsNotABook_IsANotFound(string id)
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await PostDeleteAsync(client, id);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ABookFileThatCannotBeRead_IsReported_AndLeftAlone()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        Directory.CreateDirectory(Path.Combine(factory.DataDirectory, "books"));
        var broken = Path.Combine(factory.DataDirectory, "books", "0123456789abcdef0123456789abcdef.json");
        await File.WriteAllTextAsync(broken, "{ SECRET-LOOKING TEXT", Token);

        var html = Decoded(await GetAsync(client));

        html.ShouldContain("could not be read");
        html.ShouldContain("0123456789abcdef0123456789abcdef.json");
        html.ShouldNotContain("SECRET-LOOKING");
        (await File.ReadAllTextAsync(broken, Token)).ShouldBe("{ SECRET-LOOKING TEXT");
    }

    // ---- Language ----

    [Fact]
    public async Task ThePage_IsTranslated_NoEnglishLeftBehind()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var empty = Decoded(await GetAsync(client, acceptLanguage: "fr"));
        using var refused = await SendBookAsync(client, Encoding.UTF8.GetBytes("du texte"), acceptLanguage: "fr");
        var failure = Decoded(await refused.Content.ReadAsStringAsync(Token));

        empty.ShouldContain("Votre bibliothèque est vide");
        empty.ShouldContain("Ajouter un livre");
        failure.ShouldContain("Ce fichier n’est ni un EPUB ni un PDF");
        empty.ShouldNotContain("Your library is empty");
        failure.ShouldNotContain("neither an EPUB");
    }

    // ---- The analysis, as the page shows it ----

    private static async Task<Book> SeedAsync(LivingPaperFactory factory, Action<Book> shape, bool queued = false, string title = "The Lighthouse")
    {
        await factory.Services.GetRequiredService<BookAnalysisWorker>().Recovered; // The restart recovery is over.
        var book = new Book(BookId.New(), title, "lighthouse.epub", DateTimeOffset.UtcNow.AddMinutes(-30));
        book.UpdatedAt = DateTimeOffset.UtcNow; // Moving just now: not stuck.
        shape(book);
        await factory.Services.GetRequiredService<IBookRepository>().SaveAsync(book, Token);
        await factory.Services.GetRequiredService<IBookTextStore>().SaveAsync(book.Id, "text", Token);
        if (queued)
        {
            factory.Services.GetRequiredService<IAnalysisScheduler>().Enqueue(book.Id);
        }

        return book;
    }

    private static async Task<HttpResponseMessage> PostRetryAsync(HttpClient client, string id)
    {
        var token = AntiforgeryToken().Match(await GetAsync(client)).Groups[1].Value;
        return await client.PostAsync("/?handler=Retry", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = id, ["__RequestVerificationToken"] = token }), Token);
    }

    [Fact]
    public async Task ABookWaitingInTheQueue_SaysSo_AndTheProgressOfARunningOneIsShown()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await SeedAsync(factory, book => { }, queued: true, title: "Waiting book");
        await SeedAsync(factory, book => { book.Status = BookStatus.Running; book.PartsTotal = 12; book.PartsDone = 3; }, queued: true, title: "Reading book");
        await SeedAsync(factory, book => { book.Status = BookStatus.Running; book.PartsTotal = 12; book.PartsDone = 12; book.ProfilesTotal = 10; book.ProfilesDone = 4; }, queued: true, title: "Profiles book");

        var html = Decoded(await GetAsync(client));

        html.ShouldContain("Waiting for analysis");
        html.ShouldContain("Analysis in progress");
        html.ShouldContain("3 of 12 parts read");
        html.ShouldContain("4 of 10 profiles written");
        html.ShouldNotContain("interrupted");
        html.ShouldContain("data-analysis-active");
    }

    [Fact]
    public async Task ADoneBook_ShowsItsCharactersModelDurationAndTokens()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await SeedAsync(factory, book =>
        {
            book.Status = BookStatus.Done;
            book.Characters.AddRange([new Character(1, "Argan", "d", "p"), new Character(2, "Toinette", "d", "p")]);
            book.Model = "gpt-5-nano";
            book.ProviderName = "OpenAI";
            book.StartedAt = book.SentAt;
            book.FinishedAt = book.SentAt.AddSeconds(134);
            book.InputTokens = 12345;
            book.CachedInputTokens = 2000;
            book.OutputTokens = 678;
        });

        var book = (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.Single();
        var library = Decoded(await GetAsync(client));
        var html = Decoded(await GetAsync(client, "/Book/" + book.Id));

        library.ShouldContain("Analyzed");
        library.ShouldContain("2 characters");
        library.ShouldContain("analyzed with gpt-5-nano");
        html.ShouldContain("gpt-5-nano (OpenAI)");
        html.ShouldContain("2 min 14 s");
        html.ShouldContain("12,345 in (2,000 cached), 678 out");
        library.ShouldNotContain("data-analysis-active");
        html.ShouldNotContain(">Retry<");
    }

    [Fact]
    public async Task APartLeftOutByTheFilter_IsSaid()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await SeedAsync(factory, book => { book.Status = BookStatus.Done; book.PartsSkipped = 2; });

        (await GetAsync(client)).ShouldContain("2 parts of the book were refused by the content filter of the model and left out");
    }

    [Fact]
    public async Task AFailedBook_SaysWhatWentWrong_AndOffersToRetry()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await SeedAsync(factory, book => { book.Status = BookStatus.Failed; book.ErrorCode = "ModelRefused"; book.ErrorDetail = "HTTP 401"; });

        var html = Decoded(await GetAsync(client));

        html.ShouldContain("Analysis failed");
        html.ShouldContain("The provider refused the request");
        html.ShouldContain("(HTTP 401)");
        Regex.IsMatch(html, @"<button[^>]*>Retry</button>").ShouldBeTrue();
        html.ShouldNotContain("data-analysis-active");
    }

    [Fact]
    public async Task ABookWhoseTextIsGone_CannotBeRetried_ItMustBeSentAgain()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await SeedAsync(factory, book => { book.Status = BookStatus.Failed; book.ErrorCode = "TextMissing"; });

        var html = Decoded(await GetAsync(client));

        html.ShouldContain("no longer stored");
        Regex.IsMatch(html, @"<button[^>]*>Retry</button>").ShouldBeFalse();
    }

    [Fact]
    public async Task ABookRunningButNotInTheQueue_IsShownInterrupted_WithARetryButton()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await SeedAsync(factory, book => book.Status = BookStatus.Running);

        var html = Decoded(await GetAsync(client));

        html.ShouldContain("Analysis interrupted");
        Regex.IsMatch(html, @"<button[^>]*>Retry</button>").ShouldBeTrue();
        html.ShouldNotContain("data-analysis-active");
    }

    [Fact]
    public async Task Retry_PutsTheBookBackInTheQueue_AndGoesBackWithANotice()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedAsync(factory, book => { book.Status = BookStatus.Failed; book.ErrorCode = "NoModel"; });

        using var response = await PostRetryAsync(client, book.Id.ToString());
        var html = Decoded(await GetAsync(client, response.Headers.Location!.OriginalString));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        html.ShouldContain("will start again");
        html.ShouldContain("Waiting for analysis");
        factory.Services.GetRequiredService<IAnalysisScheduler>().IsQueued(book.Id).ShouldBeTrue();
        (await factory.Services.GetRequiredService<IBookRepository>().GetAsync(book.Id, Token))!.ErrorCode.ShouldBeNull();
    }

    [Fact]
    public async Task Retry_OfABookThatIsDone_OrUnknown_IsANotFound()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var done = await SeedAsync(factory, book => book.Status = BookStatus.Done);

        foreach (var id in new[] { done.Id.ToString(), "nope", "0123456789abcdef0123456789abcdef" })
        {
            using var response = await PostRetryAsync(client, id);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task TheListCanBeFetchedAlone_ForTheAutomaticRefresh()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await SeedAsync(factory, book => { book.Status = BookStatus.Running; book.PartsTotal = 4; book.PartsDone = 1; }, queued: true);

        var fragment = Decoded(await GetAsync(client, "/?handler=List"));
        var page = await GetAsync(client);

        fragment.ShouldNotContain("<html");
        fragment.ShouldContain("1 of 4 parts read");
        fragment.ShouldContain("data-analysis-active");
        page.ShouldContain("id=\"book-list\"");
        page.ShouldContain("data-refresh-url=\"/?handler=List\"");
        page.ShouldContain("/js/books.js");
    }

    [Fact]
    public async Task TheScriptOfTheRefresh_IsServed_AndLeavesAnOpenConfirmationAlone()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var script = await GetAsync(client, "/js/books.js");

        script.ShouldContain("data-analysis-active");
        script.ShouldContain("details[open]");
    }

    [Fact]
    public async Task AFailedBook_IsTranslated()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await SeedAsync(factory, book => { book.Status = BookStatus.Failed; book.ErrorCode = "InvalidReply"; });

        var html = Decoded(await GetAsync(client, acceptLanguage: "fr"));

        html.ShouldContain("Analyse échouée");
        html.ShouldContain("Réessayer");
        html.ShouldNotContain("Analysis failed");
    }

    [Fact]
    public async Task ASentBook_IsPutInTheQueueOfTheAnalysis()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await SendBookAsync(client, Epub());

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var id = (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.Single().Id;
        factory.Services.GetRequiredService<IAnalysisScheduler>().IsQueued(id).ShouldBeTrue();
    }

    // A reason for stopping an analysis added later without its text fails here, in every language.
    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public void EveryReasonForAnAnalysisToStop_HasItsText(string language)
    {
        var texts = Texts(language);

        foreach (var failure in Enum.GetNames<AnalysisFailure>())
        {
            texts.Keys.ShouldContain("books.failure." + char.ToLowerInvariant(failure[0]) + failure[1..], $"{language}.json lacks the text of the failure '{failure}'");
        }
    }

    // ---- The texts ----

    private static IReadOnlyDictionary<string, string> Texts(string language) =>
        LivingPaper.Web.Localization.TranslationFile.Parse(
            File.ReadAllText(Path.Combine(RepositoryFiles.Find(Path.Combine("src", "LivingPaper.Web")), "Resources", language + ".json")),
            language + ".json");

    public static TheoryData<string> LanguageCodes() => ["en", "fr"];

    // A reason added later without its text fails here, in every language.
    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public void EveryReasonForRefusingABook_HasItsText(string language)
    {
        var texts = Texts(language);

        foreach (var rejection in Enum.GetNames<BookRejection>())
        {
            texts.Keys.ShouldContain("books.error." + char.ToLowerInvariant(rejection[0]) + rejection[1..], $"{language}.json lacks the text of the reason '{rejection}'");
        }
    }
}
