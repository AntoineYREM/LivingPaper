using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

// PAPER-52 and PAPER-53: the analysis reads the scenes of each part, writes the story of the whole book scene by scene (the
// story the characters know when they chat), says when it has holes, and keeps the profiles for who the characters are.
public sealed class StoryAnalysisTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 20, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBookRepository _books = new();
    private readonly InMemoryBookTextStore _texts = new();
    private readonly InMemoryAnalysisStepStore _steps = new();
    private readonly InMemoryLlmSettingsRepository _settingsRepository = new();
    private readonly ScriptedAnalysisModelFactory _model = new();
    private readonly KeyedPromptProvider _prompts = new();
    private readonly ManualTimeProvider _clock = new(Start);
    private readonly LlmSettingsService _settings;
    private AnalysisOptions _options = new() { PagesPerPart = 1, HeartbeatEvery = TimeSpan.FromHours(1) };

    public StoryAnalysisTests()
    {
        var provider = new LlmProvider("p1", "OpenAI", "https://api.openai.com/v1", "sk-secret-key-123", "gpt-5-nano");
        _settingsRepository.Seed(LlmSettings.Restore([provider], "p1", "p1"));
        _settings = new LlmSettingsService(_settingsRepository, new FakeConnectionTester(), new FakeModelCatalog(), new PendingProviderStore(TimeProvider.System));
    }

    public void Dispose() => _settings.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string ThreePages => "Page one text.\fPage two text.\fPage three text.";

    private async Task<Book> RunAsync(string? text = null)
    {
        var book = new Book(BookId.New(), "The Lighthouse", "lighthouse.pdf", Start);
        _books.Books[book.Id] = book;
        await _texts.SaveAsync(book.Id, text ?? ThreePages, Token);
        await new BookAnalyzer(_books, _texts, _steps, _settings, _model, _prompts, _clock, _options).AnalyzeAsync(book.Id, Token);
        return book;
    }

    // ---- The scenes are asked for, and told ----

    [Fact]
    public async Task TheStory_IsWrittenOnce_FromTheScenesOfEveryPart_InOrder()
    {
        _model.Handler = (call, number) => Task.FromResult(call.IsPart
            ? "{\"characters\":[" + ScriptedAnalysisModelFactory.Note("Argan") + "],\"scenes\":[" + ScriptedAnalysisModelFactory.Scene("Scene of " + call.User, "Argan", "Toinette") + "]}"
            : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        var book = await RunAsync();

        book.Status.ShouldBe(BookStatus.Done);
        book.StorySummary.ShouldBe("Scene 1. A scene happens. Present: Argan, Toinette.");
        book.StorySummaryPartial.ShouldBeFalse();
        var story = _model.Calls.Single(call => call.Step == "analysis.storySummary");
        story.User.ShouldContain("Scene of Page one text.");
        story.User.ShouldContain("Scene of Page two text.");
        story.User.ShouldContain("Scene of Page three text.");
        story.User.IndexOf("Page one", StringComparison.Ordinal).ShouldBeLessThan(story.User.IndexOf("Page two", StringComparison.Ordinal));
        story.User.IndexOf("Page two", StringComparison.Ordinal).ShouldBeLessThan(story.User.IndexOf("Page three", StringComparison.Ordinal));
        story.User.ShouldContain("\"present\":[\"Argan\",\"Toinette\"]");
        story.System.ShouldContain("bookTitle=The Lighthouse");
    }

    [Fact]
    public async Task TheStoryPrompt_ReceivesTheWordBudget_OfTheLengthOfTheBook()
    {
        await RunAsync();

        _model.Calls.Single(call => call.Step == "analysis.storySummary").System.ShouldContain("maxWords=800");
    }

    [Fact]
    public async Task ALongBook_GetsALongerStory_UpToTheMaximum()
    {
        _options = _options with { PagesPerPart = 50 };
        var longBook = string.Join('\f', Enumerable.Range(1, 500).Select(page => new string('a', 3000)));

        await RunAsync(longBook);

        _model.Calls.Single(call => call.Step == "analysis.storySummary").System.ShouldContain("maxWords=3000");
    }

    [Fact]
    public async Task TheStory_IsCutAtItsLimit_OnAWholeWord()
    {
        _model.Handler = (call, number) => Task.FromResult(call.Step == "analysis.storySummary"
            ? string.Join(' ', Enumerable.Repeat("word", 9000))
            : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        var book = await RunAsync();

        book.StorySummary!.Length.ShouldBeLessThanOrEqualTo(CharacterLimits.StorySummary);
        book.StorySummary.ShouldEndWith("word");
        book.FindLimitViolations().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheScenes_OfAVeryLongBook_AreShrunkBeforeTheStory_SoTheyFitTheModelContext()
    {
        _options = _options with { MaxNotesLetters = 4000 };
        var pages = string.Join('\f', Enumerable.Range(1, 12).Select(page => $"Page {page} text."));
        _model.Handler = (call, number) => Task.FromResult(call.IsPart
            ? "{\"characters\":[" + ScriptedAnalysisModelFactory.Note("Argan") + "],\"scenes\":[" + string.Join(',', Enumerable.Range(1, 2).Select(index => ScriptedAnalysisModelFactory.Scene(new string('s', 500), "Argan"))) + "]}"
            : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        await RunAsync(pages);

        _model.Calls.Single(call => call.Step == "analysis.storySummary").User.Length.ShouldBeLessThanOrEqualTo(4000);
    }

    // ---- The model is told that the notes serve for the story ----

    [Fact]
    public async Task TheStoryStep_IsSavedAsSoonAsItSucceeds_AndNotAskedAgainOnARetry()
    {
        var book = await RunAsync();
        _steps.Of(book.Id).ShouldBeEmpty(); // Cleaned after a success.

        var failing = new Book(BookId.New(), "The Lighthouse", "lighthouse.pdf", Start);
        _books.Books[failing.Id] = failing;
        await _texts.SaveAsync(failing.Id, ThreePages, Token);
        var first = true;
        _model.Handler = async (call, number) =>
        {
            if (call.Step == "analysis.bookSummary" && first)
            {
                first = false;
                await Task.Delay(100, Token); // The story, asked at the same time, is done and saved by then.
                throw new ChatProviderException(ChatFailureKind.Http, "down", 500);
            }

            return await ScriptedAnalysisModelFactory.DefaultReply(call, number);
        };
        var analyzer = new BookAnalyzer(_books, _texts, _steps, _settings, _model, _prompts, _clock, _options);
        await analyzer.AnalyzeAsync(failing.Id, Token);
        failing.Status.ShouldBe(BookStatus.Failed);
        var storyCallsBefore = _model.CallsOf("analysis.storySummary");

        failing.Status = BookStatus.Waiting;
        await analyzer.AnalyzeAsync(failing.Id, Token);

        failing.Status.ShouldBe(BookStatus.Done);
        failing.StorySummary.ShouldNotBeNull();
        _model.CallsOf("analysis.storySummary").ShouldBe(storyCallsBefore); // Read from the saved step, not paid again.
    }

    // ---- The holes of the story ----

    [Fact]
    public async Task APartWithoutScenes_LeavesAHole_TheStoryIsWrittenFromTheOthers_AndSaysItIsPartial()
    {
        _model.Handler = (call, number) => Task.FromResult(call.IsPart && call.User.StartsWith("Page two", StringComparison.Ordinal)
            ? ScriptedAnalysisModelFactory.PartReplyWithoutScenes("Argan")
            : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        var book = await RunAsync();

        book.Status.ShouldBe(BookStatus.Done);
        book.StorySummary.ShouldNotBeNull();
        book.StorySummaryPartial.ShouldBeTrue();
        _model.Calls.Single(call => call.Step == "analysis.storySummary").User.ShouldNotContain("\"part\":2");
    }

    [Fact]
    public async Task APartThatTheContentFilterRefused_IsLeftOutOfTheStory_WhichIsPartial()
    {
        _model.Handler = (call, number) => call.IsPart && call.User.StartsWith("Page three", StringComparison.Ordinal)
            ? throw new ContentFilteredException()
            : ScriptedAnalysisModelFactory.DefaultReply(call, number);

        var book = await RunAsync();

        book.Status.ShouldBe(BookStatus.Done);
        book.PartsSkipped.ShouldBe(1);
        book.StorySummary.ShouldNotBeNull();
        book.StorySummaryPartial.ShouldBeTrue();
    }

    [Fact]
    public async Task APartRead_WithTheSimpleNotes_HasNoScenes_EvenIfTheModelAddsSome()
    {
        _model.Handler = (call, number) =>
        {
            if (call.Step == "analysis.readPart" && call.User.StartsWith("Page one", StringComparison.Ordinal))
            {
                throw new ContentFilteredException(); // The rich notes are refused: the simple ones are tried.
            }

            return ScriptedAnalysisModelFactory.DefaultReply(call, number);
        };

        var book = await RunAsync();

        _model.CallsOf("analysis.readPartSimple").ShouldBe(1);
        book.StorySummaryPartial.ShouldBeTrue();
        _model.Calls.Single(call => call.Step == "analysis.storySummary").User.ShouldNotContain("\"part\":1");
    }

    [Fact]
    public async Task WhenNoPartGivesAScene_ThereIsNoStory_NoCallIsMade_AndTheBookIsStillAnalyzed()
    {
        _model.Handler = (call, number) => Task.FromResult(call.IsPart
            ? ScriptedAnalysisModelFactory.PartReplyWithoutScenes("Argan")
            : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        var book = await RunAsync();

        book.Status.ShouldBe(BookStatus.Done);
        book.Characters.ShouldNotBeEmpty();
        book.StorySummary.ShouldBeNull();
        book.StorySummaryPartial.ShouldBeTrue();
        _model.CallsOf("analysis.storySummary").ShouldBe(0);
    }

    [Theory]
    [InlineData("""{"characters":[],"scenes":"none"}""")]
    [InlineData("""{"characters":[],"scenes":[1,"two",null]}""")]
    [InlineData("""{"characters":[],"scenes":[{"summary":"   ","present":[]}]}""")]
    public async Task ScenesOfTheWrongShape_AreNeverTrusted_TheyAreHolesNotErrors(string reply)
    {
        _model.Handler = (call, number) => Task.FromResult(call.IsPart ? reply : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        var book = await RunAsync("One page only.");

        book.Status.ShouldBe(BookStatus.Done);
        book.StorySummary.ShouldBeNull();
        book.StorySummaryPartial.ShouldBeTrue();
    }

    // ---- What the model writes is limited ----

    [Fact]
    public async Task TheScenes_AreLimited_TheNamesPresentAreCut_KeptOnce_AndLimited()
    {
        var present = string.Join(',', Enumerable.Range(1, 40).Select(index => $"\"Person {index}\"")) + ",\"person 1\"";
        var scenes = string.Join(',', Enumerable.Range(1, 60).Select(index => $$"""{"summary":"scene {{index}}","present":[{{present}}]}"""));
        _model.Handler = (call, number) => Task.FromResult(call.IsPart
            ? "{\"characters\":[" + ScriptedAnalysisModelFactory.Note("Argan") + "],\"scenes\":[" + scenes + "]}"
            : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        await RunAsync("One page only.");

        var story = _model.Calls.Single(call => call.Step == "analysis.storySummary").User;
        System.Text.RegularExpressions.Regex.Count(story, "\"summary\"").ShouldBe(40);
        story.ShouldContain("Person 15");
        story.ShouldNotContain("Person 16");
    }

    // ---- The word limit is kept ----

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("fact", count));

    [Fact]
    public async Task AStoryWithinTheBudget_IsAskedOnce()
    {
        _model.Handler = (call, number) => Task.FromResult(call.Step == "analysis.storySummary" ? Words(850) : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result); // 10% over 800 is tolerated.

        var book = await RunAsync();

        _model.CallsOf("analysis.storySummary").ShouldBe(1);
        book.StorySummary!.Split(' ').Length.ShouldBe(850);
    }

    [Fact]
    public async Task AStoryThatGoesOverTheBudget_IsAskedAgainWithATighterLimit_AndTheShorterOneIsKept()
    {
        _model.Handler = (call, number) => Task.FromResult(call.Step == "analysis.storySummary"
            ? Words(number == 1 ? 2000 : 700)
            : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        var book = await RunAsync();

        var asked = _model.Calls.Where(call => call.Step == "analysis.storySummary").ToList();
        asked.Count.ShouldBe(2);
        asked[0].System.ShouldContain("maxWords=800");
        asked[1].System.ShouldContain("maxWords=640");
        book.StorySummary!.Split(' ').Length.ShouldBe(700);
        book.Status.ShouldBe(BookStatus.Done);
    }

    [Fact]
    public async Task AModelThatNeverObeysTheLimit_IsAskedThreeTimesAtMost_AndItsShortestStoryIsKept_NeverCutInTheMiddle()
    {
        _model.Handler = (call, number) => Task.FromResult(call.Step == "analysis.storySummary"
            ? Words(number switch { 1 => 3000, 2 => 2000, _ => 2500 })
            : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        var book = await RunAsync();

        _model.CallsOf("analysis.storySummary").ShouldBe(3);
        _model.Calls.Where(call => call.Step == "analysis.storySummary").Select(call => call.System.Split(' ')[^1]).ShouldBe(["maxWords=800", "maxWords=640", "maxWords=512"]);
        book.StorySummary!.Split(' ').Length.ShouldBe(2000);
        book.Status.ShouldBe(BookStatus.Done);
    }

    [Fact]
    public async Task EveryTryOfTheStory_PaysItsTokens()
    {
        _model.Handler = (call, number) => Task.FromResult(call.Step == "analysis.storySummary" ? Words(3000) : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        var book = await RunAsync();

        book.InputTokens.ShouldBe(100 * (7 + 3 + 1)); // 3 parts, the merge, 2 profiles, the summary, 3 tries at the story, and the plot.
    }

    // ---- The page can say what the analysis is doing ----

    [Fact]
    public async Task WhileTheStoryIsWritten_TheBookSaysWhichTryItIsAt()
    {
        var seen = new List<(int Try, int Tries)>();
        _model.Handler = (call, number) =>
        {
            if (call.Step == "analysis.storySummary")
            {
                var running = _books.Books.Values.Single();
                seen.Add((running.StoryTry, running.StoryTries));
                return Task.FromResult(Words(number == 1 ? 2000 : 700));
            }

            return Task.FromResult(ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);
        };

        var book = await RunAsync();

        seen.ShouldBe([(1, 3), (2, 3)]);
        book.StoryTry.ShouldBe(0); // Done: nothing is being written any more.
        book.StoryTries.ShouldBe(0);
    }

    // ---- The profile no longer carries the story ----

    [Fact]
    public async Task TheProfileStep_StillReceivesTheNotes_AndTheStoryIsAskedApart()
    {
        await RunAsync();

        _model.Calls.Where(call => call.Step == "analysis.characterProfile").ShouldAllBe(call => !call.System.Contains("maxWords", StringComparison.Ordinal));
        _model.Calls.Where(call => call.Step == "analysis.storySummary").ShouldHaveSingleItem();
    }
}

public class StoryBudgetTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("one", 1)]
    [InlineData("Scene 1.  A  man\nwalks. Present: Argan.", 7)]
    public void CountWords_CountsTheWordsSeparatedByAnySpace(string text, int expected)
    {
        StoryBudget.CountWords(text).ShouldBe(expected);
    }

    private static readonly AnalysisOptions Options = new();

    [Theory]
    [InlineData(0, 800)]
    [InlineData(6_000, 820)]
    [InlineData(120_000, 1200)] // A play of 20,000 words.
    [InlineData(600_000, 2800)] // A novel of 100,000 words.
    [InlineData(1_000_000, 3000)]
    [InlineData(int.MaxValue, 3000)]
    public void TheBudget_GrowsWithTheBook_BetweenTheMinimumAndTheMaximum(int textLength, int expected)
    {
        StoryBudget.Words(textLength, Options).ShouldBe(expected);
    }

    [Fact]
    public void TheLimits_AreThoseOfTheOptions()
    {
        var options = new AnalysisOptions { MinStoryWords = 100, MaxStoryWords = 150 };

        StoryBudget.Words(0, options).ShouldBe(100);
        StoryBudget.Words(int.MaxValue, options).ShouldBe(150);
    }

    [Fact]
    public void ANegativeLength_IsTreatedAsEmpty()
    {
        StoryBudget.Words(-5, Options).ShouldBe(800);
    }
}
