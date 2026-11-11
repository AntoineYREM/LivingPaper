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

        using var request = new HttpRequestMessage(HttpMethod.Post, "/?handler=Add") { Content = content };
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
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        var html = Decoded(await GetAsync(client));

        html.ShouldContain("No book yet");
        html.ShouldContain("enctype=\"multipart/form-data\"");
        html.ShouldContain("type=\"file\"");
        html.ShouldContain("name=\"file\"");
        html.ShouldContain("name=\"title\"");
        html.ShouldContain("name=\"replace\"");
        html.ShouldContain("20 MB");
        html.ShouldContain("Chat with the characters of your books.");
    }

    [Fact]
    public async Task EveryFieldHasAVisibleLabel_AndTheFileFieldOnlyOffersEpubAndPdf()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        var html = await GetAsync(client);

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
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);
        var html = await AddBookAsync(client);

        var forms = Regex.Matches(html, @"<form\b[^>]*method=""post""[\s\S]*?</form>", RegexOptions.IgnoreCase).Select(match => match.Value).Where(form => !form.Contains("/Language", StringComparison.Ordinal)).ToList();

        forms.Count.ShouldBeGreaterThanOrEqualTo(2); // Add and delete.
        forms.ShouldAllBe(form => form.Contains("__RequestVerificationToken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenAnalysisIsOff_ThePageWarnsThatBooksWillWait_AndLinksToTheSettings()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        var html = Decoded(await GetAsync(client));

        html.ShouldContain("Analysis is off");
        Regex.IsMatch(html, @"<a[^>]*href=""/Settings""[^>]*>Open the settings</a>").ShouldBeTrue();
    }

    // ---- Sending a book ----

    [Fact]
    public async Task ABookSent_IsReadAtOnce_ListedWaiting_AndItsTextIsKeptApart()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        using var response = await SendBookAsync(client, Epub("Le Malade imaginaire", "Argan est malade."), "malade.epub");
        var html = Decoded(await GetAsync(client, response.Headers.Location!.OriginalString));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        html.ShouldContain("Le Malade imaginaire was added");
        html.ShouldContain("Waiting for analysis");
        html.ShouldContain("malade.epub");
        var books = await factory.Services.GetRequiredService<BookService>().ListAsync(Token);
        var book = books.Books.ShouldHaveSingleItem();
        book.Status.ShouldBe(BookStatus.Waiting);
        (await File.ReadAllTextAsync(Path.Combine(factory.DataDirectory, "uploads", book.Id + ".txt"), Token)).ShouldBe("Argan est malade.");
        (await File.ReadAllTextAsync(Path.Combine(factory.DataDirectory, "books", book.Id + ".json"), Token)).ShouldNotContain("Argan est malade");
    }

    [Fact]
    public async Task TheTitleTyped_IsTheTitleOfTheBook()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        using var response = await SendBookAsync(client, Epub("File title"), title: "My title");
        var html = Decoded(await GetAsync(client));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        html.ShouldContain("My title");
    }

    [Fact]
    public async Task AFileRenamedToEpub_ButNotOne_IsRefusedByItsContent()
    {
        using var factory = new LivingPaperFactory();
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
        using var factory = new LivingPaperFactory();
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
        using var factory = new LivingPaperFactory();
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
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);
        await AddBookAsync(client, "Le Malade imaginaire");
        var service = factory.Services.GetRequiredService<BookService>();
        var firstId = (await service.ListAsync(Token)).Books.Single().Id;

        using var refused = await SendBookAsync(client, Epub("Le Malade imaginaire"), title: "le malade IMAGINAIRE");
        var refusedHtml = Decoded(await refused.Content.ReadAsStringAsync(Token));
        using var replaced = await SendBookAsync(client, Epub("Le Malade imaginaire"), title: "Le Malade imaginaire", replace: true);
        var html = Decoded(await GetAsync(client, replaced.Headers.Location!.OriginalString));

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
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        using var response = await SendBookAsync(client, Epub(), withToken: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheTextOfABook_NeverReachesTheLogs()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        (await SendBookAsync(client, Epub("Secret Title", "A very particular sentence of the book."))).Dispose();
        (await SendBookAsync(client, Encoding.UTF8.GetBytes("A very particular sentence of the book."))).Dispose();

        factory.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains("very particular sentence", StringComparison.Ordinal));
    }

    // ---- Deleting a book ----

    [Fact]
    public async Task Deleting_AsksForConfirmation_WithoutScript()
    {
        using var factory = new LivingPaperFactory();
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
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);
        await AddBookAsync(client);
        var id = (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.Single().Id;

        using var response = await PostDeleteAsync(client, id.ToString());
        var html = Decoded(await GetAsync(client, response.Headers.Location!.OriginalString));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        html.ShouldContain("The Lighthouse was deleted");
        html.ShouldContain("No book yet");
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
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        using var response = await PostDeleteAsync(client, id);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ABookFileThatCannotBeRead_IsReported_AndLeftAlone()
    {
        using var factory = new LivingPaperFactory();
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
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        var empty = Decoded(await GetAsync(client, acceptLanguage: "fr"));
        using var refused = await SendBookAsync(client, Encoding.UTF8.GetBytes("du texte"), acceptLanguage: "fr");
        var failure = Decoded(await refused.Content.ReadAsStringAsync(Token));

        empty.ShouldContain("Aucun livre pour l’instant");
        empty.ShouldContain("Ajouter un livre");
        failure.ShouldContain("Ce fichier n’est ni un EPUB ni un PDF");
        empty.ShouldNotContain("No book yet");
        failure.ShouldNotContain("neither an EPUB");
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
