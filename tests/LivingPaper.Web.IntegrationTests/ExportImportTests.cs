using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// Exporting an analyzed book and importing one (PAPER-12), through the real application: what is downloaded, what an import
// accepts and refuses, and the round trip.
public partial class ExportImportTests
{
    private const string Key = "sk-export-secret-key-8642";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    private sealed class PongChatFactory : IChatModelFactory
    {
        public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true) => new Model();

        private sealed class Model : IChatModel
        {
            public IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) => Task.FromResult("pong");
        }
    }

    private static LivingPaperFactory NewFactory(string? examples = null) =>
        LivingPaperFactory.With(
            examplesDirectory: examples,
            configureServices: services =>
            {
                services.AddSingleton<IChatModelFactory>(new PongChatFactory());
                services.AddSingleton<IAnalysisScheduler>(new RecordingScheduler());
            });

    private static HttpClient NewClient(LivingPaperFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    private static async Task<Book> SeedAsync(LivingPaperFactory factory, Action<Book>? more = null, string title = "Le Malade imaginaire")
    {
        await factory.Services.GetRequiredService<BookAnalysisWorker>().Recovered;
        var book = new Book(BookId.New(), title, "malade.pdf", DateTimeOffset.UtcNow.AddDays(-2))
        {
            Status = BookStatus.Done,
            Summary = "France, 1673. Une comédie.",
            ProviderName = "OpenAI",
            Model = "gpt-5-nano",
            PartsDone = 2,
            PartsTotal = 2,
            ProfilesDone = 2,
            ProfilesTotal = 2,
            InputTokens = 1200,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-2),
            FinishedAt = DateTimeOffset.UtcNow.AddDays(-2).AddMinutes(4),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2).AddMinutes(4),
        };
        book.Characters.AddRange([new Character(1, "Argan", "Un hypocondriaque.", "Je suis Argan.\n\nJ’ai peur de mourir."), new Character(2, "Toinette", "La servante.", "Je suis Toinette.")]);
        more?.Invoke(book);
        await factory.Services.GetRequiredService<IBookRepository>().SaveAsync(book, Token);
        return book;
    }

    private static async Task<(HttpStatusCode Status, string Html)> GetAsync(HttpClient client, string path, string? acceptLanguage = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        using var response = await client.SendAsync(request, Token);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    private static async Task<HttpResponseMessage> ImportAsync(HttpClient client, byte[]? file, bool replace = false, bool withToken = true, string? acceptLanguage = null, string fileName = "book.json")
    {
        using var content = new MultipartFormDataContent();
        if (withToken)
        {
            content.Add(new StringContent(AntiforgeryToken().Match((await GetAsync(client, "/Import")).Html).Groups[1].Value), "__RequestVerificationToken");
        }

        if (file is not null)
        {
            var part = new ByteArrayContent(file);
            part.Headers.ContentType = new("application/json");
            content.Add(part, "file", fileName);
        }

        if (replace)
        {
            content.Add(new StringContent("true"), "replace");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Import") { Content = content };
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        return await client.SendAsync(request, Token);
    }

    private static async Task<byte[]> ExportBytesAsync(HttpClient client, Book book)
    {
        using var response = await client.GetAsync($"/Book/{book.Id}/Export", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync(Token);
    }

    // A file as an export would give it, then changed by the test.
    private static byte[] Edited(byte[] export, Action<Dictionary<string, object?>> change)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, object?>>(export)!;
        change(map);
        return JsonSerializer.SerializeToUtf8Bytes(map);
    }

    // ---- The export ----

    [Fact]
    public async Task TheExport_IsADownloadOfTheBookAsJson_WithASafeNameAndNoSniffing()
    {
        using var factory = NewFactory();
        var book = await SeedAsync(factory);
        using var client = NewClient(factory);

        using var response = await client.GetAsync($"/Book/{book.Id}/Export", Token);
        var bytes = await response.Content.ReadAsByteArrayAsync(Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("le-malade-imaginaire.json");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var json = JsonDocument.Parse(bytes);
        json.RootElement.GetProperty("title").GetString().ShouldBe("Le Malade imaginaire");
        json.RootElement.GetProperty("characters").GetArrayLength().ShouldBe(2);
        Encoding.UTF8.GetString(bytes).ShouldContain("J’ai peur de mourir.");
    }

    [Fact]
    public async Task TheExport_HoldsNoKeyNoSettingNoPathAndNoText()
    {
        using var factory = NewFactory();
        (await factory.Services.GetRequiredService<LlmSettingsService>().AddAsync(new LlmProviderDraft("OpenAI", "https://api.openai.com/v1", Key, "gpt-5-nano"), Token)).Succeeded.ShouldBeTrue();
        var book = await SeedAsync(factory);
        await factory.Services.GetRequiredService<IBookTextStore>().SaveAsync(book.Id, "THE-WHOLE-TEXT-OF-THE-BOOK", Token);
        using var client = NewClient(factory);

        var text = Encoding.UTF8.GetString(await ExportBytesAsync(client, book));

        text.ShouldNotContain(Key);
        text.ShouldNotContain("apiKey", Case.Insensitive);
        text.ShouldNotContain("https://api.openai.com");
        text.ShouldNotContain(factory.DataDirectory);
        text.ShouldNotContain("THE-WHOLE-TEXT-OF-THE-BOOK");
        text.ShouldNotContain("keys");
    }

    [Theory]
    [InlineData(BookStatus.Waiting)]
    [InlineData(BookStatus.Running)]
    [InlineData(BookStatus.Failed)]
    public async Task ABookThatIsNotAnalyzed_CannotBeExported(BookStatus status)
    {
        using var factory = NewFactory();
        var book = await SeedAsync(factory, made => made.Status = status);
        using var client = NewClient(factory);

        (await GetAsync(client, $"/Book/{book.Id}/Export")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/Book/nope/Export")]
    [InlineData("/Book/0123456789abcdef0123456789abcdef/Export")]
    public async Task AnUnknownBook_IsAFriendly404(string path)
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var (status, html) = await GetAsync(client, path);

        status.ShouldBe(HttpStatusCode.NotFound);
        Decoded(html).ShouldContain("Page not found");
    }

    [Fact]
    public async Task TheTitleOfTheBook_NeverMakesTheFileNameUnsafe()
    {
        using var factory = NewFactory();
        var book = await SeedAsync(factory, title: "Évil \"title\" ../../etc/passwd");
        using var client = NewClient(factory);

        using var response = await client.GetAsync($"/Book/{book.Id}/Export", Token);

        response.Content.Headers.ContentDisposition!.FileName.ShouldBe("evil-title-etc-passwd.json");
    }

    [Fact]
    public async Task TheLibraryAndThePageOfABook_OfferTheExport_OnlyForAnAnalyzedBook()
    {
        using var factory = NewFactory();
        var done = await SeedAsync(factory);
        var failed = await SeedAsync(factory, made => { made.Status = BookStatus.Failed; made.ErrorCode = "NoModel"; }, title: "Failed book");
        using var client = NewClient(factory);

        var library = Decoded((await GetAsync(client, "/")).Html);
        var page = Decoded((await GetAsync(client, "/Book/" + done.Id)).Html);

        library.ShouldContain($"href=\"/Book/{done.Id}/Export\"");
        library.ShouldNotContain($"/Book/{failed.Id}/Export");
        library.ShouldContain("aria-label=\"Export Le Malade imaginaire as a JSON file\"");
        page.ShouldContain($"href=\"/Book/{done.Id}/Export\"");
        page.ShouldContain("holds no key and no setting");
    }

    [Fact]
    public async Task AnExample_CanBeExported_ToProposeItAsAnExample()
    {
        using var scratch = new TempExamples();
        using var factory = NewFactory(scratch.Path);
        var example = await scratch.PutAsync(new Book(BookId.New(), "Alice in Wonderland", "alice.epub", DateTimeOffset.UtcNow) { Status = BookStatus.Done }, "alice.json");
        using var client = NewClient(factory);

        using var response = await client.GetAsync($"/Book/{example.Id}/Export", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName.ShouldBe("alice-in-wonderland.json");
    }

    // A directory of examples that a test fills, and removes.
    private sealed class TempExamples : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "livingpaper-export-examples-" + Guid.NewGuid().ToString("N"));

        public TempExamples() => Directory.CreateDirectory(Path);

        public async Task<Book> PutAsync(Book book, string fileName)
        {
            book.Characters.Add(new Character(1, "Alice", "A girl.", "I am Alice."));
            var scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "livingpaper-export-scratch-" + Guid.NewGuid().ToString("N"));
            await new JsonBookRepository(scratch).SaveAsync(book, Token);
            File.Move(System.IO.Path.Combine(scratch, book.Id + ".json"), System.IO.Path.Combine(Path, fileName));
            Directory.Delete(scratch, recursive: true);
            return book;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    // ---- The round trip ----

    [Fact]
    public async Task AnExport_ImportedInAnotherLibrary_GivesTheSameBook()
    {
        using var first = NewFactory();
        var original = await SeedAsync(first, made => made.Characters[0] = made.Characters[0] with { ChatProfile = "A sheet edited by hand.\n\nTwo paragraphs." });
        using var firstClient = NewClient(first);
        var file = await ExportBytesAsync(firstClient, original);

        using var second = NewFactory();
        using var secondClient = NewClient(second);
        using var response = await ImportAsync(secondClient, file);
        var imported = (await second.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.Single();

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/Book/" + original.Id);
        imported.ShouldBeEquivalentTo(original);
        (await ExportBytesAsync(secondClient, imported)).ShouldBe(file); // The same file comes out again.
    }

    [Fact]
    public async Task TheExampleOfTheRepository_ExportedAndImported_RoundTrips()
    {
        var shipped = await new JsonExampleCatalog(RepositoryFiles.Find("examples")).ListAsync(Token);
        var example = shipped.Books[0];
        using var factory = NewFactory(RepositoryFiles.Find("examples"));
        using var client = NewClient(factory);

        var file = await ExportBytesAsync(client, example);
        using var response = await ImportAsync(client, file);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var imported = (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.Single();
        imported.Id.ShouldNotBe(example.Id); // The example keeps its own id: it is not hidden.
        imported.Characters.ShouldBe(example.Characters);
        imported.Title.ShouldBe(example.Title);
        (await client.GetAsync("/Book/" + example.Id, Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- The import ----

    [Fact]
    public async Task ThePage_OffersTheFileAndTheReplaceBox_WithLabels_AndTheLibraryLinksToIt()
    {
        using var factory = NewFactory();
        await SeedAsync(factory);
        using var client = NewClient(factory);

        var (status, raw) = await GetAsync(client, "/Import");
        var html = Decoded(raw);
        var library = (await GetAsync(client, "/")).Html;

        status.ShouldBe(HttpStatusCode.OK);
        Regex.Count(html, @"<h1\b").ShouldBe(1);
        html.ShouldContain("enctype=\"multipart/form-data\"");
        Regex.IsMatch(html, @"<input[^>]*type=""file""[^>]*accept="".json,application/json""").ShouldBeTrue();
        foreach (var field in new[] { "import-file", "import-replace" })
        {
            html.ShouldContain($"for=\"{field}\"");
            html.ShouldContain($"id=\"{field}\"");
        }

        raw.ShouldContain("__RequestVerificationToken");
        html.ShouldContain("1024 KB at most");
        Regex.IsMatch(library, @"<a[^>]*href=""/Import""[^>]*>Import a book</a>").ShouldBeTrue();
    }

    [Fact]
    public async Task AnImportedBook_IsAddedFinished_NothingIsAnalyzed_AndItsPageOpensWithANotice()
    {
        using var first = NewFactory();
        var original = await SeedAsync(first);
        var file = await ExportBytesAsync(NewClient(first), original);
        var scheduler = new RecordingScheduler();
        using var second = LivingPaperFactory.With(configureServices: services =>
        {
            services.AddSingleton<IChatModelFactory>(new PongChatFactory());
            services.AddSingleton<IAnalysisScheduler>(scheduler);
        });
        using var client = NewClient(second);

        using var response = await ImportAsync(client, file);
        var page = Decoded((await GetAsync(client, response.Headers.Location!.OriginalString)).Html);

        page.ShouldContain("Le Malade imaginaire was imported.");
        page.ShouldContain("Argan");
        page.ShouldContain("Toinette");
        scheduler.Queued.ShouldBeEmpty();
        Directory.Exists(Path.Combine(second.DataDirectory, "uploads")).ShouldBeFalse();
    }

    public static TheoryData<string, string> RefusedFiles() => new()
    {
        { "{ SECRET-LOOKING TEXT, not json", "not a LivingPaper book" },
        { "null", "not a LivingPaper book" },
        { "<html>a page</html>", "not a LivingPaper book" },
        { """{"schemaVersion":1}""", "This file is not a valid book" },
    };

    [Theory]
    [MemberData(nameof(RefusedFiles))]
    public async Task AFileThatIsNotABook_IsRefused_WithAClearMessage_NothingIsKept_NothingIsRepeated(string content, string expected)
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await ImportAsync(client, Encoding.UTF8.GetBytes(content));
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain(expected);
        html.ShouldNotContain("SECRET-LOOKING");
        Regex.IsMatch(html, @"<input[^>]*id=""import-file""[^>]*aria-invalid=""true""").ShouldBeTrue();
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task NothingSent_OrAnEmptyFile_IsRefused()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var nothing = await ImportAsync(client, null);
        using var empty = await ImportAsync(client, []);

        Decoded(await nothing.Content.ReadAsStringAsync(Token)).ShouldContain("No file was sent");
        Decoded(await empty.Content.ReadAsStringAsync(Token)).ShouldContain("No file was sent");
    }

    [Fact]
    public async Task AFileOverTheLimit_IsRefused_WithItsLimit()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var big = new byte[(int)BookService.MaxImportBytes + 1];
        Array.Fill(big, (byte)' ');

        using var response = await ImportAsync(client, big);

        Decoded(await response.Content.ReadAsStringAsync(Token)).ShouldContain("larger than 1024 KB");
    }

    [Fact]
    public async Task ANewerVersion_AnUnknownVersion_AMissingField_AndATooLongText_AreEachRefusedForTheirOwnReason()
    {
        using var source = NewFactory();
        var original = await SeedAsync(source);
        var export = await ExportBytesAsync(NewClient(source), original);
        using var factory = NewFactory();
        using var client = NewClient(factory);

        async Task<string> MessageOf(Action<Dictionary<string, object?>> change)
        {
            using var response = await ImportAsync(client, Edited(export, change));
            return Decoded(await response.Content.ReadAsStringAsync(Token));
        }

        (await MessageOf(map => map["schemaVersion"] = 2)).ShouldContain("exported by a newer LivingPaper");
        (await MessageOf(map => map["schemaVersion"] = 0)).ShouldContain("version of the format that this LivingPaper does not know");
        (await MessageOf(map => map.Remove("title"))).ShouldContain("not a valid book");
        (await MessageOf(map => map["title"] = new string('t', BookLimits.Title + 1))).ShouldContain("longer than allowed: title");
        (await MessageOf(map => map["status"] = "Running")).ShouldContain("not analyzed");
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASheetThatBreaksTheRulesOfASheet_IsRefused_NamingTheField()
    {
        using var source = NewFactory();
        var original = await SeedAsync(source);
        var export = await ExportBytesAsync(NewClient(source), original);
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var characters = new List<Dictionary<string, object?>>
        {
            new() { ["position"] = 1, ["name"] = "Argan", ["shortDescription"] = "d", ["chatProfile"] = "p" },
            new() { ["position"] = 2, ["name"] = "ARGAN", ["shortDescription"] = "d", ["chatProfile"] = "p" },
        };

        using var response = await ImportAsync(client, Edited(export, map => map["characters"] = characters));
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        html.ShouldContain("not a valid book: characters[");
        html.ShouldContain("taken");
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryToken_IsRefused_AndKeepsNothing()
    {
        using var source = NewFactory();
        var original = await SeedAsync(source);
        var export = await ExportBytesAsync(NewClient(source), original);
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await ImportAsync(client, export, withToken: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    // ---- A book that is already there ----

    [Fact]
    public async Task ABookAlreadyThere_IsNotReplaced_UntilTheUserTicksReplace_AndSendsTheFileAgain()
    {
        using var factory = NewFactory();
        var original = await SeedAsync(factory);
        using var client = NewClient(factory);
        var file = await ExportBytesAsync(client, original);
        (await factory.Services.GetRequiredService<BookService>().UpdateCharacterAsync(original.Id, 1, new CharacterDraft("Argan", "Edited after the export.", "An edited sheet."), Token)).Outcome.ShouldBe(CharacterUpdateOutcome.Saved);

        using var asked = await ImportAsync(client, file);
        var askedHtml = Decoded(await asked.Content.ReadAsStringAsync(Token));
        var stillEdited = (await factory.Services.GetRequiredService<BookService>().GetAsync(original.Id, Token))!.Characters[0].ShortDescription;
        using var confirmed = await ImportAsync(client, file, replace: true);
        var page = Decoded((await GetAsync(client, confirmed.Headers.Location!.OriginalString)).Html);

        asked.StatusCode.ShouldBe(HttpStatusCode.OK);
        askedHtml.ShouldContain("The library already holds “Le Malade imaginaire”");
        askedHtml.ShouldContain("tick “Replace” and choose the file again");
        Regex.IsMatch(askedHtml, @"<input[^>]*id=""import-replace""").ShouldBeTrue();
        stillEdited.ShouldBe("Edited after the export.");
        confirmed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        page.ShouldContain("was imported and replaced");
        (await factory.Services.GetRequiredService<BookService>().GetAsync(original.Id, Token))!.Characters[0].ShortDescription.ShouldBe("Un hypocondriaque.");
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ABookWithTheSameTitleButAnotherId_AlsoAsksBeforeReplacing_AndReplacesWhenAsked()
    {
        using var source = NewFactory();
        var other = await SeedAsync(source);
        var file = await ExportBytesAsync(NewClient(source), other);
        using var factory = NewFactory();
        var mine = await SeedAsync(factory); // Same title, another id.
        using var client = NewClient(factory);

        using var asked = await ImportAsync(client, file);
        using var confirmed = await ImportAsync(client, file, replace: true);

        Decoded(await asked.Content.ReadAsStringAsync(Token)).ShouldContain("same title or the same identifier");
        (await factory.Services.GetRequiredService<BookService>().GetAsync(mine.Id, Token)).ShouldBeNull();
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.Single().Id.ShouldBe(other.Id);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    // ---- Nothing leaks, everything is translated ----

    [Fact]
    public async Task TheContentOfAFile_NeverReachesTheLogs()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        (await ImportAsync(client, Encoding.UTF8.GetBytes("{ \"title\": \"SECRET-TITLE-IN-A-FILE\" not json"))).Dispose();
        (await ImportAsync(client, Encoding.UTF8.GetBytes("""{"schemaVersion":1,"id":"0123456789abcdef0123456789abcdef","title":"SECRET-TITLE-OF-INVALID"}"""))).Dispose();

        factory.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains("SECRET-TITLE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThePageAndItsErrors_AreTranslated()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var page = Decoded((await GetAsync(client, "/Import", "fr")).Html);
        using var refused = await ImportAsync(client, Encoding.UTF8.GetBytes("not json"), acceptLanguage: "fr");

        page.ShouldContain("Importer un livre");
        page.ShouldContain("Fichier du livre");
        Decoded(await refused.Content.ReadAsStringAsync(Token)).ShouldContain("Ce fichier n’est pas un livre de LivingPaper");
    }

    private static IReadOnlyDictionary<string, string> Texts(string language) =>
        LivingPaper.Web.Localization.TranslationFile.Parse(
            File.ReadAllText(Path.Combine(RepositoryFiles.Find(Path.Combine("src", "LivingPaper.Web")), "Resources", language + ".json")),
            language + ".json");

    public static TheoryData<string> LanguageCodes() => ["en", "fr"];

    // A reason for refusing a file added later without its text fails here, in every language.
    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public void EveryReasonForRefusingAFile_HasItsText(string language)
    {
        var texts = Texts(language);

        foreach (var rejection in Enum.GetNames<ImportRejection>())
        {
            texts.Keys.ShouldContain("import.error." + char.ToLowerInvariant(rejection[0]) + rejection[1..], $"{language}.json lacks the text of the reason '{rejection}'");
        }
    }
}
