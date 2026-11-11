using System.IO.Compression;
using System.Text;
using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The analysis running in the background of the real application (PAPER-8), with a language model that never leaves the
// process: from a book sent to its characters, what a restart does, a book deleted while it is analyzed, and a retry.
public class AnalysisWorkerTests
{
    private const string Key = "sk-analysis-secret-key-13579";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Plays the provider with the real default prompts: it recognises the step by a phrase of its prompt.
    private sealed class FakeAnalysisModelFactory : IChatModelFactory
    {
        public Func<string, string, Task<string>> Handler { get; set; } = (system, _) => Task.FromResult(Reply(system));

        public int Calls;

        public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true) => new Model(this);

        public static string Reply(string system) =>
            system.Contains("You are reading an excerpt", StringComparison.Ordinal)
                ? """{"characters":[{"name":"Argan","otherNames":[],"role":"A hypochondriac.","personality":"Anxious.","speech":"Whining.","events":"Wants a doctor son-in-law.","importance":9},{"name":"Toinette","otherNames":["Toinon"],"role":"The maid.","personality":"Sharp.","speech":"Cheeky.","events":"Disguises herself.","importance":8}]}"""
                : system.Contains("You receive the notes taken on each part", StringComparison.Ordinal) && system.Contains("most important characters", StringComparison.Ordinal)
                    ? """{"characters":[{"name":"Argan","shortDescription":"The imaginary invalid."},{"name":"Toinette","shortDescription":"The sharp maid."}]}"""
                    : system.Contains("Write the chat profile", StringComparison.Ordinal)
                        ? "I am a character of the book. My long profile."
                        : system.Contains("atmosphere of the book", StringComparison.Ordinal)
                            ? "France, 1673. A satirical comedy."
                            : "pong";

