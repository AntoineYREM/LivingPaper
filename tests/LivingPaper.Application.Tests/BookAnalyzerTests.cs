using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

// The analysis of a book with a simulated model (PAPER-8): the notes of each part, the merge, one profile per character and
// the summary; what is saved at each step; and what happens when the model fails or refuses.
public sealed class BookAnalyzerTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 20, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBookRepository _books = new();
    private readonly InMemoryBookTextStore _texts = new();
    private readonly InMemoryAnalysisStepStore _steps = new();
    private readonly InMemoryLlmSettingsRepository _settingsRepository = new();
    private readonly ScriptedAnalysisModelFactory _model = new();
    private readonly KeyedPromptProvider _prompts = new();
    private readonly ManualTimeProvider _clock = new(Start);
    private readonly LlmSettingsService _settings;
    private AnalysisOptions _options = new() { PagesPerPart = 1, HeartbeatEvery = TimeSpan.FromHours(1) };

    public BookAnalyzerTests()
    {
        var provider = new LlmProvider("p1", "OpenAI", "https://api.openai.com/v1", "sk-secret-key-123", "gpt-5-nano");
        _settingsRepository.Seed(LlmSettings.Restore([provider], "p1", "p1"));
        _settings = new LlmSettingsService(_settingsRepository, new FakeConnectionTester(), new FakeModelCatalog(), new PendingProviderStore(TimeProvider.System));
    }

    public void Dispose() => _settings.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private BookAnalyzer NewAnalyzer() => new(_books, _texts, _steps, _settings, _model, _prompts, _clock, _options);

    // Three pages of a PDF, one per part.
    private static string ThreePages => "Page one text.\fPage two text.\fPage three text.";

    private async Task<Book> WaitingBookAsync(string text = "", string title = "The Lighthouse")
    {
        var book = new Book(BookId.New(), title, "lighthouse.pdf", Start);
        _books.Books[book.Id] = book;
        await _texts.SaveAsync(book.Id, text.Length == 0 ? ThreePages : text, Token);
        return book;
    }

    private async Task<AnalysisFailure?> RunAsync(Book book, CancellationToken? token = null) =>
        await NewAnalyzer().AnalyzeAsync(book.Id, token ?? Token);

    // ---- Nominal ----

    [Fact]
    public async Task AnAnalysis_ReadsEveryPart_Merges_WritesAProfilePerCharacter_AndASummary()
    {
        var book = await WaitingBookAsync();

        var failure = await RunAsync(book);

        failure.ShouldBeNull();
        book.Status.ShouldBe(BookStatus.Done);
        book.Characters.Select(character => (character.Position, character.Name)).ShouldBe([(1, "Argan"), (2, "Toinette")]);
        book.Characters[0].ShortDescription.ShouldBe("Argan is in the story.");
        book.Characters[0].ChatProfile.ShouldBe("I am Argan. My profile.");
        book.Summary.ShouldBe("France, 1673. A satirical comedy.");
        book.ProviderName.ShouldBe("OpenAI");
        book.Model.ShouldBe("gpt-5-nano");
        book.ErrorCode.ShouldBeNull();
        (_model.CallsOf("analysis.readPart"), _model.CallsOf("analysis.mergeCharacters"), _model.CallsOf("analysis.characterProfile"), _model.CallsOf("analysis.bookSummary"))
            .ShouldBe((3, 1, 2, 1));
    }

    [Fact]
    public async Task EachPart_IsSentAsTheUserMessage_WithTheTitleInThePrompt()
    {
        var book = await WaitingBookAsync(title: "Le Malade imaginaire");

        await RunAsync(book);

        var parts = _model.Calls.Where(call => call.IsPart).ToList();
        parts.Select(call => call.User).Order().ShouldBe(["Page one text.", "Page three text.", "Page two text."]);
        parts.ShouldAllBe(call => call.System.Contains("bookTitle=Le Malade imaginaire", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheMerge_ReceivesTheNotesOfEveryPart_AndTheMaximumNumberOfCharacters()
    {
        var book = await WaitingBookAsync();

        await RunAsync(book);

        var merge = _model.Calls.Single(call => call.Step == "analysis.mergeCharacters");
        merge.System.ShouldContain("maxCharacters=10");
        using var notes = JsonDocument.Parse(merge.User);
        notes.RootElement.GetProperty("parts").GetArrayLength().ShouldBe(3);
        merge.User.ShouldContain("personality of Argan");
    }

    [Fact]
    public async Task EachProfile_IsAskedWithTheNameAndTheShortDescriptionOfTheCharacter()
    {
        var book = await WaitingBookAsync();

        await RunAsync(book);

        var profile = _model.Calls.Where(call => call.Step == "analysis.characterProfile").Single(call => call.System.Contains("characterName=Toinette", StringComparison.Ordinal));
        profile.System.ShouldContain("shortDescription=Toinette is in the story.");
        profile.User.ShouldContain("personality of Toinette");
    }

    [Fact]
    public async Task TheProgress_TheTokens_AndTheDuration_AreRecorded()
    {
        var book = await WaitingBookAsync();
        _model.Handler = async (call, number) =>
        {
            _clock.Advance(TimeSpan.FromSeconds(10));
            return await ScriptedAnalysisModelFactory.DefaultReply(call, number);
        };

        await RunAsync(book);

        book.PartsDone.ShouldBe(3);
        book.PartsTotal.ShouldBe(3);
        book.ProfilesDone.ShouldBe(2);
        book.ProfilesTotal.ShouldBe(2);
        book.InputTokens.ShouldBe(100 * 7);
        book.CachedInputTokens.ShouldBe(20 * 7);
        book.OutputTokens.ShouldBe(50 * 7);
        book.StartedAt.ShouldNotBeNull();
        book.FinishedAt.ShouldNotBeNull();
        (book.FinishedAt!.Value - book.StartedAt!.Value).ShouldBeGreaterThan(TimeSpan.FromSeconds(10));
        book.UpdatedAt.ShouldBe(book.FinishedAt.Value);
    }

    [Fact]
    public async Task AfterASuccess_TheStepsAndTheTextOfTheBookAreDeleted()
    {
        var book = await WaitingBookAsync();

        await RunAsync(book);

        _steps.Of(book.Id).ShouldBeEmpty();
        _texts.Texts.ShouldNotContainKey(book.Id);
    }

    [Fact]
    public async Task EveryStep_IsSavedAsSoonAsItSucceeds()
    {
        var book = await WaitingBookAsync();
        _model.Handler = async (call, number) =>
        {
            if (call.Step == "analysis.bookSummary")
            {
                await Task.Delay(100, Token); // The profiles, asked at the same time, are done by then.
                throw new ChatProviderException(ChatFailureKind.Http, "down", 500);
            }

            return await ScriptedAnalysisModelFactory.DefaultReply(call, number);
        };

        await RunAsync(book);

        _steps.Of(book.Id).Keys.Order().ShouldBe(["merge", "part:1", "part:2", "part:3", "profile:1", "profile:2"]);
    }

    [Fact]
    public async Task ABookThatIsNotWaiting_IsLeftAlone()
    {
        var done = await WaitingBookAsync();
        done.Status = BookStatus.Done;

        (await RunAsync(done)).ShouldBeNull();
        (await NewAnalyzer().AnalyzeAsync(BookId.New(), Token)).ShouldBeNull();

        _model.Calls.ShouldBeEmpty();
    }

    // ---- What the model writes ----

    [Fact]
    public async Task ADuplicateName_ComesOnce_TheMostImportantFirst_AndNoMoreThanTheMaximum()
    {
        var book = await WaitingBookAsync();
        var names = new[] { "Argan", "argan ", "Toinette", "Béline", "Cléante", "Angélique", "Béralde", "Diafoirus", "Thomas", "Purgon", "Fleurant", "Louison" };
        _model.Handler = (call, number) => Task.FromResult(call.Step == "analysis.mergeCharacters" ? ScriptedAnalysisModelFactory.MergeReply(names) : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        await RunAsync(book);

        book.Characters.Count.ShouldBe(10);
        book.Characters.Select(character => character.Name.ToUpperInvariant()).Distinct().Count().ShouldBe(10);
        book.Characters[0].Name.ShouldBe("Argan");
        book.Characters[1].Name.ShouldBe("Toinette");
    }

    [Fact]
    public async Task TheTexts_AreCutAtTheirLimit_OnAWholeWord()
    {
        var book = await WaitingBookAsync();
        var longName = string.Join(' ', Enumerable.Repeat("Name", 60));
        var longText = string.Join(' ', Enumerable.Repeat("word", 1200));
        _model.Handler = (call, number) => Task.FromResult(call.Step switch
        {
            "analysis.mergeCharacters" => $$"""{"characters":[{"name":"{{longName}}","shortDescription":"{{longText}}"}]}""",
            "analysis.characterProfile" => longText,
            "analysis.bookSummary" => longText,
            _ => ScriptedAnalysisModelFactory.DefaultReply(call, number).Result,
        });

        await RunAsync(book);

        var character = book.Characters.Single();
        character.Name.Length.ShouldBeLessThanOrEqualTo(CharacterLimits.Name);
        character.ShortDescription.Length.ShouldBeLessThanOrEqualTo(CharacterLimits.ShortDescription);
        character.ChatProfile.Length.ShouldBeLessThanOrEqualTo(CharacterLimits.ChatProfile);
        book.Summary!.Length.ShouldBeLessThanOrEqualTo(CharacterLimits.Summary);
        character.ShortDescription.ShouldEndWith("word");
        book.FindLimitViolations().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheJson_MayBeWrappedInFencesOrASentence()
    {
        var book = await WaitingBookAsync();
        _model.Handler = (call, number) => Task.FromResult(call.IsPart
            ? "Here are the notes:\n```json\n" + ScriptedAnalysisModelFactory.PartReply("Argan") + "\n```\nHope this helps."
            : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        (await RunAsync(book)).ShouldBeNull();
    }

    [Fact]
    public async Task APartWithNoCharacter_IsFine()
    {
        var book = await WaitingBookAsync();
        _model.Handler = (call, number) => Task.FromResult(call.IsPart && call.User.StartsWith("Page two", StringComparison.Ordinal)
            ? """{"characters":[]}"""
            : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        (await RunAsync(book)).ShouldBeNull();
    }

    [Fact]
    public async Task TheNotes_OfAVeryLongBook_AreShrunkBeforeTheMerge_SoTheyFitTheModelContext()
    {
        _options = _options with { MaxNotesLetters = 6000, PagesPerPart = 1 };
        var pages = string.Join('\f', Enumerable.Range(1, 12).Select(page => $"Page {page} text."));
        var book = await WaitingBookAsync(pages);
        _model.Handler = (call, number) => Task.FromResult(call.IsPart
            ? ScriptedAnalysisModelFactory.PartReply(Enumerable.Range(1, 15).Select(index => $"Person {index}").ToArray())
            : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        await RunAsync(book);

        _model.Calls.Single(call => call.Step == "analysis.mergeCharacters").User.Length.ShouldBeLessThanOrEqualTo(6000);
        _model.Calls.Where(call => call.Step == "analysis.bookSummary").ShouldAllBe(call => call.User.Length <= 6000);
    }

    // ---- A reply that cannot be read ----

    [Fact]
    public async Task AReplyThatCannotBeRead_IsAskedAgain_ThenTheAnalysisStopsWithAClearError()
    {
        var book = await WaitingBookAsync();
        _model.Handler = (call, number) => Task.FromResult(call.IsPart ? "I could not do it, sorry. {broken" : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        var failure = await RunAsync(book);

        failure.ShouldBe(AnalysisFailure.InvalidReply);
        book.Status.ShouldBe(BookStatus.Failed);
        book.ErrorCode.ShouldBe("InvalidReply");
        book.ErrorDetail.ShouldNotBeNull().ShouldNotContain("broken");
        book.Characters.ShouldBeEmpty();
        _model.CallsOf("analysis.readPart").ShouldBeLessThanOrEqualTo(3 * 3); // Each part: the first try and two more.
    }

    [Fact]
    public async Task AReplyThatBreaksOnce_IsAskedAgain_AndTheAnalysisGoesOn()
    {
        var book = await WaitingBookAsync("One page only.");
        _model.Handler = (call, number) => Task.FromResult(call.IsPart && number == 1 ? "not json at all" : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        (await RunAsync(book)).ShouldBeNull();

        _model.CallsOf("analysis.readPart").ShouldBe(2);
    }

    [Theory]
    [InlineData("""{"people":[]}""")]
    [InlineData("""{"characters":"Argan"}""")]
    [InlineData("[]")]
    public async Task AJsonOfTheWrongShape_IsAnUnreadableReply_NeverAnEmptyResultInDisguise(string reply)
    {
        var book = await WaitingBookAsync("One page only.");
        _model.Handler = (call, number) => Task.FromResult(call.IsPart ? reply : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        (await RunAsync(book)).ShouldBe(AnalysisFailure.InvalidReply);
    }

    [Fact]
    public async Task AnEmptyProfile_IsAskedAgain_ThenRefused()
    {
        var book = await WaitingBookAsync("One page only.");
        _model.Handler = (call, number) => Task.FromResult(call.Step == "analysis.characterProfile" ? "   " : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        (await RunAsync(book)).ShouldBe(AnalysisFailure.InvalidReply);
    }

    [Fact]
    public async Task AMergeWithNoCharacter_StopsTheAnalysis_NoCardIsInvented()
    {
        var book = await WaitingBookAsync("One page only.");
        _model.Handler = (call, number) => Task.FromResult(call.Step == "analysis.mergeCharacters" ? """{"characters":[]}""" : ScriptedAnalysisModelFactory.DefaultReply(call, number).Result);

        (await RunAsync(book)).ShouldBe(AnalysisFailure.NoCharacters);
        book.Status.ShouldBe(BookStatus.Failed);
    }

    // ---- Resuming ----

    [Fact]
    public async Task AfterAFailure_RetryingResumesAtTheStepThatFailed_WithoutRedoingTheOthers()
    {
        var book = await WaitingBookAsync();
        _model.Handler = (call, number) =>
            call.Step == "analysis.mergeCharacters" ? throw new ChatProviderException(ChatFailureKind.Http, "busy", 503) : ScriptedAnalysisModelFactory.DefaultReply(call, number);
        (await RunAsync(book)).ShouldBe(AnalysisFailure.ModelUnreachable);
        book.Status.ShouldBe(BookStatus.Failed);
        book.PartsDone.ShouldBe(3);
        var tokensAfterFirstTry = book.InputTokens;
        tokensAfterFirstTry.ShouldBe(100 * 4); // The tokens of a try that failed were spent all the same: they are counted.

        _model.Handler = ScriptedAnalysisModelFactory.DefaultReply;
        book.Status = BookStatus.Waiting;
        (await RunAsync(book)).ShouldBeNull();

        _model.CallsOf("analysis.readPart").ShouldBe(3); // Not read again.
        _model.CallsOf("analysis.mergeCharacters").ShouldBe(2);
        book.Status.ShouldBe(BookStatus.Done);
        book.ErrorCode.ShouldBeNull();
        book.InputTokens.ShouldBeGreaterThan(tokensAfterFirstTry); // The tokens of both tries are counted.
    }

    [Fact]
    public async Task AFailureInAProfile_ResumesAtThatProfile()
    {
        _options = _options with { MaxParallelCalls = 1 }; // One at a time, so Argan is done when Toinette fails.
        var book = await WaitingBookAsync();
        _model.Handler = (call, number) => call.Step == "analysis.characterProfile" && call.System.Contains("characterName=Toinette", StringComparison.Ordinal)
            ? throw new ChatProviderException(ChatFailureKind.Http, "down", 500)
            : ScriptedAnalysisModelFactory.DefaultReply(call, number);
        (await RunAsync(book)).ShouldBe(AnalysisFailure.ModelUnreachable);

        _model.Handler = ScriptedAnalysisModelFactory.DefaultReply;
        book.Status = BookStatus.Waiting;
        await RunAsync(book);

        _model.Calls.Count(call => call.Step == "analysis.characterProfile" && call.System.Contains("characterName=Argan", StringComparison.Ordinal)).ShouldBe(1);
        book.Characters.Count.ShouldBe(2);
        book.Status.ShouldBe(BookStatus.Done);
    }

    [Fact]
    public async Task ARetry_ShowsTheProgressAlreadyDone_FromTheFirstPartOn()
    {
        _options = _options with { MaxParallelCalls = 1 }; // One at a time, so the first two parts are done when the third fails.
        var book = await WaitingBookAsync();
        _model.Handler = (call, number) => call.IsPart && call.User.StartsWith("Page three", StringComparison.Ordinal)
            ? throw new ChatProviderException(ChatFailureKind.Http, "down", 500)
            : ScriptedAnalysisModelFactory.DefaultReply(call, number);
        await RunAsync(book);
        book.PartsDone.ShouldBe(2);

        _model.Handler = ScriptedAnalysisModelFactory.DefaultReply;
        book.Status = BookStatus.Waiting;
        await RunAsync(book);

        _model.Calls.Count(call => call.IsPart && call.User.StartsWith("Page one", StringComparison.Ordinal)).ShouldBe(1);
        book.PartsDone.ShouldBe(3);
    }

    // ---- Failures of the model ----

    [Theory]
    [InlineData(ChatFailureKind.Unreachable, null, AnalysisFailure.ModelUnreachable, "Unreachable")]
    [InlineData(ChatFailureKind.TimedOut, null, AnalysisFailure.ModelUnreachable, "TimedOut")]
    [InlineData(ChatFailureKind.Http, 503, AnalysisFailure.ModelUnreachable, "HTTP 503")]
    [InlineData(ChatFailureKind.Http, 429, AnalysisFailure.ModelUnreachable, "HTTP 429")]
    [InlineData(ChatFailureKind.Http, 401, AnalysisFailure.ModelRefused, "HTTP 401")]
    [InlineData(ChatFailureKind.Http, 404, AnalysisFailure.ModelRefused, "HTTP 404")]
    [InlineData(ChatFailureKind.InvalidResponse, null, AnalysisFailure.InvalidReply, "InvalidResponse")]
    [InlineData(ChatFailureKind.EmptyReply, null, AnalysisFailure.InvalidReply, "EmptyReply")]
    public async Task AFailureOfTheModel_StopsTheAnalysis_WithItsCodeAndAShortDetail(ChatFailureKind kind, int? status, AnalysisFailure expected, string detail)
    {
        var book = await WaitingBookAsync();
        _model.Handler = (_, _) => throw new ChatProviderException(kind, "the message of the client", status);

        var failure = await RunAsync(book);

        failure.ShouldBe(expected);
        book.Status.ShouldBe(BookStatus.Failed);
        book.ErrorCode.ShouldBe(expected.ToString());
        book.ErrorDetail.ShouldBe(detail);
        book.FinishedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ADefinitiveRefusal_IsNotAskedAgain()
    {
        var book = await WaitingBookAsync("One page only.");
        _model.Handler = (_, _) => throw new ChatProviderException(ChatFailureKind.Http, "refused", 401);

        await RunAsync(book);

        _model.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WithNoModelChosen_TheAnalysisFails_AskingToChooseOne()
    {
        var book = await WaitingBookAsync();
        _settingsRepository.Seed(LlmSettings.Restore([], null, null));

        (await RunAsync(book)).ShouldBe(AnalysisFailure.NoModel);

        book.Status.ShouldBe(BookStatus.Failed);
        _model.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheTextOfTheBookIsGone_TheAnalysisFails_AskingToSendItAgain()
    {
        var book = await WaitingBookAsync();
        await _texts.DeleteAsync(book.Id, Token);

        (await RunAsync(book)).ShouldBe(AnalysisFailure.TextMissing);
    }

    [Fact]
    public async Task AnUnusablePromptsFile_StopsTheAnalysis_WithWhatToFix()
    {
        var book = await WaitingBookAsync();
        _prompts.Failure = new PromptFileException("prompts.json", PromptKeys.ReadPart, "the prompt is empty");

        (await RunAsync(book)).ShouldBe(AnalysisFailure.PromptFile);

        book.ErrorDetail.ShouldBe("prompts.json, prompt 'analysis.readPart': the prompt is empty");
        _model.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnexpectedError_IsAFailureOfTheBook_NeverAStuckOne_AndItsMessageIsNotKept()
    {
        var book = await WaitingBookAsync();
        _model.Handler = (_, _) => throw new InvalidOperationException("secret text of the book");

        (await RunAsync(book)).ShouldBe(AnalysisFailure.Unexpected);

        book.Status.ShouldBe(BookStatus.Failed);
        book.ErrorDetail.ShouldBe("InvalidOperationException");
    }

    // ---- The content filter ----

    [Fact]
    public async Task APartRefusedByTheFilter_IsReadAgainWithSimpleNotes()
    {
        var book = await WaitingBookAsync();
        _model.Handler = (call, number) => call.Step == "analysis.readPart" && call.User.StartsWith("Page two", StringComparison.Ordinal)
            ? throw new ContentFilteredException()
            : ScriptedAnalysisModelFactory.DefaultReply(call, number);

        (await RunAsync(book)).ShouldBeNull();

        _model.Calls.Count(call => call.Step == "analysis.readPartSimple").ShouldBe(1);
        _model.Calls.Single(call => call.Step == "analysis.readPartSimple").User.ShouldBe("Page two text.");
        book.PartsSkipped.ShouldBe(0);
        book.Status.ShouldBe(BookStatus.Done);
    }

    [Fact]
    public async Task APartRefusedEvenWithSimpleNotes_IsLeftOut_AndTheBookSaysSo()
    {
        var book = await WaitingBookAsync();
        _model.Handler = (call, number) => call.IsPart && call.User.StartsWith("Page two", StringComparison.Ordinal)
            ? throw new ContentFilteredException()
            : ScriptedAnalysisModelFactory.DefaultReply(call, number);

        (await RunAsync(book)).ShouldBeNull();

        book.PartsSkipped.ShouldBe(1);
        book.Status.ShouldBe(BookStatus.Done);
        book.Characters.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task APartLeftOut_StaysLeftOut_WhenTheAnalysisIsRetried()
    {
        var book = await WaitingBookAsync();
        _model.Handler = (call, number) => call.IsPart && call.User.StartsWith("Page two", StringComparison.Ordinal)
            ? throw new ContentFilteredException()
            : call.Step == "analysis.bookSummary" ? throw new ChatProviderException(ChatFailureKind.Http, "down", 500) : ScriptedAnalysisModelFactory.DefaultReply(call, number);
        await RunAsync(book);

        _model.Handler = ScriptedAnalysisModelFactory.DefaultReply;
        book.Status = BookStatus.Waiting;
        await RunAsync(book);

        _model.Calls.Count(call => call.IsPart && call.User.StartsWith("Page two", StringComparison.Ordinal)).ShouldBe(2); // Not asked again after the retry.
        book.PartsSkipped.ShouldBe(1);
    }

    [Theory]
    [InlineData("analysis.mergeCharacters")]
    [InlineData("analysis.characterProfile")]
    [InlineData("analysis.bookSummary")]
    public async Task AFilterThatRefusesALaterStep_StopsTheAnalysis(string step)
    {
        var book = await WaitingBookAsync("One page only.");
        _model.Handler = (call, number) => call.Step == step ? throw new ContentFilteredException() : ScriptedAnalysisModelFactory.DefaultReply(call, number);

        (await RunAsync(book)).ShouldBe(AnalysisFailure.ContentFiltered);
    }

    // ---- Several calls at once ----

    [Fact]
    public async Task NoMoreCallsAtOnceThanTheLimit_AndSeveralWhenItAllows()
    {
        var pages = string.Join('\f', Enumerable.Range(1, 9).Select(page => $"Page {page} text."));
        var book = await WaitingBookAsync(pages);
        _model.Delay = TimeSpan.FromMilliseconds(40);
        _options = _options with { MaxParallelCalls = 3 };

        await RunAsync(book);

        _model.MaxRunningAtOnce.ShouldBe(3);
    }

    [Fact]
    public async Task WithALimitOfOne_TheCallsFollowEachOther()
    {
        var book = await WaitingBookAsync();
        _model.Delay = TimeSpan.FromMilliseconds(20);
        _options = _options with { MaxParallelCalls = 1 };

        await RunAsync(book);

        _model.MaxRunningAtOnce.ShouldBe(1);
    }

    [Fact]
    public async Task WhenOnePartFails_TheOthersStopAtOnce_AndTheFailureReportedIsTheRealOne()
    {
        var pages = string.Join('\f', Enumerable.Range(1, 9).Select(page => $"Page {page} text."));
        var book = await WaitingBookAsync(pages);
        // The first part fails at once; the others are slow, so they are still waiting when it fails.
        _model.Handler = async (call, number) =>
        {
            if (call.IsPart && call.User.StartsWith("Page 1 ", StringComparison.Ordinal))
            {
                throw new ChatProviderException(ChatFailureKind.Http, "refused", 401);
            }

            await Task.Delay(150, Token);
            return await ScriptedAnalysisModelFactory.DefaultReply(call, number);
        };

        (await RunAsync(book)).ShouldBe(AnalysisFailure.ModelRefused);

        _model.CallsOf("analysis.readPart").ShouldBeLessThanOrEqualTo(3); // Only the first three were started.
        _model.CallsOf("analysis.mergeCharacters").ShouldBe(0);
    }

    // ---- Stopping ----

    [Fact]
    public async Task WhenTheApplicationStops_TheAnalysisStopsAtOnce_AndTheBookIsNotMarkedFailed()
    {
        var book = await WaitingBookAsync();
        using var stopping = new CancellationTokenSource();
        _model.Delay = TimeSpan.FromSeconds(30);
        var run = RunAsync(book, stopping.Token);
        await Task.Delay(100, Token);

        await stopping.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => run);
        book.Status.ShouldBe(BookStatus.Running); // Found interrupted at the next start, and retried.
    }

    [Fact]
    public async Task ARunningAnalysis_WritesItsProgressEvenDuringALongCall_SoItIsNotTakenForAStoppedOne()
    {
        _options = _options with { HeartbeatEvery = TimeSpan.FromMilliseconds(25) };
        var book = await WaitingBookAsync("One page only.");
        _model.Delay = TimeSpan.FromMilliseconds(400);
        var savesBefore = 0;
        _books.OnSave = () => Interlocked.Increment(ref savesBefore);

        await RunAsync(book);

        savesBefore.ShouldBeGreaterThan(8); // Without the heartbeat there are about 8 writes: one per step.
    }

    [Fact]
    public async Task TheKey_NeverReachesTheBookOrItsError()
    {
        var book = await WaitingBookAsync();
        _model.Handler = (_, _) => throw new ChatProviderException(ChatFailureKind.Http, "refused sk-secret-key-123", 401);

        await RunAsync(book);

        $"{book.ErrorCode} {book.ErrorDetail} {book.ProviderName} {book.Model}".ShouldNotContain("sk-secret-key-123");
    }
}
