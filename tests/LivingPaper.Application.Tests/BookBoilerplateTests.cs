using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

// The pages that Project Gutenberg wraps around a book (header, license, donations) are not the book: they are left out of what the
// analysis reads, so they never become scenes of its story.
public sealed class BookBoilerplateTests
{
    private const string Header = "The Project Gutenberg eBook of Alice's Adventures in Wonderland\nThis eBook is for the use of anyone anywhere.";
    private const string Start = "*** START OF THE PROJECT GUTENBERG EBOOK ALICE'S ADVENTURES IN WONDERLAND ***";
    private const string End = "*** END OF THE PROJECT GUTENBERG EBOOK ALICE'S ADVENTURES IN WONDERLAND ***";
    private const string Book = "Alice was beginning to get very tired.\nShe followed the White Rabbit.";
    private const string License = "Project Gutenberg Literary Archive Foundation. Donations are accepted.";

    [Fact]
    public void TheHeaderAndTheLicense_AreLeftOut_TheBookStays()
    {
        var text = $"{Header}\n{Start}\n{Book}\n{End}\n{License}";

        BookBoilerplate.Strip(text).Trim().ShouldBe(Book);
    }

    [Theory]
    [InlineData("*** START OF THIS PROJECT GUTENBERG EBOOK ALICE ***", "*** END OF THIS PROJECT GUTENBERG EBOOK ALICE ***")]
    [InlineData("***START OF THE PROJECT GUTENBERG EBOOK ALICE***", "***END OF THE PROJECT GUTENBERG EBOOK ALICE***")]
    [InlineData("*** start of the project gutenberg ebook alice ***", "*** end of the project gutenberg ebook alice ***")]
    public void TheMarkers_AreRecognizedWhateverTheirWordingOrCase(string start, string end)
    {
        BookBoilerplate.Strip($"{Header}\n{start}\n{Book}\n{end}\n{License}").Trim().ShouldBe(Book);
    }

    [Fact]
    public void WhenOnlyTheEndIsMarked_WhatFollowsIsLeftOut()
    {
        BookBoilerplate.Strip($"{Book}\n{End}\n{License}").Trim().ShouldBe(Book);
    }

    [Fact]
    public void WhenOnlyTheStartIsMarked_WhatPrecedesIsLeftOut()
    {
        BookBoilerplate.Strip($"{Header}\n{Start}\n{Book}").Trim().ShouldBe(Book);
    }

    [Fact]
    public void ATextWithoutMarkers_IsUntouched()
    {
        BookBoilerplate.Strip(Book).ShouldBe(Book);
        BookBoilerplate.Strip(string.Empty).ShouldBe(string.Empty);
    }

    [Fact]
    public void MarkersThatLeaveNothing_AreNotTrusted_TheWholeTextIsKept()
    {
        var text = $"{Start}\n{End}";

        BookBoilerplate.Strip(text).ShouldBe(text);
    }

    [Fact]
    public void AnEndMarkerBeforeTheStart_IsNotTheEndOfTheBook()
    {
        // The end is looked for after the start: a mention of the end in the header does not empty the book.
        BookBoilerplate.Strip($"Header says: {End}\n{Start}\n{Book}\n{End}\n{License}").Trim().ShouldBe(Book);
    }

    [Fact]
    public void ThePagesOfAPdf_KeepTheirFormFeeds()
    {
        BookBoilerplate.Strip($"{Start}\nPage one.\fPage two.\n{End}").ShouldBe("\nPage one.\fPage two.\n");
    }
}

// What the analysis sends to the model: nothing of the pages around the book.
public sealed class BoilerplateInTheAnalysisTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 20, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBookRepository _books = new();
    private readonly InMemoryBookTextStore _texts = new();
    private readonly InMemoryAnalysisStepStore _steps = new();
    private readonly InMemoryLlmSettingsRepository _settingsRepository = new();
    private readonly ScriptedAnalysisModelFactory _model = new();
    private readonly LlmSettingsService _settings;

    public BoilerplateInTheAnalysisTests()
    {
        var provider = new LlmProvider("p1", "OpenAI", "https://api.openai.com/v1", "sk-secret-key-123", "gpt-5-nano");
        _settingsRepository.Seed(LlmSettings.Restore([provider], "p1", "p1"));
        _settings = new LlmSettingsService(_settingsRepository, new FakeConnectionTester(), new FakeModelCatalog(), new PendingProviderStore(TimeProvider.System));
    }

    public void Dispose() => _settings.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheLicenseOfProjectGutenberg_IsNeverSentToTheModel()
    {
        var book = new Book(BookId.New(), "Alice", "alice.epub", Start);
        _books.Books[book.Id] = book;
        await _texts.SaveAsync(book.Id, "The Project Gutenberg eBook of Alice\n*** START OF THE PROJECT GUTENBERG EBOOK ALICE ***\nAlice follows a rabbit.\n*** END OF THE PROJECT GUTENBERG EBOOK ALICE ***\nDonations are welcome. Literary Archive Foundation.", Token);
        var analyzer = new BookAnalyzer(_books, _texts, _steps, _settings, _model, new KeyedPromptProvider(), new ManualTimeProvider(Start), new AnalysisOptions { HeartbeatEvery = TimeSpan.FromHours(1) });

        await analyzer.AnalyzeAsync(book.Id, Token);

        book.Status.ShouldBe(BookStatus.Done);
        var parts = _model.Calls.Where(call => call.IsPart).ToList();
        parts.ShouldHaveSingleItem().User.Trim().ShouldBe("Alice follows a rabbit.");
        _model.Calls.ShouldAllBe(call => !call.User.Contains("Donations", StringComparison.Ordinal) && !call.User.Contains("Literary Archive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABookWithNothingButTheWrapping_IsStillReadAsItIs_NeverRefusedForAnEmptyText()
    {
        var book = new Book(BookId.New(), "Odd", "odd.epub", Start);
        _books.Books[book.Id] = book;
        await _texts.SaveAsync(book.Id, "*** START OF THE PROJECT GUTENBERG EBOOK ODD ***\n*** END OF THE PROJECT GUTENBERG EBOOK ODD ***", Token);
        var analyzer = new BookAnalyzer(_books, _texts, _steps, _settings, _model, new KeyedPromptProvider(), new ManualTimeProvider(Start), new AnalysisOptions { HeartbeatEvery = TimeSpan.FromHours(1) });

        await analyzer.AnalyzeAsync(book.Id, Token);

        book.Status.ShouldBe(BookStatus.Done);
    }
}
