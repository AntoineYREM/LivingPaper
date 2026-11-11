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

// The examples (PAPER-11): books already analyzed that come with the application. Read only, readable and chattable like any
// book, and copied into the library to become one's own.
public partial class ExamplesTests
{
    private const string Key = "sk-examples-secret-key-4321";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    // Plays the provider for the chat and for the connection test.
    private sealed class PongChatFactory : IChatModelFactory
    {
        public List<IReadOnlyList<ChatMessage>> Sent { get; } = [];

        public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true) => new Model(this);

        private sealed class Model(PongChatFactory owner) : IChatModel
        {
            public async IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                owner.Sent.Add(messages);
                await Task.Yield();
                yield return "Bonjour, ";
                yield return "mon ami.";
            }

            public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) => Task.FromResult("pong");
        }
    }

    private static Book Example(string title = "Alice in Wonderland", BookId? id = null)
    {
        var book = new Book(id ?? BookId.New(), title, "alice.epub", DateTimeOffset.UtcNow.AddYears(-1))
        {
            Status = BookStatus.Done,
            Summary = "England, 1865. A dream.",
            ProviderName = "OpenAI",
            Model = "gpt-5-nano",
            PartsDone = 2,
            PartsTotal = 2,
            ProfilesDone = 2,
            ProfilesTotal = 2,
        };
        book.Characters.AddRange([new Character(1, "Alice", "A curious girl.", "I am Alice.\nI follow a rabbit."), new Character(2, "The Queen", "A furious queen.", "I am the Queen.")]);
        return book;
    }

    // Puts an example file into the directory of a factory (before the application starts, which reads it).
    private static async Task PutExampleAsync(LivingPaperFactory factory, Book book, string fileName)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "livingpaper-example-" + Guid.NewGuid().ToString("N"));
        await new JsonBookRepository(scratch).SaveAsync(book, Token);
        File.Move(Path.Combine(scratch, book.Id + ".json"), Path.Combine(factory.ExamplesDirectory, fileName));
        Directory.Delete(scratch, recursive: true);
    }

    private static LivingPaperFactory NewFactory(PongChatFactory? chat = null) =>
        LivingPaperFactory.With(configureServices: services =>
        {
            services.AddSingleton<IChatModelFactory>(chat ?? new PongChatFactory());
            services.AddSingleton<IAnalysisScheduler>(new RecordingScheduler());
        });

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

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> form, string? tokenPage = null)
    {
        form["__RequestVerificationToken"] = AntiforgeryToken().Match((await GetAsync(client, tokenPage ?? "/Examples")).Html).Groups[1].Value;
        return await client.PostAsync(path, new FormUrlEncodedContent(form), Token);
    }

    // ---- The page of the examples ----

    [Fact]
    public async Task TheExamplesPage_ListsTheExamples_ByTitle_ReadOnly_WithALinkToEachAndAnAddButton()
    {
        using var factory = NewFactory();
        var zola = Example("Zola");
        var alice = Example("Alice in Wonderland");
        await PutExampleAsync(factory, zola, "zola.json");
        await PutExampleAsync(factory, alice, "alice.json");
        using var client = NewClient(factory);

        var (status, raw) = await GetAsync(client, "/Examples");
        var html = Decoded(raw);

        status.ShouldBe(HttpStatusCode.OK);
        Regex.Count(html, @"<h1\b").ShouldBe(1);
        html.IndexOf("Alice in Wonderland", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Zola", StringComparison.Ordinal));
        html.ShouldContain($"href=\"/Book/{alice.Id}\"");
        html.ShouldContain("England, 1865. A dream.");
        html.ShouldContain("2 characters");
        html.ShouldContain("aria-label=\"Add Alice in Wonderland to my library\"");
        html.ShouldNotContain("Delete");
        html.ShouldNotContain("Retry");
        raw.ShouldContain("__RequestVerificationToken");
    }

    [Fact]
    public async Task WithNoExample_ThePageSaysSo()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        Decoded((await GetAsync(client, "/Examples")).Html).ShouldContain("No example comes with this installation.");
    }

    [Fact]
    public async Task TheExamples_AreNotInTheLibrary()
    {
        using var factory = NewFactory();
        await PutExampleAsync(factory, Example(), "alice.json");
        using var client = NewClient(factory);

        var html = Decoded((await GetAsync(client, "/")).Html);

        html.ShouldNotContain("Alice in Wonderland");
        html.ShouldContain("Your library is empty");
    }

    [Fact]
    public async Task TheRunningHead_LinksToTheExamples_InBothLanguages_AndMarksThePage()
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        var examples = (await GetAsync(client, "/Examples")).Html;
        var home = Decoded((await GetAsync(client, "/", "fr")).Html);

        Regex.IsMatch(examples, @"<a[^>]*href=""/Examples""[^>]*aria-current=""page""[^>]*>Examples</a>").ShouldBeTrue();
        home.ShouldContain(">Exemples</a>");
    }

    [Fact]
    public async Task ThePage_IsTranslated()
    {
        using var factory = NewFactory();
        await PutExampleAsync(factory, Example(), "alice.json");
        using var client = NewClient(factory);

        var html = Decoded((await GetAsync(client, "/Examples", "fr")).Html);

        html.ShouldContain("Des livres du domaine public déjà analysés");
        html.ShouldContain("Ajouter à ma bibliothèque");
        html.ShouldNotContain("Add to my library");
    }

    // ---- Validation at startup ----

    [Fact]
    public async Task AnInvalidExample_IsIgnoredWithAWarning_TheOthersStay_AndNothingOfItsContentIsLogged()
    {
        using var factory = NewFactory();
        await PutExampleAsync(factory, Example(), "alice.json");
        await File.WriteAllTextAsync(Path.Combine(factory.ExamplesDirectory, "broken.json"), "{ SECRET-LOOKING TEXT, not json", Token);
        using var client = NewClient(factory);

        var html = Decoded((await GetAsync(client, "/Examples")).Html);

        html.ShouldContain("Alice in Wonderland");
        factory.Logs.Entries.ShouldContain(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning && entry.Message.Contains("broken.json", StringComparison.Ordinal));
        factory.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains("SECRET-LOOKING", StringComparison.Ordinal));
        factory.Logs.Entries.ShouldContain(entry => entry.Message.Contains("1 example(s) loaded", StringComparison.Ordinal));
    }

    // ---- Read only ----

    [Fact]
    public async Task AnExample_OpensOnThePageOfABook_ReadOnly_WithItsCharacters()
    {
        using var factory = NewFactory();
        var alice = Example();
        await PutExampleAsync(factory, alice, "alice.json");
        using var client = NewClient(factory);

        var (status, raw) = await GetAsync(client, "/Book/" + alice.Id);
        var html = Decoded(raw);

        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<h1>Alice in Wonderland</h1>");
        html.ShouldContain("This book is an example, and is read only.");
        html.ShouldContain("Add to my library");
        html.ShouldContain("href=\"/Examples\"");
        html.ShouldContain("Back to the examples");
        html.ShouldContain($"href=\"/Book/{alice.Id}/Character/1\"");
        html.ShouldNotContain("Retry");
        html.ShouldNotContain("Delete");
    }

    [Fact]
    public async Task TheSheetOfAnExample_CanBeRead_ButHasNoEditForm_AndASaveIsA404()
    {
        using var factory = NewFactory();
        var alice = Example();
        await PutExampleAsync(factory, alice, "alice.json");
        using var client = NewClient(factory);
        var path = $"/Book/{alice.Id}/Character/1";

        var (status, raw) = await GetAsync(client, path);
        var html = Decoded(raw);
        using var save = await PostFormAsync(client, path, new Dictionary<string, string> { ["name"] = "Hacked", ["shortDescription"] = "d", ["chatProfile"] = "p" }, tokenPage: path);

        status.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("I am Alice.");
        html.ShouldContain("This sheet belongs to an example");
        html.ShouldNotContain("id=\"character-name\"");
        html.ShouldNotContain("Save the sheet");
        save.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(client, path)).Html.ShouldNotContain("Hacked");
    }

    [Fact]
    public async Task AnExample_CannotBeDeletedOrRetried_ThroughTheLibraryEndpoints()
    {
        using var factory = NewFactory();
        var alice = Example();
        await PutExampleAsync(factory, alice, "alice.json");
        using var client = NewClient(factory);

        using var delete = await PostFormAsync(client, "/?handler=Delete", new Dictionary<string, string> { ["id"] = alice.Id.ToString() });
        using var retry = await PostFormAsync(client, $"/Book/{alice.Id}?handler=Retry", []);

        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        retry.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(client, "/Examples")).Html.ShouldContain("Alice in Wonderland");
    }

    [Fact]
    public async Task YouCanChatWithTheCharactersOfAnExample()
    {
        var chat = new PongChatFactory();
        using var factory = NewFactory(chat);
        var alice = Example();
        await PutExampleAsync(factory, alice, "alice.json");
        (await factory.Services.GetRequiredService<LlmSettingsService>().AddAsync(new LlmProviderDraft("OpenAI", "https://api.openai.com/v1", Key, "gpt-5-nano"), Token)).Succeeded.ShouldBeTrue();
        using var client = NewClient(factory);
        var path = $"/Book/{alice.Id}/Character/1/Chat";

        var (status, page) = await GetAsync(client, path);
        using var request = new HttpRequestMessage(HttpMethod.Post, path + "?handler=Message") { Content = new StringContent("""{"messages":[{"role":"user","content":"Hello"}]}""", Encoding.UTF8, "application/json") };
        request.Headers.Add("RequestVerificationToken", AntiforgeryToken().Match(page).Groups[1].Value);
        using var response = await client.SendAsync(request, Token);
        var body = await response.Content.ReadAsStringAsync(Token);

        status.ShouldBe(HttpStatusCode.OK);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("type").GetString()).ShouldBe(["text", "text", "done"]);
        chat.Sent.Single()[0].Content.ShouldContain("You play Alice");
        chat.Sent.Single()[0].Content.ShouldContain("I follow a rabbit.");
    }

    // ---- Adding to the library ----

    [Fact]
    public async Task AddingAnExample_CopiesItIntoTheLibrary_AndOpensTheCopyWithANotice_WhoseSheetsCanBeEdited()
    {
        using var factory = NewFactory();
        var alice = Example();
        await PutExampleAsync(factory, alice, "alice.json");
        using var client = NewClient(factory);

        using var response = await PostFormAsync(client, "/Examples?handler=Add", new Dictionary<string, string> { ["id"] = alice.Id.ToString() });
        var location = response.Headers.Location!.OriginalString;
        var page = Decoded((await GetAsync(client, location)).Html);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var copy = (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.Single();
        copy.Id.ShouldNotBe(alice.Id);
        location.ShouldBe("/Book/" + copy.Id);
        (copy.Title, copy.Status, copy.Characters.Count).ShouldBe(("Alice in Wonderland", BookStatus.Done, 2));
        page.ShouldContain("Alice in Wonderland was added to your library.");
        page.ShouldNotContain("This book is an example");
        (await GetAsync(client, "/")).Html.ShouldContain($"href=\"/Book/{copy.Id}\"");

        // The copy is the user's: its sheets can be edited, and the example stays as it was.
        var sheet = $"/Book/{copy.Id}/Character/1";
        using var edit = await PostFormAsync(client, sheet, new Dictionary<string, string> { ["name"] = "Alice, edited", ["shortDescription"] = "Changed.", ["chatProfile"] = "I am another Alice." }, tokenPage: sheet);
        edit.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Decoded((await GetAsync(client, $"/Book/{alice.Id}/Character/1")).Html).ShouldContain("<h1>Alice</h1>");
    }

    [Fact]
    public async Task AddingAnExampleWhoseTitleIsAlreadyThere_IsRefusedWithAMessage_AndNothingIsReplaced()
    {
        using var factory = NewFactory();
        var alice = Example();
        await PutExampleAsync(factory, alice, "alice.json");
        using var client = NewClient(factory);
        (await PostFormAsync(client, "/Examples?handler=Add", new Dictionary<string, string> { ["id"] = alice.Id.ToString() })).Dispose();

        using var again = await PostFormAsync(client, "/Examples?handler=Add", new Dictionary<string, string> { ["id"] = alice.Id.ToString() });
        var html = Decoded(await again.Content.ReadAsStringAsync(Token));

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("A book titled Alice in Wonderland is already in your library.");
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public async Task AddingSomethingThatIsNotAnExample_IsA404(string id)
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await PostFormAsync(client, "/Examples?handler=Add", new Dictionary<string, string> { ["id"] = id });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryToken_IsRefused()
    {
        using var factory = NewFactory();
        var alice = Example();
        await PutExampleAsync(factory, alice, "alice.json");
        using var client = NewClient(factory);

        using var response = await client.PostAsync("/Examples?handler=Add", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = alice.Id.ToString() }), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    // ---- The examples that come with the repository ----

    [Fact]
    public async Task TheExamplesOfTheRepository_AreAllValid_AndFollowTheRulesOfAnExample()
    {
        var directory = RepositoryFiles.Find("examples");

        var listing = await new JsonExampleCatalog(directory).ListAsync(Token);

        listing.Unreadable.ShouldBeEmpty("an example of the repository does not follow the rules: see CONTRIBUTING.md");
        listing.Books.ShouldNotBeEmpty("the repository ships at least one example");
        listing.Books.Select(book => book.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(listing.Books.Count);
        foreach (var book in listing.Books)
        {
            book.Summary.ShouldNotBeNullOrWhiteSpace(book.Title);
            book.Characters.Count.ShouldBeGreaterThanOrEqualTo(1, book.Title);
            book.Characters.ShouldAllBe(character => character.ChatProfile.Length > 0, book.Title);
            book.FindLimitViolations().ShouldBeEmpty(book.Title);
        }
    }

    [Fact]
    public void TheExamplesFolder_IsCopiedWithTheApplication_SoTheDockerImageHasThem()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryFiles.Find(Path.Combine("src", "LivingPaper.Web")), "LivingPaper.Web.csproj"));

        project.ShouldContain("examples\\*.json");
        project.ShouldContain("CopyToPublishDirectory");
        project.ShouldContain("CopyToOutputDirectory");
    }

    // ---- The texts ----

    [Fact]
    public async Task AnExample_IsTranslatedToo()
    {
        using var factory = NewFactory();
        var alice = Example();
        await PutExampleAsync(factory, alice, "alice.json");
        using var client = NewClient(factory);

        var html = Decoded((await GetAsync(client, "/Book/" + alice.Id, "fr")).Html);

        html.ShouldContain("Ce livre est un exemple, en lecture seule.");
        html.ShouldContain("Retour aux exemples");
    }
}
