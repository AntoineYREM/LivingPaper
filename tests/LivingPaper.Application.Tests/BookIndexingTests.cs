using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

// PAPER-85: the vectors of the passages, computed at the end of an analysis or again on demand, and what a book says about
// its passages.
public sealed class BookIndexingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 20, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBookRepository _books = new();
    private readonly InMemoryBookTextStore _texts = new();
    private readonly InMemoryAnalysisStepStore _steps = new();
    private readonly InMemoryLlmSettingsRepository _settingsRepository = new();
    private readonly ScriptedAnalysisModelFactory _model = new();
    private readonly FakeEmbeddingModelFactory _embeddings = new();
    private readonly FakePassageIndex _index = new();
    private readonly FakeAnalysisScheduler _scheduler = new();
    private readonly LlmSettingsService _settings;
    private readonly AnalysisOptions _options = new() { PagesPerPart = 1, HeartbeatEvery = TimeSpan.FromHours(1), PassagesPerEmbeddingCall = 2 };
    private readonly BookIndexer _indexer;
    private readonly BookService _service;

    public BookIndexingTests()
    {
        Choose("bge-m3");
        _settings = new LlmSettingsService(_settingsRepository, new FakeConnectionTester(), new FakeModelCatalog(), new PendingProviderStore(TimeProvider.System));
        _indexer = new BookIndexer(_index, _settings, _embeddings, _options);
        _service = new BookService(
            _books, _texts, new FakeBookTextExtractor(), _scheduler, new FixedTimeProvider(Now), _options, passages: _index, indexer: _indexer);
    }

    public void Dispose()
    {
        _service.Dispose();
        _settings.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // null: no model for vectorization.
    private void Choose(string? embeddingModel)
    {
        var providers = new List<LlmProvider> { new("p1", "OpenAI", "https://api.openai.com/v1", "sk-secret-key-123", "gpt-5-nano") };
        if (embeddingModel is not null)
        {
            providers.Add(new LlmProvider("e1", "Vectors", "http://localhost:11434/v1", string.Empty, embeddingModel));
        }

        _settingsRepository.Seed(LlmSettings.Restore(providers, "p1", "p1", embeddingProviderId: embeddingModel is null ? null : "e1"));
    }

    private static BookKey Key(Book book) => new(BookShelf.Library, book.Id.ToString());

    private static StoredPassage Passage(int number, params PassageScene[] scenes) => new(number, $"Text of passage {number}.", scenes);

    private async Task<Book> DoneBookAsync(int passages = 3)
    {
        var book = new Book(BookId.New(), "The Lighthouse", "lighthouse.pdf", Now) { Status = BookStatus.Done };
        book.Characters.Add(new Character(1, "Argan", "The sick man.", "I am Argan."));
        _books.Books[book.Id] = book;
        if (passages > 0)
        {
            await _index.SavePassagesAsync(Key(book), [.. Enumerable.Range(1, passages).Select(number => Passage(number))], Token);
        }

        return book;
    }

    private Task<PassagesView?> ViewAsync(Book book) => _service.DescribePassagesAsync(book, Token);

    private static ChatProviderException Http(int status) => new(ChatFailureKind.Http, "refused", status);

    // ---- What is vectorized ----

    [Fact]
    public void APassageWithoutScene_IsVectorizedAsItIs()
    {
        PassageHeader.TextToVectorize(Passage(1)).ShouldBe("Text of passage 1.");
    }

    [Fact]
    public void APassage_IsVectorizedUnderAHeaderThatTellsItsSceneAndWhoIsThere()
    {
        var passage = Passage(1, new PassageScene("Emma accepts a waltz\nfrom the Viscount.", ["Emma", "Viscount"]));

        PassageHeader.TextToVectorize(passage).ShouldBe("[Scene: Emma accepts a waltz from the Viscount. | Present: Emma, Viscount]\nText of passage 1.");
    }

    [Fact]
    public void APassageOfSeveralScenes_ListsThemAll_AndEachCharacterOnce()
    {
        var passage = Passage(1, new PassageScene("The ball.", ["Emma", "Charles"]), new PassageScene("The window.", ["emma"]));

        PassageHeader.TextToVectorize(passage).ShouldStartWith("[Scene: The ball. / The window. | Present: Emma, Charles]\n");
    }

    [Fact]
    public void TheSceneOfAHeader_IsCutAtTwoHundredCharacters_OnAWholeWord()
    {
        var passage = Passage(1, new PassageScene(string.Concat(Enumerable.Repeat("waltz ", 100)), []));

        var header = PassageHeader.TextToVectorize(passage).Split('\n')[0];

        header.ShouldEndWith("waltz]");
        header.Length.ShouldBeInRange(190, 200 + "[Scene: ]".Length);
        header.ShouldNotContain("Present");
    }

    // ---- Indexing ----

    [Fact]
    public async Task Indexing_VectorizesThePassagesByGroups_AndKeepsTheVectorsWithTheNameOfTheModel()
    {
        var book = await DoneBookAsync(5);

        await _service.IndexAsync(book.Id, Token);

        _embeddings.Sent.Select(texts => texts.Count).ShouldBe([2, 2, 1]);
        _embeddings.Sent.SelectMany(texts => texts).ShouldBe(Enumerable.Range(1, 5).Select(number => $"Text of passage {number}."));
        _embeddings.Created.ShouldHaveSingleItem().Retry.ShouldBeTrue();
        _index.Vectors[Key(book)].Model.ShouldBe("bge-m3");
        _index.Vectors[Key(book)].Vectors.Count.ShouldBe(5);
        (book.IndexDone, book.IndexTotal, book.IndexErrorCode).ShouldBe((0, 0, null)); // Nothing is running any more.
        book.Status.ShouldBe(BookStatus.Done);
    }

    [Fact]
    public async Task WhileIndexing_TheBookSaysHowFarItIs()
    {
        var book = await DoneBookAsync(5);
        var seen = new List<(int Done, int Total)>();
        var reply = _embeddings.Reply;
        _embeddings.Reply = (texts, token) =>
        {
            seen.Add((book.IndexDone, book.IndexTotal));
            return reply(texts, token);
        };

        await _service.IndexAsync(book.Id, Token);

        seen.ShouldBe([(0, 5), (2, 5), (4, 5)]);
    }

    [Fact]
    public async Task ASheetEditedWhileTheBookIsIndexed_IsKept()
    {
        var book = await DoneBookAsync();
        var reply = _embeddings.Reply;
        _embeddings.Reply = async (texts, token) =>
        {
            await _service.UpdateCharacterAsync(book.Id, 1, new CharacterDraft("Argan", "Edited meanwhile.", "I am Argan."), token);
            return await reply(texts, token);
        };

        await _service.IndexAsync(book.Id, Token);

        _books.Books[book.Id].Characters[0].ShortDescription.ShouldBe("Edited meanwhile.");
    }

    [Theory]
    [InlineData(ChatFailureKind.Unreachable, null, IndexFailure.ModelUnreachable)]
    [InlineData(ChatFailureKind.TimedOut, null, IndexFailure.ModelUnreachable)]
    [InlineData(ChatFailureKind.Http, 401, IndexFailure.KeyRefused)]
    [InlineData(ChatFailureKind.Http, 403, IndexFailure.KeyRefused)]
    [InlineData(ChatFailureKind.Http, 400, IndexFailure.CannotVectorize)]
    [InlineData(ChatFailureKind.Http, 404, IndexFailure.CannotVectorize)]
    [InlineData(ChatFailureKind.Http, 501, IndexFailure.CannotVectorize)]
    [InlineData(ChatFailureKind.Http, 429, IndexFailure.ProviderError)]
    [InlineData(ChatFailureKind.Http, 500, IndexFailure.ProviderError)]
    [InlineData(ChatFailureKind.InvalidResponse, null, IndexFailure.InvalidReply)]
    [InlineData(ChatFailureKind.StreamCut, null, IndexFailure.InvalidReply)]
    public async Task AModelThatFails_LeavesThePassagesWithoutVectors_AndTheBookSaysWhy(ChatFailureKind kind, int? status, IndexFailure expected)
    {
        var book = await DoneBookAsync();
        _embeddings.Reply = (_, _) => throw new ChatProviderException(kind, "failed", status);

        await _service.IndexAsync(book.Id, Token);

        book.IndexErrorCode.ShouldBe(expected.ToString());
        (book.IndexDone, book.IndexTotal).ShouldBe((0, 0));
        _index.Vectors.ShouldBeEmpty();
        _index.Books[Key(book)].Count.ShouldBe(3);
        book.Status.ShouldBe(BookStatus.Done);
    }

    [Fact]
    public async Task VectorsThatDoNotFit_OrAnAddressThatCannotBeUsed_AreFailuresToo()
    {
        var sizes = await DoneBookAsync();
        var call = 0;
        _embeddings.Reply = (texts, _) => Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(_ => new float[++call > 2 ? 3 : 2])]);

        await _service.IndexAsync(sizes.Id, Token);

        sizes.IndexErrorCode.ShouldBe(nameof(IndexFailure.InvalidReply));
        var address = await DoneBookAsync();
        _embeddings.CreateFailure = new ArgumentException("bad address");

        await _service.IndexAsync(address.Id, Token);

        address.IndexErrorCode.ShouldBe(nameof(IndexFailure.ModelUnreachable));
    }

    [Fact]
    public async Task AnIndexThatCannotKeepTheVectors_IsSaid()
    {
        var book = await DoneBookAsync();
        _embeddings.Reply = (texts, _) =>
        {
            _index.Failure = new PassageIndexException("disk full");
            return Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(_ => new float[] { 1, 0 })]);
        };

        await _service.IndexAsync(book.Id, Token);

        book.IndexErrorCode.ShouldBe(nameof(IndexFailure.IndexUnusable));
    }

    [Fact]
    public async Task WithoutAModelForVectorization_NothingIsSent()
    {
        var book = await DoneBookAsync();
        Choose(null);

        await _service.IndexAsync(book.Id, Token);

        _embeddings.Sent.ShouldBeEmpty();
        book.IndexErrorCode.ShouldBe(nameof(IndexFailure.NoModel));
    }

    [Fact]
    public async Task ACancelledIndexing_KeepsTheEarlierVectors()
    {
        var book = await DoneBookAsync();
        await _service.IndexAsync(book.Id, Token);
        Choose("other-model");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        _embeddings.Reply = async (_, token) =>
        {
            await cancel.CancelAsync();
            token.ThrowIfCancellationRequested();
            return [];
        };

        await Should.ThrowAsync<OperationCanceledException>(() => _service.IndexAsync(book.Id, cancel.Token));

        _index.Vectors[Key(book)].Model.ShouldBe("bge-m3");
    }

    [Fact]
    public async Task TheTokensOfTheVectors_AreCounted_EvenWhenTheIndexingFailsInTheMiddle()
    {
        var book = await DoneBookAsync(5);
        _embeddings.TokensPerText = 10;
        var calls = 0;
        var reply = _embeddings.Reply;
        _embeddings.Reply = (texts, token) => ++calls == 3 ? throw Http(500) : reply(texts, token);

        await _service.IndexAsync(book.Id, Token);

        book.EmbeddingTokens.ShouldBe(40);
    }

    // ---- At the end of an analysis ----

    private async Task<Book> AnalyzeAsync()
    {
        var book = new Book(BookId.New(), "The Lighthouse", "lighthouse.pdf", Now);
        _books.Books[book.Id] = book;
        await _texts.SaveAsync(book.Id, "Page one text.\fPage two text.\fPage three text.", Token);
        var analyzer = new BookAnalyzer(
            _books, _texts, _steps, _settings, _model, new KeyedPromptProvider(), new ManualTimeProvider(Now), _options, _index, _indexer);
        await analyzer.AnalyzeAsync(book.Id, Token);
        return book;
    }

    [Fact]
    public async Task AnAnalysis_EndsByIndexingThePassages_WhileTheBookIsStillRunning()
    {
        var seen = new List<(BookStatus Status, int Done, int Total)>();
        var reply = _embeddings.Reply;
        _embeddings.TokensPerText = 10;
        _embeddings.Reply = (texts, token) =>
        {
            var running = _books.Books.Values.Single();
            seen.Add((running.Status, running.IndexDone, running.IndexTotal));
            return reply(texts, token);
        };

        var book = await AnalyzeAsync();

        seen.ShouldBe([(BookStatus.Running, 0, 3), (BookStatus.Running, 2, 3)]);
        book.Status.ShouldBe(BookStatus.Done);
        _index.Vectors[Key(book)].Vectors.Count.ShouldBe(3);
        book.EmbeddingTokens.ShouldBe(30);
        (book.IndexDone, book.IndexTotal, book.IndexErrorCode).ShouldBe((0, 0, null));
    }

    [Fact]
    public async Task AnIndexingThatFails_DoesNotFailTheAnalysis_ThePassagesStay()
    {
        _embeddings.Reply = (_, _) => throw new ChatProviderException(ChatFailureKind.Unreachable, "down");

        var book = await AnalyzeAsync();

        book.Status.ShouldBe(BookStatus.Done);
        book.IndexErrorCode.ShouldBe(nameof(IndexFailure.ModelUnreachable));
        _index.Books[Key(book)].Count.ShouldBe(3);
        (await _texts.ReadAsync(book.Id, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task PassagesThatCannotBeSaved_AreSaidOnTheBook()
    {
        _index.Failure = new PassageIndexException("disk full");

        var book = await AnalyzeAsync();

        book.Status.ShouldBe(BookStatus.Done);
        book.IndexErrorCode.ShouldBe(nameof(IndexFailure.IndexUnusable));
        _embeddings.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithoutAModelForVectorization_AnAnalysisIndexesNothing()
    {
        Choose(null);

        var book = await AnalyzeAsync();

        _embeddings.Sent.ShouldBeEmpty();
        book.IndexErrorCode.ShouldBeNull();
        book.EmbeddingTokens.ShouldBe(0);
    }

    // ---- What a book says about its passages ----

    [Fact]
    public async Task ABook_SaysNothingAboutPassages_WhenTheFeatureIsOff()
    {
        var book = await DoneBookAsync();
        Choose(null);
        (await ViewAsync(book)).ShouldBeNull();

        Choose("bge-m3");
        _index.Problem = PassageIndexProblem.FileUnusable;
        (await ViewAsync(book)).ShouldBeNull();
    }

    [Fact]
    public async Task ABook_SaysWhereItsPassagesAre()
    {
        var without = await DoneBookAsync(passages: 0);
        var book = await DoneBookAsync();

        (await ViewAsync(without))!.State.ShouldBe(PassagesState.Missing);
        (await ViewAsync(book)).ShouldBe(new PassagesView(PassagesState.NotIndexed, 3, null, null, 0, 0));

        book.IndexErrorCode = nameof(IndexFailure.KeyRefused);
        (await ViewAsync(book))!.Failure.ShouldBe(IndexFailure.KeyRefused);

        await _service.IndexAsync(book.Id, Token);
        (await ViewAsync(book)).ShouldBe(new PassagesView(PassagesState.Indexed, 3, "bge-m3", null, 0, 0));
    }

    [Theory]
    [InlineData("bge-m3", PassagesState.Indexed)]
    [InlineData("BGE-M3:latest", PassagesState.Indexed)]
    [InlineData("bge-m3:567m", PassagesState.Outdated)]
    [InlineData("text-embedding-3-small", PassagesState.Outdated)]
    public async Task APassagesIndexedWithAnotherModel_AreToBeIndexedAgain(string model, PassagesState expected)
    {
        var book = await DoneBookAsync();
        await _service.IndexAsync(book.Id, Token);

        Choose(model);

        var view = (await ViewAsync(book)).ShouldNotBeNull();
        view.State.ShouldBe(expected);
        view.Model.ShouldBe("bge-m3");
    }

    [Fact]
    public async Task AQueuedBook_SaysItIsBeingIndexed()
    {
        var book = await DoneBookAsync();
        _scheduler.EnqueueIndexing(book.Id);
        (book.IndexDone, book.IndexTotal) = (2, 3);

        (await ViewAsync(book)).ShouldBe(new PassagesView(PassagesState.Indexing, 3, null, null, 2, 3));
    }

    // ---- Asking for it ----

    [Fact]
    public async Task Reindex_QueuesABookWhosePassagesHaveNoVectors_AndForgetsTheEarlierFailure()
    {
        var book = await DoneBookAsync();
        book.IndexErrorCode = nameof(IndexFailure.ModelUnreachable);

        (await _service.ReindexAsync(book.Id, Token)).ShouldBeTrue();

        _scheduler.Indexing.ShouldBe([book.Id]);
        book.IndexErrorCode.ShouldBeNull();
        (book.IndexDone, book.IndexTotal).ShouldBe((0, 3));
        (await _service.ReindexAsync(book.Id, Token)).ShouldBeFalse(); // Already queued.
    }

    [Fact]
    public async Task Reindex_QueuesABookIndexedWithAnotherModel_ButNotOneThatIsUpToDate()
    {
        var book = await DoneBookAsync();
        await _service.IndexAsync(book.Id, Token);
        (await _service.ReindexAsync(book.Id, Token)).ShouldBeFalse();

        Choose("other-model");

        (await _service.ReindexAsync(book.Id, Token)).ShouldBeTrue();
    }

    [Fact]
    public async Task Reindex_IsRefused_WhenThereIsNothingToDo()
    {
        var without = await DoneBookAsync(passages: 0);
        var running = await DoneBookAsync();
        running.Status = BookStatus.Running;
        var off = await DoneBookAsync();

        (await _service.ReindexAsync(without.Id, Token)).ShouldBeFalse();
        (await _service.ReindexAsync(running.Id, Token)).ShouldBeFalse();
        (await _service.ReindexAsync(BookId.New(), Token)).ShouldBeFalse();
        Choose(null);
        (await _service.ReindexAsync(off.Id, Token)).ShouldBeFalse();
        _scheduler.Indexing.ShouldBeEmpty();
    }

    [Fact]
    public async Task Indexing_ABookThatIsGoneOrNotDone_DoesNothing()
    {
        var running = await DoneBookAsync();
        running.Status = BookStatus.Running;

        await _service.IndexAsync(running.Id, Token);
        await _service.IndexAsync(BookId.New(), Token);

        _embeddings.Sent.ShouldBeEmpty();
    }
}