        private sealed class Model(FakeAnalysisModelFactory owner) : IChatModel
        {
            public IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref owner.Calls);
                usage?.Add(100, 0, 50);
                // The connection test sends no system message, only the user one.
                return owner.Handler(messages.FirstOrDefault(message => message.Role == ChatRole.System)?.Content ?? string.Empty, messages.Single(message => message.Role == ChatRole.User).Content);
            }
        }
    }

    private static LivingPaperFactory NewFactory(FakeAnalysisModelFactory model, string? dataDirectory = null, bool realWorker = true) =>
        LivingPaperFactory.With(dataDirectory, services =>
        {
            services.AddSingleton<IChatModelFactory>(model);
            if (!realWorker)
            {
                services.AddSingleton<IAnalysisScheduler>(new RecordingScheduler());
            }
        });

    private static byte[] Epub(string title = "Le Malade imaginaire")
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "META-INF/container.xml", """<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="content.opf"/></rootfiles></container>""");
            Add(zip, "content.opf", $"""<package xmlns="http://www.idpf.org/2007/opf"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>{title}</dc:title></metadata><manifest><item id="a" href="a.xhtml"/></manifest><spine><itemref idref="a"/></spine></package>""");
            Add(zip, "a.xhtml", "<html><body><p>Argan est malade et Toinette se moque de lui.</p></body></html>");
        }

        return memory.ToArray();
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static async Task AddModelAsync(LivingPaperFactory factory)
    {
        var result = await factory.Services.GetRequiredService<LlmSettingsService>().AddAsync(new LlmProviderDraft("OpenAI", "https://api.openai.com/v1", Key, "gpt-5-nano"), Token);
        result.Succeeded.ShouldBeTrue();
    }

    private static async Task<Book> SendBookAsync(LivingPaperFactory factory, string title = "Le Malade imaginaire")
    {
        using var stream = new MemoryStream(Epub(title));
        var result = await factory.Services.GetRequiredService<BookService>().AddAsync(stream, "malade.epub", null, replace: false, Token);
        result.Succeeded.ShouldBeTrue();
        return result.Added!;
    }

    private static async Task<Book> WaitForAsync(LivingPaperFactory factory, BookId id, Func<Book, bool> condition)
    {
        var service = factory.Services.GetRequiredService<BookService>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var last = "no such book";
        while (DateTime.UtcNow < deadline)
        {
            var book = (await service.ListAsync(Token)).Books.FirstOrDefault(candidate => candidate.Id == id);
            if (book is not null && condition(book))
            {
                return book;
            }

            last = book is null ? "no such book" : $"{book.Status} {book.ErrorCode} {book.ErrorDetail} parts {book.PartsDone}/{book.PartsTotal}";

            await Task.Delay(50, Token);
        }

        throw new TimeoutException($"The book did not reach the expected state in 20 seconds. Last seen: {last}.");
    }

    private static string Books(LivingPaperFactory factory, params string[] path) => Path.Combine([factory.DataDirectory, "books", .. path]);

    // ---- From a book sent to its characters ----

    [Fact]
    public async Task ABookSent_IsAnalyzedInTheBackground_UntilItsCharactersAreThere()
    {
        var model = new FakeAnalysisModelFactory();
        using var factory = NewFactory(model);
        await AddModelAsync(factory);

        var sent = await SendBookAsync(factory);
        var book = await WaitForAsync(factory, sent.Id, candidate => candidate.Status == BookStatus.Done);

        book.Characters.Select(character => character.Name).ShouldBe(["Argan", "Toinette"]);
        book.Characters[0].ChatProfile.ShouldBe("I am a character of the book. My long profile.");
        book.Summary.ShouldBe("France, 1673. A satirical comedy.");
        book.ProviderName.ShouldBe("OpenAI");
        book.Model.ShouldBe("gpt-5-nano");
        book.PartsDone.ShouldBe(1);
        book.ProfilesDone.ShouldBe(2);
        book.InputTokens.ShouldBe(100 * 5); // One part, the merge, two profiles and the summary.
        File.Exists(Path.Combine(factory.DataDirectory, "uploads", sent.Id + ".txt")).ShouldBeFalse("the text is deleted after the analysis");
        File.Exists(Books(factory, sent.Id + ".steps.json")).ShouldBeFalse();
        (await File.ReadAllTextAsync(Books(factory, sent.Id + ".json"), Token)).ShouldNotContain("Argan est malade");
        (await File.ReadAllTextAsync(Books(factory, sent.Id + ".json"), Token)).ShouldNotContain(Key);
    }

    [Fact]
    public async Task WithNoModelChosen_TheBookFails_AndSaysWhichModelIsMissing()
    {
        using var factory = NewFactory(new FakeAnalysisModelFactory());

        var sent = await SendBookAsync(factory);
        var book = await WaitForAsync(factory, sent.Id, candidate => candidate.Status == BookStatus.Failed);

        book.ErrorCode.ShouldBe("NoModel");
    }

    [Fact]
    public async Task Books_AreAnalyzedOneAtATime()
    {
        var model = new FakeAnalysisModelFactory();
        var running = 0;
        var maxRunning = 0;
        model.Handler = async (system, _) =>
        {
            if (system.Contains("You are reading an excerpt", StringComparison.Ordinal))
            {
                var now = Interlocked.Increment(ref running);
                int seen;
                while (now > (seen = Volatile.Read(ref maxRunning)) && Interlocked.CompareExchange(ref maxRunning, now, seen) != seen)
                {
                }

                await Task.Delay(80, Token);
                Interlocked.Decrement(ref running);
            }

            return FakeAnalysisModelFactory.Reply(system);
        };
        using var factory = NewFactory(model);
        await AddModelAsync(factory);

        var first = await SendBookAsync(factory, "First");
        var second = await SendBookAsync(factory, "Second");
        await WaitForAsync(factory, first.Id, candidate => candidate.Status == BookStatus.Done);
        await WaitForAsync(factory, second.Id, candidate => candidate.Status == BookStatus.Done);

        maxRunning.ShouldBe(1);
    }

    [Fact]
    public async Task TheKey_NeverReachesTheLogsOfTheAnalysis()
    {
        var model = new FakeAnalysisModelFactory();
        using var factory = NewFactory(model);
        await AddModelAsync(factory);
        // The connection test passed; now the model refuses, and its message repeats the key.
        model.Handler = (_, _) => throw new ChatProviderException(ChatFailureKind.Http, "refused " + Key, 401);
        var sent = await SendBookAsync(factory);

        var book = await WaitForAsync(factory, sent.Id, candidate => candidate.Status == BookStatus.Failed);

        book.ErrorCode.ShouldBe("ModelRefused");
        factory.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains(Key, StringComparison.Ordinal) && !entry.Message.Contains("Argan est malade", StringComparison.Ordinal));
    }

    // ---- Retry ----

    [Fact]
    public async Task AFailedAnalysis_CanBeRetried_AndResumesWithoutPayingAgainForTheStepsDone()
    {
        var model = new FakeAnalysisModelFactory();
        using var factory = NewFactory(model);
        await AddModelAsync(factory);
        var failMerge = true;
        model.Handler = (system, _) =>
            failMerge && system.Contains("most important characters", StringComparison.Ordinal)
                ? throw new ChatProviderException(ChatFailureKind.Http, "busy", 503)
                : Task.FromResult(FakeAnalysisModelFactory.Reply(system));
        var sent = await SendBookAsync(factory);
        var failed = await WaitForAsync(factory, sent.Id, candidate => candidate.Status == BookStatus.Failed);
        failed.ErrorCode.ShouldBe("ModelUnreachable");
        failed.PartsDone.ShouldBe(1);

        failMerge = false;
        var service = factory.Services.GetRequiredService<BookService>();
        (await service.RetryAsync(sent.Id, Token)).ShouldBeTrue();
        var done = await WaitForAsync(factory, sent.Id, candidate => candidate.Status == BookStatus.Done);

        done.Characters.Count.ShouldBe(2);
        done.ErrorCode.ShouldBeNull();
        model.Calls.ShouldBe(1 + 1 + 2 + 2 + 1); // The connection test, the part once, the merge twice, two profiles and the summary.
    }

    // ---- A restart ----

    [Fact]
    public async Task AfterARestart_ABookThatWasWaiting_IsAnalyzedWithoutAnyAction()
    {
        var model = new FakeAnalysisModelFactory();
        using var first = NewFactory(model, realWorker: false);
        await AddModelAsync(first);
        var sent = await SendBookAsync(first); // Waiting: nothing runs in this one.

        using var second = NewFactory(model, first.DataDirectory);
        var book = await WaitForAsync(second, sent.Id, candidate => candidate.Status == BookStatus.Done);

        book.Characters.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AfterARestart_ABookThatWasRunning_IsInterrupted_NotAnalyzedAgainBehindTheBack_AndCanBeRetried()
    {
        var model = new FakeAnalysisModelFactory();
        using var first = NewFactory(model, realWorker: false);
        await AddModelAsync(first);
        var sent = await SendBookAsync(first);
        var stored = (await first.Services.GetRequiredService<IBookRepository>().GetAsync(sent.Id, Token))!;
        stored.Status = BookStatus.Running; // The application stopped in the middle of the analysis.
        await first.Services.GetRequiredService<IBookRepository>().SaveAsync(stored, Token);

        using var second = NewFactory(model, first.DataDirectory);
        var interrupted = await WaitForAsync(second, sent.Id, candidate => candidate.Status == BookStatus.Failed);

        interrupted.ErrorCode.ShouldBe("Interrupted");
        model.Calls.ShouldBe(1); // Only the connection test.
        (await second.Services.GetRequiredService<BookService>().RetryAsync(sent.Id, Token)).ShouldBeTrue();
        await WaitForAsync(second, sent.Id, candidate => candidate.Status == BookStatus.Done);
    }

    // ---- Deleting while it runs ----

    [Fact]
    public async Task DeletingABookWhileItIsAnalyzed_StopsTheAnalysis_AndNothingIsWrittenAfterwards()
    {
        var model = new FakeAnalysisModelFactory();
        using var factory = NewFactory(model);
        await AddModelAsync(factory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.Handler = async (system, _) =>
        {
            if (system.Contains("You are reading an excerpt", StringComparison.Ordinal))
            {
                entered.TrySetResult();
                await release.Task; // Held until the book is deleted.
            }

            return FakeAnalysisModelFactory.Reply(system);
        };
        var sent = await SendBookAsync(factory);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);

        var delete = factory.Services.GetRequiredService<BookService>().DeleteAsync(sent.Id, Token);
        await Task.Delay(100, Token);
        release.SetResult();
        (await delete).ShouldBeTrue();
        await Task.Delay(300, Token); // Time for a late write, if there was one.

        File.Exists(Books(factory, sent.Id + ".json")).ShouldBeFalse();
        File.Exists(Books(factory, sent.Id + ".steps.json")).ShouldBeFalse();
        File.Exists(Path.Combine(factory.DataDirectory, "uploads", sent.Id + ".txt")).ShouldBeFalse();
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    // ---- The settings of the analysis ----

    private static IConfiguration Config(params (string, string?)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Item1, value.Item2))).Build();

    [Fact]
    public void WithNothingSet_TheDefaultsAreUsed()
    {
        var options = AnalysisSettings.Read(Config());

        options.PagesPerPart.ShouldBe(40);
        options.MaxParallelCalls.ShouldBe(3);
    }

    [Fact]
    public void TheTwoVariables_AreRead()
    {
        var options = AnalysisSettings.Read(Config(("ANALYSIS_PAGES_PER_PART", "15"), ("ANALYSIS_PARALLEL_CALLS", " 1 ")));

        options.PagesPerPart.ShouldBe(15);
        options.MaxParallelCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData("ANALYSIS_PAGES_PER_PART", "abc")]
    [InlineData("ANALYSIS_PAGES_PER_PART", "0")]
    [InlineData("ANALYSIS_PAGES_PER_PART", "-5")]
    [InlineData("ANALYSIS_PAGES_PER_PART", "1001")]
    [InlineData("ANALYSIS_PARALLEL_CALLS", "1.5")]
    [InlineData("ANALYSIS_PARALLEL_CALLS", "21")]
    [InlineData("ANALYSIS_PARALLEL_CALLS", "0")]
    public void AValueThatIsNotUsable_StopsTheApplication_NamingTheVariable(string name, string value) =>
        Should.Throw<InvalidOperationException>(() => AnalysisSettings.Read(Config((name, value)))).Message.ShouldContain(name);
}
