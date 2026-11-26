using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

// PAPER-84: with a model for vectorization, the analysis reads each part with numbered passages, learns which passages each
// scene covers and keeps the passages of the book; with or without it, it writes a short summary of the plot.
public sealed class PassageAnalysisTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 26, 20, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBookRepository _books = new();
    private readonly InMemoryBookTextStore _texts = new();
    private readonly InMemoryAnalysisStepStore _steps = new();
    private readonly InMemoryLlmSettingsRepository _settingsRepository = new();
    private readonly ScriptedAnalysisModelFactory _model = new();
    private readonly KeyedPromptProvider _prompts = new();
    private readonly FakePassageIndex _index = new();
    private readonly ManualTimeProvider _clock = new(Start);
    private readonly LlmSettingsService _settings;
    private readonly AnalysisOptions _options = new() { PagesPerPart = 1, HeartbeatEvery = TimeSpan.FromHours(1) };

    public PassageAnalysisTests()
    {
        ChooseModels(vectorization: true);
        _settings = new LlmSettingsService(_settingsRepository, new FakeConnectionTester(), new FakeModelCatalog(), new PendingProviderStore(TimeProvider.System));
    }

    public void Dispose() => _settings.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private void ChooseModels(bool vectorization)
    {
        LlmProvider[] providers =
        [
            new("p1", "OpenAI", "https://api.openai.com/v1", "sk-secret-key-123", "gpt-5-nano"),
            new("e1", "Vectors", "http://localhost:11434/v1", string.Empty, "bge-m3"),
        ];
        _settingsRepository.Seed(LlmSettings.Restore(providers, "p1", "p1", embeddingProviderId: vectorization ? "e1" : null));
    }

    private static string ThreePages => "Page one text.\fPage two text.\fPage three text.";

    // One page that is cut into several passages: sentences of 100 characters, so about fourteen to a passage.
    private static string LongPage(int sentences) =>
        string.Join(' ', Enumerable.Range(1, sentences).Select(number => $"Sentence {number:D4} tells how Emma crossed the quiet garden and looked at the sky above the town.")) + "\f";

    private static string Reply(params string[] scenes) =>
        "{\"characters\":[" + ScriptedAnalysisModelFactory.Note("Argan") + "],\"scenes\":[" + string.Join(',', scenes) + "]}";

    private static string Scene(string summary, object? first, object? last, params string[] present)
    {
        var range = (first is null ? string.Empty : $",\"firstPassage\":{System.Text.Json.JsonSerializer.Serialize(first)}")
            + (last is null ? string.Empty : $",\"lastPassage\":{System.Text.Json.JsonSerializer.Serialize(last)}");
        return ScriptedAnalysisModelFactory.Scene(summary, present).TrimEnd('}') + range + "}";
    }

    private void AnswerPartsWith(Func<ModelCall, string> reply) =>
        _model.Handler = (call, number) => Task.FromResult(call.IsPart ? reply(call) : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

    private async Task<Book> NewBookAsync(string text)
    {
        var book = new Book(BookId.New(), "The Lighthouse", "lighthouse.pdf", Start);
        _books.Books[book.Id] = book;
        await _texts.SaveAsync(book.Id, text, Token);
        return book;
    }

    private BookAnalyzer Analyzer() => new(_books, _texts, _steps, _settings, _model, _prompts, _clock, _options, _index);

    private async Task<Book> RunAsync(string? text = null)
    {
        var book = await NewBookAsync(text ?? ThreePages);
        await Analyzer().AnalyzeAsync(book.Id, Token);
        return book;
    }

    private IReadOnlyList<StoredPassage> PassagesOf(Book book) => _index.Books[new BookKey(BookShelf.Library, book.Id.ToString())];

    // The summaries of the scenes of each passage, as "1: a, b".
    private IReadOnlyList<string> ScenesByPassage(Book book) =>
        [.. PassagesOf(book).Select(passage => $"{passage.Number}: {string.Join(", ", passage.Scenes.Select(scene => scene.Summary))}")];

    // ---- Without a model for vectorization ----

    [Fact]
    public async Task WithoutAModelForVectorization_ThePartsAreReadAsBefore_AndNoPassageIsKept()
    {
        ChooseModels(vectorization: false);

        var book = await RunAsync();

        book.Status.ShouldBe(BookStatus.Done);
        _model.Calls.Where(call => call.IsPart).Select(call => call.User).Order().ShouldBe(["Page one text.", "Page three text.", "Page two text."]);
        _model.Calls.Where(call => call.Step == "analysis.readPart").ShouldAllBe(call => call.System == "[analysis.readPart] bookTitle=The Lighthouse passageRanges=");
        _index.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithoutAModelForVectorization_PassagesLeftByAnEarlierAnalysisAreRemoved()
    {
        ChooseModels(vectorization: false);

        var book = await RunAsync();

        _index.Deleted.ShouldBe([new BookKey(BookShelf.Library, book.Id.ToString())]);
    }

    [Fact]
    public async Task AnIndexThatCannotBeUsed_TurnsTheFeatureOff()
    {
        _index.Problem = PassageIndexProblem.FileUnusable;

        var book = await RunAsync();

        book.Status.ShouldBe(BookStatus.Done);
        _model.Calls.Where(call => call.IsPart).ShouldAllBe(call => !call.User.Contains("[P", StringComparison.Ordinal));
        _index.Books.ShouldBeEmpty();
        _index.Deleted.ShouldBeEmpty();
    }

    // ---- The passages the model reads ----

    [Fact]
    public async Task WithAModelForVectorization_EachPartIsReadWithItsNumberedPassages_AndTheRangesAreAskedFor()
    {
        await RunAsync();

        var parts = _model.Calls.Where(call => call.IsPart).OrderBy(call => call.User, StringComparer.Ordinal).ToList();
        parts.Select(call => call.User).ShouldBe(["[P1] Page one text.", "[P2] Page two text.", "[P3] Page three text."]);
        parts.ShouldAllBe(call => call.System == "[analysis.readPart] bookTitle=The Lighthouse passageRanges=[analysis.passageRanges] ");
    }

    [Fact]
    public async Task ThePartTheModelReads_HoldsEachSentenceOnce()
    {
        await RunAsync(LongPage(40));

        var user = _model.Calls.Single(call => call.IsPart).User;
        user.ShouldStartWith("[P1] Sentence 0001");
        user.ShouldContain("\n[P2] Sentence ");
        System.Text.RegularExpressions.Regex.Count(user, "Sentence ").ShouldBe(40); // The repeated sentences are left out.
    }

    [Fact]
    public async Task TheSimpleNotes_AreAskedWithTheTextAlone_AndTheirPassagesHaveNoScene()
    {
        _model.Handler = (call, number) => call.Step == "analysis.readPart" && call.User.Contains("Page one", StringComparison.Ordinal)
            ? throw new ContentFilteredException()
            : ScriptedAnalysisModelFactory.DefaultReply(call, number);

        var book = await RunAsync();

        _model.Calls.Single(call => call.Step == "analysis.readPartSimple").User.ShouldBe("Page one text.");
        PassagesOf(book)[0].Scenes.ShouldBeEmpty();
        PassagesOf(book).Count.ShouldBe(3);
    }

    // ---- Which passages belong to which scene ----

    [Fact]
    public async Task EachPassage_IsKeptWithTheScenesThatCoverIt_AndWhoIsThere()
    {
        AnswerPartsWith(_ => Reply(
            Scene("The ball.", 1, 2, "Emma", "Charles"),
            Scene("The window.", 2, 2, "Emma"),
            Scene("The morning.", 3, 3, "Charles")));

        var book = await RunAsync(LongPage(40));

        var passages = PassagesOf(book);
        ScenesByPassage(book).ShouldBe(["1: The ball.", "2: The ball., The window.", "3: The morning."]);
        passages[1].Scenes[0].Present.ShouldBe(["Emma", "Charles"]);
        passages[1].Scenes[1].Present.ShouldBe(["Emma"]);
        passages[0].Text.ShouldStartWith("Sentence 0001");
        passages[1].Text.ShouldStartWith(passages[0].Text[passages[0].Text.LastIndexOf("Sentence", StringComparison.Ordinal)..]); // With the repeated sentence.
    }

    [Fact]
    public async Task PassagesAreNumberedThroughTheBook_AndAScenePointsInsideItsOwnPart()
    {
        // The second part holds passage 2: its scene says so; the first and third parts point outside theirs.
        AnswerPartsWith(call => Reply(Scene("Scene of " + call.User[5..13], 2, 2, "Emma")));

        var book = await RunAsync();

        ScenesByPassage(book).ShouldBe(["1: ", "2: Scene of Page two", "3: "]);
    }

    [Theory]
    [InlineData(2, 1)] // Reversed.
    [InlineData(0, 1)]
    [InlineData(1, 4)] // Past the end of the part.
    [InlineData(-1, 2)]
    [InlineData(1, null)]
    [InlineData(null, 2)]
    [InlineData(null, null)]
    [InlineData("one", "two")]
    [InlineData(1.5, 2)]
    [InlineData(true, 2)]
    public async Task ARangeThatCannotBeFollowed_IsIgnored_TheSceneStillTellsTheStory(object? first, object? last)
    {
        AnswerPartsWith(_ => Reply(Scene("The ball.", first, last, "Emma")));

        var book = await RunAsync(LongPage(40));

        PassagesOf(book).Count.ShouldBe(3);
        PassagesOf(book).ShouldAllBe(passage => passage.Scenes.Count == 0);
        _model.Calls.Single(call => call.Step == "analysis.storySummary").User.ShouldContain("The ball.");
    }

    [Fact]
    public async Task ARange_CanBeWrittenAsATextOrWithItsMarker()
    {
        AnswerPartsWith(_ => Reply(Scene("The ball.", "1", "P2", "Emma"), Scene("The night.", "[P3]", " 3 ", "Emma")));

        var book = await RunAsync(LongPage(40));

        ScenesByPassage(book).ShouldBe(["1: The ball.", "2: The ball.", "3: The night."]);
    }

    [Fact]
    public async Task AMarkerCopiedIntoATextByTheModel_IsRemoved()
    {
        AnswerPartsWith(_ => Reply(Scene("[P1] Emma dances [P2] with a viscount. [P12]", 1, 1, "[P1] Emma", "Charles")));

        var book = await RunAsync(LongPage(40));

        var scene = PassagesOf(book)[0].Scenes.ShouldHaveSingleItem();
        scene.Summary.ShouldBe("Emma dances with a viscount.");
        scene.Present.ShouldBe(["Emma", "Charles"]);
        _model.Calls.Single(call => call.Step == "analysis.storySummary").User.ShouldNotContain("[P");
    }

    // ---- What does not block the book ----

    [Fact]
    public async Task ARefusedPart_DoesNotBlockTheBook_ItsPassagesAreKeptWithoutScene()
    {
        _model.Handler = (call, number) => call.IsPart && call.User.Contains("Page two", StringComparison.Ordinal)
            ? throw new ContentFilteredException()
            : Task.FromResult(call.IsPart ? Reply(Scene("A scene.", 1, 3, "Argan")) : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        var book = await RunAsync();

        book.Status.ShouldBe(BookStatus.Done);
        book.PartsSkipped.ShouldBe(1);
        PassagesOf(book).Select(passage => passage.Text).ShouldBe(["Page one text.", "Page one text. Page two text.", "Page two text. Page three text."]);
        PassagesOf(book)[1].Scenes.ShouldBeEmpty();
    }

    [Fact]
    public async Task ABookWithNoSceneAtAll_IsUsable_AndAllItsPassagesAreKept()
    {
        AnswerPartsWith(_ => ScriptedAnalysisModelFactory.PartReplyWithoutScenes("Argan"));

        var book = await RunAsync();

        book.Status.ShouldBe(BookStatus.Done);
        book.StorySummary.ShouldBeNull();
        book.PlotSummary.ShouldBeNull();
        _model.CallsOf("analysis.plotSummary").ShouldBe(0);
        PassagesOf(book).Count.ShouldBe(3);
        PassagesOf(book).ShouldAllBe(passage => passage.Scenes.Count == 0);
    }

    // ---- Saving ----

    [Fact]
    public async Task ThePassages_AreSavedBeforeTheTextIsDeleted_AndBeforeTheBookIsDone()
    {
        var book = await NewBookAsync(ThreePages);
        var seen = new List<(BookStatus Status, bool Text)>();
        _index.OnSave = () => seen.Add((book.Status, _texts.ReadAsync(book.Id, Token).Result is not null));

        await Analyzer().AnalyzeAsync(book.Id, Token);

        seen.ShouldBe([(BookStatus.Running, true)]);
        (await _texts.ReadAsync(book.Id, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task AnIndexThatFailsToSave_DoesNotFailTheAnalysis()
    {
        _index.Failure = new PassageIndexException("disk full");

        var book = await RunAsync();

        book.Status.ShouldBe(BookStatus.Done);
        _index.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAnalysisThatFails_KeepsNoPassage()
    {
        _model.Handler = (call, number) => call.Step == "analysis.mergeCharacters"
            ? throw new ChatProviderException(ChatFailureKind.Http, "down", 500)
            : ScriptedAnalysisModelFactory.DefaultReply(call, number);

        var book = await RunAsync();

        book.Status.ShouldBe(BookStatus.Failed);
        _index.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task AResumedAnalysis_KeepsTheRangesItSaved()
    {
        AnswerPartsWith(_ => Reply(Scene("The ball.", 1, 2, "Emma")));
        var answerParts = _model.Handler;
        var fail = true;
        _model.Handler = (call, number) => fail && call.Step == "analysis.mergeCharacters"
            ? throw new ChatProviderException(ChatFailureKind.Http, "down", 500)
            : answerParts(call, number);
        var book = await RunAsync(LongPage(40));
        fail = false;
        book.Status = BookStatus.Waiting;

        await Analyzer().AnalyzeAsync(book.Id, Token);

        book.Status.ShouldBe(BookStatus.Done);
        _model.Calls.Count(call => call.IsPart).ShouldBe(1);
        ScenesByPassage(book).ShouldBe(["1: The ball.", "2: The ball.", "3: "]);
    }

    // ---- The short summary of the plot ----

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("fact", count));

    private void AnswerThePlotWith(Func<int, string> reply) =>
        _model.Handler = (call, number) => Task.FromResult(call.Step == "analysis.plotSummary" ? reply(number) : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EveryAnalysis_WritesAShortSummaryOfThePlot_FromTheStory(bool vectorization)
    {
        ChooseModels(vectorization);

        var book = await RunAsync();

        var call = _model.Calls.Single(call => call.Step == "analysis.plotSummary");
        call.System.ShouldBe("[analysis.plotSummary] bookTitle=The Lighthouse maxWords=500");
        call.User.ShouldBe(book.StorySummary);
        book.PlotSummary.ShouldBe("The plot, in short.");
        book.FindLimitViolations().ShouldBeEmpty();
    }

    [Fact]
    public async Task APlotSummaryThatIsTooLong_IsAskedAgainWithATighterLimit_AndTheShortestIsKept()
    {
        AnswerThePlotWith(number => Words(number == 1 ? 900 : 450));

        var book = await RunAsync();

        _model.Calls.Where(call => call.Step == "analysis.plotSummary").Select(call => call.System.Split(' ')[^1]).ShouldBe(["maxWords=500", "maxWords=400"]);
        book.PlotSummary!.Split(' ').Length.ShouldBe(450);
    }

    [Fact]
    public async Task APlotSummary_IsCutAtItsLimit_OnAWholeWord()
    {
        AnswerThePlotWith(_ => Words(3000));

        var book = await RunAsync();

        _model.CallsOf("analysis.plotSummary").ShouldBe(3);
        book.PlotSummary!.Length.ShouldBeLessThanOrEqualTo(CharacterLimits.PlotSummary);
        book.PlotSummary.ShouldEndWith("fact");
    }

    [Fact]
    public async Task APlotSummaryRefusedByTheContentFilter_LeavesTheBookWithoutIt()
    {
        AnswerThePlotWith(_ => throw new ContentFilteredException());

        var book = await RunAsync();

        book.Status.ShouldBe(BookStatus.Done);
        book.StorySummary.ShouldNotBeNull();
        book.PlotSummary.ShouldBeNull();
        PassagesOf(book).Count.ShouldBe(3);
    }

    [Fact]
    public async Task AFailureWhileThePlotIsWritten_FailsTheAnalysis_AndARetryDoesNotWriteTheStoryAgain()
    {
        var fail = true;
        _model.Handler = (call, number) => fail && call.Step == "analysis.plotSummary"
            ? throw new ChatProviderException(ChatFailureKind.Http, "down", 500)
            : ScriptedAnalysisModelFactory.DefaultReply(call, number);
        var book = await RunAsync();
        book.Status.ShouldBe(BookStatus.Failed);
        fail = false;
        book.Status = BookStatus.Waiting;

        await Analyzer().AnalyzeAsync(book.Id, Token);

        book.Status.ShouldBe(BookStatus.Done);
        book.PlotSummary.ShouldBe("The plot, in short.");
        _model.CallsOf("analysis.storySummary").ShouldBe(1);
    }

    [Fact]
    public async Task APlotSummaryAlreadyWritten_IsNotAskedAgainOnARetry()
    {
        var fail = true;
        _model.Handler = async (call, number) =>
        {
            if (fail && call.Step == "analysis.bookSummary")
            {
                await Task.Delay(200, Token); // The story and the plot, asked at the same time, are saved by then.
                throw new ChatProviderException(ChatFailureKind.Http, "down", 500);
            }

            return await ScriptedAnalysisModelFactory.DefaultReply(call, number);
        };
        var book = await RunAsync();
        fail = false;
        book.Status = BookStatus.Waiting;

        await Analyzer().AnalyzeAsync(book.Id, Token);

        book.PlotSummary.ShouldBe("The plot, in short.");
        _model.CallsOf("analysis.plotSummary").ShouldBe(1);
    }
}
