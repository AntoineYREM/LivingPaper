using System.Net;
using System.Text.RegularExpressions;
using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The page of a book and the sheet of a character (PAPER-9): what a finished analysis shows, what a running or failed one
// shows, changing a sheet, and a book or a character that is not there.
public partial class BookPageTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    // The analysis is not what these tests are about: the queue only remembers, nothing runs in the background.
    private static LivingPaperFactory NewFactory() =>
        LivingPaperFactory.With(configureServices: services => services.AddSingleton<IAnalysisScheduler>(new RecordingScheduler()));

    private static HttpClient NewClient(LivingPaperFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

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

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> form, bool withToken = true, string? tokenPage = null)
    {
        if (withToken)
        {
            form["__RequestVerificationToken"] = AntiforgeryToken().Match((await GetAsync(client, tokenPage ?? path)).Html).Groups[1].Value;
        }

        return await client.PostAsync(path, new FormUrlEncodedContent(form), Token);
    }

    private static async Task<Book> SeedAsync(LivingPaperFactory factory, Action<Book> shape, bool queued = false, string title = "Le Malade imaginaire")
    {
        await factory.Services.GetRequiredService<BookAnalysisWorker>().Recovered; // The restart recovery is over.
        var book = new Book(BookId.New(), title, "malade.epub", DateTimeOffset.UtcNow.AddMinutes(-30)) { UpdatedAt = DateTimeOffset.UtcNow };
        shape(book);
        await factory.Services.GetRequiredService<IBookRepository>().SaveAsync(book, Token);
        await factory.Services.GetRequiredService<IBookTextStore>().SaveAsync(book.Id, "text", Token);
        if (queued)
        {
            factory.Services.GetRequiredService<IAnalysisScheduler>().Enqueue(book.Id);
        }

        return book;
    }

    private static Task<Book> SeedDoneAsync(LivingPaperFactory factory, Action<Book>? more = null) => SeedAsync(factory, book =>
    {
        book.Status = BookStatus.Done;
        book.Summary = "France, 1673. A satirical comedy of a man afraid to die.";
        book.ProviderName = "OpenAI";
        book.Model = "gpt-5-nano";
        book.StartedAt = book.SentAt;
        book.FinishedAt = book.SentAt.AddSeconds(134);
        book.InputTokens = 12345;
        book.CachedInputTokens = 2000;
        book.OutputTokens = 678;
        book.Characters.AddRange(
        [
            new Character(2, "Toinette", "The quick-witted maid.", "I am Toinette.\nI say what I think."),
            new Character(1, "Argan", "The imaginary invalid.", "I am Argan.\n\nI am afraid to die."),
        ]);
        more?.Invoke(book);
    });

    // ---- The page of a book ----

    [Fact]
    public async Task AFinishedBook_ShowsItsSummary_ItsSpoilerWarning_AndItsCharactersMostImportantFirst()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory);

        var (status, raw) = await GetAsync(client, "/Book/" + book.Id);
        var html = Decoded(raw);

        status.ShouldBe(HttpStatusCode.OK);
        Regex.Count(html, @"<h1\b").ShouldBe(1);
        html.ShouldContain("<title>Le Malade imaginaire");
        html.ShouldContain("France, 1673. A satirical comedy of a man afraid to die.");
        html.ShouldContain("Spoilers");
        html.ShouldContain("Characters know the whole book, ending included.");
        html.IndexOf("Argan", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Toinette", StringComparison.Ordinal));
        html.ShouldContain("The imaginary invalid.");
        html.ShouldContain($"href=\"/Book/{book.Id}/Character/1\"");
        html.ShouldContain($"href=\"/Book/{book.Id}/Character/2\"");
        html.ShouldContain("aria-label=\"Read the sheet of Argan\"");
        html.ShouldContain("gpt-5-nano (OpenAI)");
        html.ShouldContain("2 min 14 s");
        html.ShouldContain("12,345 in (2,000 cached), 678 out");
    }

    [Fact]
    public async Task EveryCharacter_HasAChatButton_HiddenWithoutJavaScript()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory);

        var cast = (await GetAsync(client, "/Book/" + book.Id)).Html;
        var sheet = (await GetAsync(client, SheetPath(book))).Html;

        Regex.IsMatch(cast, $@"<a[^>]*class=""button button--small js-only""[^>]*href=""/Book/{book.Id}/Character/1/Chat""").ShouldBeTrue();
        Regex.IsMatch(cast, $@"href=""/Book/{book.Id}/Character/2/Chat""").ShouldBeTrue();
        Regex.IsMatch(sheet, $@"<a[^>]*class=""button js-only""[^>]*href=""/Book/{book.Id}/Character/1/Chat""[^>]*>Chat with Argan</a>").ShouldBeTrue();
        cast.ShouldContain("/js/js-flag.js");
    }

    [Fact]
    public async Task ABookWithSkippedParts_SaysSo()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory, done => done.PartsSkipped = 1);

        Decoded((await GetAsync(client, "/Book/" + book.Id)).Html).ShouldContain("A part of the book was refused by the content filter");
    }

    [Fact]
    public async Task ARunningBook_ShowsItsProgress_AndRefreshesByItself()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedAsync(factory, running => { running.Status = BookStatus.Running; running.PartsTotal = 8; running.PartsDone = 3; }, queued: true);

        var (_, raw) = await GetAsync(client, "/Book/" + book.Id);
        var html = Decoded(raw);
        var (_, fragment) = await GetAsync(client, $"/Book/{book.Id}?handler=Progress");

        html.ShouldContain("Analysis in progress");
        html.ShouldContain("3 of 8 parts read");
        html.ShouldContain("You can leave this page");
        html.ShouldContain($"data-refresh-url=\"/Book/{book.Id}?handler=Progress\"");
        html.ShouldContain("/js/book.js");
        html.ShouldNotContain("The characters");
        fragment.ShouldNotContain("<html");
        fragment.ShouldContain("data-state=\"active\"");
        Regex.IsMatch(fragment, @"<progress[^>]*value=""3""[^>]*max=""8""").ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheAnalysisStops_TheFragmentSaysItIsNoLongerActive_SoTheScriptReloadsThePage()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory);

        var (status, fragment) = await GetAsync(client, $"/Book/{book.Id}?handler=Progress");

        status.ShouldBe(HttpStatusCode.OK);
        fragment.ShouldContain("data-state=\"stopped\"");
    }

    [Fact]
    public async Task TheScriptOfThePageOfABook_IsServed_AndReloadsWhenTheStateChanges()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var script = (await GetAsync(client, "/js/book.js")).Html;

        script.ShouldContain("location.reload()");
        script.ShouldContain("data-state");
    }

    [Fact]
    public async Task AFailedBook_ShowsWhatWentWrong_AndRetryResumesIt()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedAsync(factory, failed => { failed.Status = BookStatus.Failed; failed.ErrorCode = "NoModel"; });
        var path = "/Book/" + book.Id;

        var html = Decoded((await GetAsync(client, path)).Html);
        using var retry = await PostAsync(client, $"{path}?handler=Retry", [], tokenPage: path);
        var after = Decoded((await GetAsync(client, retry.Headers.Location!.OriginalString)).Html);

        html.ShouldContain("Analysis failed");
        html.ShouldContain("No model is chosen to analyze books");
        Regex.IsMatch(html, @"<button[^>]*>Retry</button>").ShouldBeTrue();
        retry.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        retry.Headers.Location!.OriginalString.ShouldBe(path);
        after.ShouldContain("will start again");
        after.ShouldContain("Waiting for analysis");
        factory.Services.GetRequiredService<IAnalysisScheduler>().IsQueued(book.Id).ShouldBeTrue();
    }

    [Fact]
    public async Task RetryOfABookThatIsDone_OrUnknown_IsANotFound()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var done = await SeedDoneAsync(factory);

        using var first = await PostAsync(client, $"/Book/{done.Id}?handler=Retry", [], tokenPage: "/");
        using var second = await PostAsync(client, "/Book/0123456789abcdef0123456789abcdef?handler=Retry", [], tokenPage: "/");

        first.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        second.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/Book/nope")]
    [InlineData("/Book/0123456789abcdef0123456789abcdef")]
    [InlineData("/Book/..%2F..%2Fetc")]
    [InlineData("/Book/0123456789abcdef0123456789abcdef/Character/1")]
    [InlineData("/Book/0123456789abcdef0123456789abcdef?handler=Progress")]
    public async Task ABookThatIsNotThere_IsAFriendly404_NotAnEmptyPage(string path)
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var (status, html) = await GetAsync(client, path);

        status.ShouldBe(HttpStatusCode.NotFound);
        Decoded(html).ShouldContain("Page not found");
        html.ShouldContain("<main id=\"main\"");
        Decoded(html).ShouldContain("Back to the library");
    }

    [Fact]
    public async Task TheNotFoundPage_IsTranslated_AndAnUnknownAddressGetsIt()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var (status, html) = await GetAsync(client, "/this-does-not-exist", "fr");

        status.ShouldBe(HttpStatusCode.NotFound);
        Decoded(html).ShouldContain("Page introuvable");
        html.ShouldContain("<html lang=\"fr\"");
    }

    [Fact]
    public async Task ABookNotFound_OnAPost_AlsoShowsThePage_NotAnAntiforgeryError()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await PostAsync(client, "/?handler=Delete", new Dictionary<string, string> { ["id"] = "0123456789abcdef0123456789abcdef" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Decoded(await response.Content.ReadAsStringAsync(Token)).ShouldContain("Page not found");
    }

    [Fact]
    public async Task TheRunningHead_LinksToTheLibrary_AndMarksItWhenYouAreThere()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var home = (await GetAsync(client, "/")).Html;
        var settings = (await GetAsync(client, "/Settings")).Html;
        var french = Decoded((await GetAsync(client, "/", "fr")).Html);

        Regex.IsMatch(home, @"<a[^>]*href=""/""[^>]*aria-current=""page""[^>]*>Library</a>").ShouldBeTrue();
        Regex.IsMatch(settings, @"<a[^>]*href=""/""[^>]*aria-current=""page""").ShouldBeFalse();
        Regex.IsMatch(settings, @"<a[^>]*href=""/""[^>]*>Library</a>").ShouldBeTrue();
        french.ShouldContain(">Bibliothèque</a>");
    }

    [Fact]
    public async Task ThePageOfABook_IsTranslated_AndTextsAreEncoded()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory, done => done.Characters[0] = new Character(2, "<script>alert(1)</script>", "d", "p"));

        var (_, raw) = await GetAsync(client, "/Book/" + book.Id, "fr");
        var html = Decoded(raw);

        html.ShouldContain("Divulgâcheurs");
        html.ShouldContain("Les personnages");
        html.ShouldContain("Lire la fiche");
        raw.ShouldNotContain("<script>alert(1)</script>");
    }

    // ---- The library ----

    [Fact]
    public async Task TheLibrary_ListsEveryBookWithItsStatus_AndALinkToItsPage()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var done = await SeedDoneAsync(factory);
        var failed = await SeedAsync(factory, book => { book.Status = BookStatus.Failed; book.ErrorCode = "ModelRefused"; }, title: "Failed book");
        var waiting = await SeedAsync(factory, _ => { }, queued: true, title: "Waiting book");

        var html = Decoded((await GetAsync(client, "/")).Html);

        html.ShouldContain("class=\"contents\"");
        foreach (var book in new[] { done, failed, waiting })
        {
            html.ShouldContain($"href=\"/Book/{book.Id}\"");
        }

        html.ShouldContain("Analyzed");
        html.ShouldContain("Analysis failed");
        html.ShouldContain("Waiting for analysis");
        html.ShouldContain("2 characters");
        Regex.Count(html, @"<h1\b").ShouldBe(1);
    }

    [Fact]
    public async Task ARunningBook_HasAProgressBarInTheLibrary()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        await SeedAsync(factory, book => { book.Status = BookStatus.Running; book.PartsTotal = 12; book.PartsDone = 3; }, queued: true);

        var html = (await GetAsync(client, "/")).Html;

        Regex.IsMatch(html, @"<progress[^>]*value=""3""[^>]*max=""12""").ShouldBeTrue();
    }

    // ---- The sheet of a character ----

    private static string SheetPath(Book book, int position = 1) => $"/Book/{book.Id}/Character/{position}";

    [Fact]
    public async Task TheSheet_ShowsTheDescriptionAndTheProfileInParagraphs_FirstWithADropCap()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory);

        var (status, raw) = await GetAsync(client, SheetPath(book));
        var html = Decoded(raw);

        status.ShouldBe(HttpStatusCode.OK);
        Regex.Count(html, @"<h1\b").ShouldBe(1);
        html.ShouldContain("<h1>Argan</h1>");
        html.ShouldContain("The imaginary invalid.");
        Regex.IsMatch(html, @"<p class=""dropcap"">I am Argan\.</p>").ShouldBeTrue();
        html.ShouldContain("<p>I am afraid to die.</p>");
        html.ShouldContain($"href=\"/Book/{book.Id}\"");
        html.ShouldContain("Spoilers");
    }

    [Fact]
    public async Task TheForm_IsFilledWithTheSavedTexts_EveryFieldHasAVisibleLabel_AndTheLimitsAreSaid()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory);

        var html = Decoded((await GetAsync(client, SheetPath(book))).Html);

        foreach (var field in new[] { "character-name", "character-description", "character-profile" })
        {
            html.ShouldContain($"for=\"{field}\"");
            html.ShouldContain($"id=\"{field}\"");
        }

        Regex.IsMatch(html, @"<input[^>]*id=""character-name""[^>]*value=""Argan""").ShouldBeTrue();
        Regex.IsMatch(html, @"<textarea[^>]*id=""character-description""[^>]*>The imaginary invalid\.</textarea>").ShouldBeTrue();
        Regex.IsMatch(html, @"<textarea[^>]*id=""character-profile""[^>]*>I am Argan\.\s*\n\s*I am afraid to die\.</textarea>").ShouldBeTrue();
        html.ShouldContain($"At most {CharacterLimits.Name} characters");
        html.ShouldContain($"at most {CharacterLimits.ShortDescription} characters");
        html.ShouldContain($"At most {CharacterLimits.ChatProfile} characters");
        html.ShouldContain("The chat always uses the texts saved here.");
    }

    [Fact]
    public async Task ChangingASheet_SavesItInTheBookFile_GoesBackWithANotice_AndKeepsTheOthers()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory);

        using var response = await PostAsync(client, SheetPath(book), new Dictionary<string, string>
        {
            ["name"] = "  Argan the anxious ",
            ["shortDescription"] = "A man afraid of dying.",
            ["chatProfile"] = "First paragraph.\r\nSecond paragraph.",
        });
        var html = Decoded((await GetAsync(client, response.Headers.Location!.OriginalString)).Html);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(SheetPath(book));
        html.ShouldContain("The sheet of Argan the anxious was saved.");
        html.ShouldContain("<h1>Argan the anxious</h1>");
        var saved = (await factory.Services.GetRequiredService<BookService>().GetAsync(book.Id, Token))!.Characters.Single(character => character.Position == 1);
        (saved.Name, saved.ShortDescription, saved.ChatProfile).ShouldBe(("Argan the anxious", "A man afraid of dying.", "First paragraph.\nSecond paragraph."));
        (await File.ReadAllTextAsync(Path.Combine(factory.DataDirectory, "books", book.Id + ".json"), Token)).ShouldContain("Argan the anxious");
        (await factory.Services.GetRequiredService<BookService>().GetAsync(book.Id, Token))!.Characters.Single(character => character.Position == 2).Name.ShouldBe("Toinette");
    }

    [Fact]
    public async Task ARefusedSheet_IsShownAgainWithWhatWasTyped_AndTheErrorsBesideTheirFields_NothingIsSaved()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory);

        using var response = await PostAsync(client, SheetPath(book), new Dictionary<string, string>
        {
            ["name"] = "toinette",
            ["shortDescription"] = string.Empty,
            ["chatProfile"] = new string('p', CharacterLimits.ChatProfile + 1),
        });
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Error: another character of this book already has this name.");
        html.ShouldContain("Error: this field is required.");
        html.ShouldContain($"Error: at most {CharacterLimits.ChatProfile} characters.");
        Regex.Count(html, "aria-invalid=\"true\"").ShouldBe(3);
        Regex.IsMatch(html, @"<input[^>]*id=""character-name""[^>]*value=""toinette""").ShouldBeTrue();
        Regex.IsMatch(html, @"<input[^>]*id=""character-name""[^>]*aria-describedby=""[^""]*character-name-error").ShouldBeTrue();
        var stored = (await factory.Services.GetRequiredService<BookService>().GetAsync(book.Id, Token))!.Characters.Single(character => character.Position == 1);
        stored.Name.ShouldBe("Argan");
        stored.ChatProfile.ShouldBe("I am Argan.\n\nI am afraid to die.");
    }

    [Fact]
    public async Task ALineBreakInAName_IsRefused()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory);

        using var response = await PostAsync(client, SheetPath(book), new Dictionary<string, string> { ["name"] = "Ar\ngan", ["shortDescription"] = "d", ["chatProfile"] = "p" });

        Decoded(await response.Content.ReadAsStringAsync(Token)).ShouldContain("Error: this text holds a character that is not allowed here");
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryToken_IsRefused_AndChangesNothing()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory);

        using var response = await PostAsync(client, SheetPath(book), new Dictionary<string, string> { ["name"] = "Hacked", ["shortDescription"] = "d", ["chatProfile"] = "p" }, withToken: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.Services.GetRequiredService<BookService>().GetAsync(book.Id, Token))!.Characters.Single(character => character.Position == 1).Name.ShouldBe("Argan");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public async Task ACharacterThatIsNotThere_IsA404_OnReadingAndOnSaving(int position)
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory);

        var (status, _) = await GetAsync(client, SheetPath(book, position));
        using var response = await PostAsync(client, SheetPath(book, position), new Dictionary<string, string> { ["name"] = "A", ["shortDescription"] = "d", ["chatProfile"] = "p" }, tokenPage: SheetPath(book));

        status.ShouldBe(HttpStatusCode.NotFound);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ASheetOfABookThatIsNotDone_IsA404()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedAsync(factory, running => { running.Status = BookStatus.Running; running.Characters.Add(new Character(1, "Argan", "d", "p")); }, queued: true);

        (await GetAsync(client, SheetPath(book))).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheSheet_IsTranslated_AndItsErrorsToo()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);
        var book = await SeedDoneAsync(factory);

        var (_, page) = await GetAsync(client, SheetPath(book), "fr");
        using var refused = await PostAsync(client, SheetPath(book), new Dictionary<string, string> { ["name"] = string.Empty, ["shortDescription"] = "d", ["chatProfile"] = "p" });

        var html = Decoded(page);
        html.ShouldContain("Fiche de personnalité");
        html.ShouldContain("Modifier cette fiche");
        html.ShouldContain("Enregistrer la fiche");
        html.ShouldNotContain("Edit this sheet");
        Decoded(await refused.Content.ReadAsStringAsync(Token)).ShouldContain("Error: this field is required."); // This visitor chose no language: English.
    }

    // ---- The texts ----

    private static IReadOnlyDictionary<string, string> Texts(string language) =>
        LivingPaper.Web.Localization.TranslationFile.Parse(
            File.ReadAllText(Path.Combine(RepositoryFiles.Find(Path.Combine("src", "LivingPaper.Web")), "Resources", language + ".json")),
            language + ".json");

    public static TheoryData<string> LanguageCodes() => ["en", "fr"];

    // An error code of a sheet added later without its text fails here, in every language.
    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public void EveryErrorCodeOfASheet_HasItsText(string language)
    {
        var codes = typeof(CharacterErrors).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!).ToList();
        var texts = Texts(language);

        codes.ShouldNotBeEmpty();
        foreach (var code in codes)
        {
            texts.Keys.ShouldContain("character.error." + code, $"{language}.json lacks the text of the error code '{code}'");
        }
    }
}
