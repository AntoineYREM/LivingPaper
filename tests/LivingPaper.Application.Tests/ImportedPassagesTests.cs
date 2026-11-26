using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

// PAPER-88: the passages and vectors that travel with a book: exporting them, keeping those of an imported file or of a
// copied example, checking that the vectors are those of the model chosen before they are used, and indexing an example.
public sealed class ImportedPassagesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 20, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBookRepository _books = new();
    private readonly FakeBookArchive _archive = new();
    private readonly InMemoryLlmSettingsRepository _settingsRepository = new();
    private readonly FakeEmbeddingModelFactory _embeddings = new() { TokensPerText = 7 };
    private readonly FakePassageIndex _index = new();
    private readonly FakeAnalysisScheduler _scheduler = new();
    private readonly Book _example = Analyzed("an-example", "An Example");
    private readonly InMemoryExampleCatalog _examples;
    private readonly LlmSettingsService _settings;
    private readonly BookService _service;

    public ImportedPassagesTests()
    {
        Choose("bge-m3");
        _examples = new InMemoryExampleCatalog(_example);
        _settings = new LlmSettingsService(_settingsRepository, new FakeConnectionTester(), new FakeModelCatalog(), new PendingProviderStore(TimeProvider.System));
        var options = new AnalysisOptions { PassagesPerEmbeddingCall = 2 };
        _service = new BookService(
            _books, new InMemoryBookTextStore(), new FakeBookTextExtractor(), _scheduler, new FixedTimeProvider(Now), options, _examples, _archive,
            _index, new BookIndexer(_index, _settings, _embeddings, options));
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
        var providers = new List<LlmProvider> { new("p1", "Chat", "https://api.openai.com/v1", "sk-secret-key-123", "chat-model") };
        if (embeddingModel is not null)
        {
            providers.Add(new LlmProvider("e1", "Vectors", "http://localhost:11434/v1", string.Empty, embeddingModel));
        }

        _settingsRepository.Seed(LlmSettings.Restore(providers, "p1", "p1", embeddingProviderId: embeddingModel is null ? null : "e1"));
    }

    private static Book Analyzed(string id, string title)
    {
        BookId.TryParse(id, out var bookId).ShouldBeTrue();
        var book = new Book(bookId, title, "book.epub", Now) { Status = BookStatus.Done, UpdatedAt = Now };
        book.Characters.Add(new Character(1, "Argan", "The sick man.", "I am Argan."));
        return book;
    }

    private static BookKey Key(Book book) => new(BookShelf.Library, book.Id.ToString());

    private BookKey ExampleKey => new(BookShelf.Examples, _example.Id.ToString());

    // "Text of passage 1." has 18 letters: the fake model makes [18, 1] of it.
    private static float[] Made => [18f, 1f];

    private static PassageSet Set(float[]? first = null, string? model = "bge-m3", int passages = 3) => new(
        [.. Enumerable.Range(1, passages).Select(number => new StoredPassage(number, $"Text of passage {number}.", []))],
        model,
        model is null ? [] : [.. Enumerable.Range(1, passages).Select(number => number == 1 ? first ?? Made : [number, 2f])]);

    private async Task<ImportBookResult> ImportAsync(Book book, PassageSet? set, bool replace = false)
    {
        var file = set is null ? _archive.Export(book) : _archive.ExportWithPassages(book, set).Content;
        return await _service.ImportAsync(new MemoryStream(file), replace, Token);
    }

    private async Task<Book> ImportedAsync(PassageSet set)
    {
        var result = await ImportAsync(Analyzed("the-lighthouse", "The Lighthouse"), set);
        result.Imported.ShouldNotBeNull(result.Detail);
        return _books.Books[result.Imported.Id];
    }

    private async Task<Book> InLibraryAsync(PassageSet? set = null)
    {
        var book = Analyzed("mine", "Mine");
        _books.Books[book.Id] = book;
        if (set is not null)
        {
            await _index.SavePassagesAsync(Key(book), set.Passages, Token);
            if (set.EmbeddingModel is not null)
            {
                await _index.SaveVectorsAsync(Key(book), set.EmbeddingModel, set.Vectors, Token);
            }
        }

        return book;
    }

    private int TextsVectorized => _embeddings.Sent.Sum(texts => texts.Count);

    // ---- Importing ----

    [Fact]
    public async Task AnImport_KeepsThePassagesAndVectors_AsNotChecked_AndQueuesTheCheck()
    {
        var result = await ImportAsync(Analyzed("the-lighthouse", "The Lighthouse"), Set());

        var key = Key(result.Imported!);
        result.PassagesDropped.ShouldBeFalse();
        _index.Books[key].Count.ShouldBe(3);
        _index.Vectors[key].Model.ShouldBe("bge-m3");
        _index.Unchecked.ShouldContain(key);
        _scheduler.Checks.ShouldBe([result.Imported!.Id]);
        TextsVectorized.ShouldBe(0); // Nothing is asked of the model while the user waits.
    }

    [Fact]
    public async Task AnImportWithoutAModelForVectorization_KeepsThemToo()
    {
        Choose(null);

        var book = await ImportedAsync(Set());

        _index.Unchecked.ShouldContain(Key(book));
        (await _service.DescribePassagesAsync(book, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task AnImportOfPassagesWithoutVectors_KeepsThem_WithNothingToCheck()
    {
        var book = await ImportedAsync(Set(model: null));

        _index.Books[Key(book)].Count.ShouldBe(3);
        _index.Vectors.ShouldBeEmpty();
        _scheduler.Checks.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnImportWithoutPassages_RemovesThoseLeftUnderItsAddress()
    {
        var leftOver = new BookKey(BookShelf.Library, "the-lighthouse");
        await _index.SavePassagesAsync(leftOver, Set().Passages, Token);

        var result = await ImportAsync(Analyzed("the-lighthouse", "The Lighthouse"), null);

        result.PassagesDropped.ShouldBeFalse();
        _index.Books.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenTheIndexCannotBeUsed_TheBookIsImportedWithoutItsPassages_AndTheUserIsTold(bool extensionMissing)
    {
        if (extensionMissing)
        {
            _index.Problem = PassageIndexProblem.ExtensionMissing;
        }
        else
        {
            _index.Failure = new PassageIndexException("The file is damaged.");
        }

        var result = await ImportAsync(Analyzed("the-lighthouse", "The Lighthouse"), Set());

        result.Imported.ShouldNotBeNull();
        result.PassagesDropped.ShouldBeTrue();
        _books.Books.ShouldHaveSingleItem();
        _index.Books.ShouldBeEmpty();
        _scheduler.Checks.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnImportThatTakesAnotherAddress_KeepsItsPassagesUnderThatOne()
    {
        var result = await ImportAsync(Analyzed("an-example", "An Example"), Set());

        result.Imported!.Id.ShouldNotBe(_example.Id);
        _index.Books.Keys.ShouldBe([Key(result.Imported)]);
    }

    // ---- Checking the vectors of a file ----

    // The check as the queue runs it: the book leaves the queue when it ends.
    private async Task CheckAsync(Book book)
    {
        await _service.CheckVectorsAsync(book.Id, Token);
        await _scheduler.CancelAsync(book.Id, Token);
    }

    [Fact]
    public async Task TheSameVectors_AreChecked_WithOnePassage_AndNothingNewIsComputed()
    {
        var book = await ImportedAsync(Set(first: [36f, 2f])); // The same direction as [18, 1].

        await CheckAsync(book);

        _embeddings.Sent.ShouldHaveSingleItem().ShouldBe(["Text of passage 1."]);
        _index.Unchecked.ShouldBeEmpty();
        _index.Vectors[Key(book)].Vectors[0].ShouldBe([36f, 2f]);
        (_books.Books[book.Id].IndexErrorCode, _books.Books[book.Id].EmbeddingTokens).ShouldBe((null, 7));
        (await _service.DescribePassagesAsync(book, Token))!.State.ShouldBe(PassagesState.Indexed);
    }

    [Fact]
    public async Task ThePassageOfTheCheck_IsVectorizedUnderItsHeader()
    {
        var set = new PassageSet(
            [new StoredPassage(1, "Text.", [new PassageScene("A walk.", ["Emma"])])], "bge-m3", [[1f, 1f]]);
        var book = await ImportedAsync(set);

        await CheckAsync(book);

        _embeddings.Sent.ShouldHaveSingleItem().ShouldBe(["[Scene: A walk. | Present: Emma]\nText."]);
    }

    [Theory]
    [InlineData(1f, 18f)] // Another direction.
    [InlineData(18f, 4f)] // Close, but not close enough.
    public async Task OtherVectors_StayUnused_AndTheBookIsToBeIndexedAgain(float first, float second)
    {
        var book = await ImportedAsync(Set(first: [first, second]));

        await CheckAsync(book);

        _index.Unchecked.ShouldContain(Key(book));
        TextsVectorized.ShouldBe(1);
        _books.Books[book.Id].IndexErrorCode.ShouldBe(nameof(IndexFailure.VectorsDiffer));
        var view = (await _service.DescribePassagesAsync(_books.Books[book.Id], Token))!;
        (view.State, view.Failure).ShouldBe((PassagesState.Unchecked, IndexFailure.VectorsDiffer));
    }

    [Fact]
    public async Task VectorsOfAnotherSize_AreNotThoseOfTheModel()
    {
        var set = new PassageSet([new StoredPassage(1, "Text of passage 1.", [])], "bge-m3", [[18f, 1f, 0f]]);
        var book = await ImportedAsync(set);

        await CheckAsync(book);

        _books.Books[book.Id].IndexErrorCode.ShouldBe(nameof(IndexFailure.VectorsDiffer));
    }

    [Fact]
    public async Task TheSameModelUnderItsLatestTag_IsChecked()
    {
        var book = await ImportedAsync(Set(model: "BGE-M3:latest"));

        await CheckAsync(book);

        _index.Unchecked.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("other-model")]
    [InlineData(null)]
    public async Task WithAnotherModelOrNone_NothingIsAskedNorRecorded(string? chosen)
    {
        var book = await ImportedAsync(Set());
        Choose(chosen);

        await CheckAsync(book);

        TextsVectorized.ShouldBe(0);
        _index.Unchecked.ShouldContain(Key(book));
        _books.Books[book.Id].IndexErrorCode.ShouldBeNull();
        if (chosen is not null)
        {
            (await _service.DescribePassagesAsync(book, Token))!.State.ShouldBe(PassagesState.Outdated);
        }
    }

    [Fact]
    public async Task AModelThatCannotBeReached_LeavesTheVectorsToBeChecked_AndSaysWhy()
    {
        var book = await ImportedAsync(Set());
        _embeddings.Reply = (_, _) => Task.FromException<IReadOnlyList<float[]>>(new ChatProviderException(ChatFailureKind.Unreachable, "down"));

        await CheckAsync(book);

        _index.Unchecked.ShouldContain(Key(book));
        var view = (await _service.DescribePassagesAsync(_books.Books[book.Id], Token))!;
        (view.State, view.Failure).ShouldBe((PassagesState.Unchecked, IndexFailure.ModelUnreachable));
    }

    [Fact]
    public async Task VectorsAlreadyChecked_AreNotCheckedAgain()
    {
        var book = await InLibraryAsync(Set());

        await CheckAsync(book);

        TextsVectorized.ShouldBe(0);
    }

    // ---- Indexing a book whose vectors came with a file ----

    [Fact]
    public async Task Indexing_ReusesTheVectorsOfTheFile_WhenTheyAreThoseOfTheModel()
    {
        var book = await ImportedAsync(Set());

        await _service.IndexAsync(book.Id, Token);

        TextsVectorized.ShouldBe(1);
        _index.Unchecked.ShouldBeEmpty();
        _index.Vectors[Key(book)].Vectors[1].ShouldBe([2f, 2f]);
    }

    [Fact]
    public async Task Indexing_ComputesNewVectors_WhenThoseOfTheFileDiffer()
    {
        var book = await ImportedAsync(Set(first: [1f, 18f]));

        await _service.IndexAsync(book.Id, Token);

        _embeddings.Sent.Select(texts => texts.Count).ShouldBe([1, 2, 1]);
        _index.Unchecked.ShouldBeEmpty();
        _index.Vectors[Key(book)].Vectors[0].ShouldBe(Made);
        _books.Books[book.Id].IndexErrorCode.ShouldBeNull();
    }

    [Fact]
    public async Task Indexing_WithAnotherModel_ComputesNewVectors_WithoutAnyCheck()
    {
        var book = await ImportedAsync(Set());
        Choose("other-model");

        await _service.IndexAsync(book.Id, Token);

        _embeddings.Sent.Select(texts => texts.Count).ShouldBe([2, 1]);
        _index.Vectors[Key(book)].Model.ShouldBe("other-model");
        _index.Unchecked.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUncheckedBook_CanBeAskedToBeIndexed()
    {
        var book = await ImportedAsync(Set());
        await _scheduler.CancelAsync(book.Id, Token); // The check that was queued is gone, as after a restart.

        (await _service.ReindexAsync(book.Id, Token)).ShouldBeTrue();

        _scheduler.Indexing.ShouldBe([book.Id]);
    }

    [Fact]
    public async Task WhileTheCheckIsQueued_TheBookSaysItIsChecking()
    {
        var book = await ImportedAsync(Set());

        (await _service.DescribePassagesAsync(book, Token))!.State.ShouldBe(PassagesState.Checking);
    }

    // ---- Exporting ----

    [Fact]
    public async Task AnExport_HoldsThePassagesAndVectorsOfTheBook()
    {
        var book = await InLibraryAsync(Set());

        var file = (await _service.ExportAsync(book.Id, Token))!;

        var set = _archive.ExportedPassages.ShouldHaveSingleItem();
        set.EmbeddingModel.ShouldBe("bge-m3");
        set.Passages.Count.ShouldBe(3);
        set.Vectors.Count.ShouldBe(3);
        (file.FileName, file.Omitted).ShouldBe(("mine.json", ExportOmission.None));
    }

    [Fact]
    public async Task ExportThenImportWithTheSameModel_ComputesNoNewVector()
    {
        var book = await InLibraryAsync(Set());
        var file = (await _service.ExportAsync(book.Id, Token))!;
        await _service.DeleteAsync(book.Id, Token);

        var imported = (await _service.ImportAsync(new MemoryStream(file.Content), false, Token)).Imported!;
        await _scheduler.CancelAsync(imported.Id, Token);
        await _service.CheckVectorsAsync(imported.Id, Token);

        TextsVectorized.ShouldBe(1); // The passage of the check, and nothing else.
        _index.Vectors[Key(imported)].Vectors.Select(vector => vector[0]).ShouldBe([18f, 2f, 3f]);
        _index.Unchecked.ShouldBeEmpty();
    }

    [Fact]
    public async Task ABookWithPassagesAndNoVectors_ExportsItsPassages()
    {
        var book = await InLibraryAsync(Set(model: null));

        await _service.ExportAsync(book.Id, Token);

        var set = _archive.ExportedPassages.ShouldHaveSingleItem();
        (set.Passages.Count, set.EmbeddingModel, set.Vectors.Count).ShouldBe((3, null, 0));
    }

    [Theory]
    [InlineData("no passages")]
    [InlineData("extension missing")]
    [InlineData("damaged")]
    public async Task WithoutPassagesToGive_TheExportIsTheBookAlone(string situation)
    {
        var book = await InLibraryAsync(situation == "no passages" ? null : Set());
        _index.Problem = situation == "extension missing" ? PassageIndexProblem.ExtensionMissing : null;
        _index.Failure = situation == "damaged" ? new PassageIndexException("damaged") : null;

        (await _service.ExportAsync(book.Id, Token)).ShouldNotBeNull();

        _archive.ExportedPassages.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheExportOfAnExample_HoldsThePassagesItIsShippedWith()
    {
        _examples.Passages[_example.Id] = Set();

        await _service.ExportAsync(_example.Id, Token);

        _archive.ExportedPassages.ShouldHaveSingleItem().ShouldBe(_examples.Passages[_example.Id]);
    }

    [Fact]
    public async Task WhatTheArchiveLeftOut_IsTold()
    {
        var book = await InLibraryAsync(Set());
        _archive.Omission = ExportOmission.Vectors;

        (await _service.ExportAsync(book.Id, Token))!.Omitted.ShouldBe(ExportOmission.Vectors);
    }

    [Fact]
    public async Task ThePageOfABook_KnowsWhetherItsExportHoldsText()
    {
        var without = await InLibraryAsync();
        (await _service.DescribeExportAsync(without.Id, Token)).ShouldBe(new ExportDescription(false, ExportOmission.None));
        (await _service.HoldsPassagesAsync(without.Id, Token)).ShouldBeFalse();

        await _index.SavePassagesAsync(Key(without), Set().Passages, Token);

        (await _service.DescribeExportAsync(without.Id, Token)).ShouldBe(new ExportDescription(true, ExportOmission.None));
        (await _service.HoldsPassagesAsync(without.Id, Token)).ShouldBeTrue();
        _archive.ExportedPassages.ShouldBeEmpty(); // A small book needs no file to be built to say so.
    }

    [Fact]
    public async Task ForABookThatMayNotFit_TheExportIsTriedToSayWhatIsLeftOut()
    {
        var book = await InLibraryAsync(Set(passages: 2000));
        _archive.Omission = ExportOmission.Vectors;

        (await _service.DescribeExportAsync(book.Id, Token)).ShouldBe(new ExportDescription(true, ExportOmission.Vectors));

        _archive.ExportedPassages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AnExampleWithPassages_SaysItsExportHoldsText()
    {
        _examples.Passages[_example.Id] = Set();

        (await _service.DescribeExportAsync(_example.Id, Token)).HasPassages.ShouldBeTrue();
        (await _service.HoldsPassagesAsync(_example.Id, Token)).ShouldBeTrue();
    }

    // ---- Copying an example into the library ----

    [Fact]
    public async Task ACopyOfAnExample_KeepsItsPassages_WithVectorsToBeChecked()
    {
        _examples.Passages[_example.Id] = Set();

        var result = await _service.AddExampleAsync(_example.Id, Token);

        var key = Key(result.Added!);
        result.PassagesDropped.ShouldBeFalse();
        _index.Books[key].Count.ShouldBe(3);
        _index.Unchecked.ShouldContain(key);
        _scheduler.Checks.ShouldBe([result.Added!.Id]);
    }

    [Fact]
    public async Task ACopyOfAnIndexedExample_TakesItsVectors_WithNothingToCheck()
    {
        _examples.Passages[_example.Id] = Set(first: [1f, 18f]);
        await _service.IndexExampleAsync(_example.Id, Token); // New vectors: those shipped are not those of the model.

        var result = await _service.AddExampleAsync(_example.Id, Token);

        var key = Key(result.Added!);
        _index.Vectors[key].Vectors[0].ShouldBe(Made);
        _index.Unchecked.ShouldBeEmpty();
        _scheduler.Checks.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACopyThatCannotKeepThePassages_SaysSo()
    {
        _examples.Passages[_example.Id] = Set();
        _index.Problem = PassageIndexProblem.ExtensionMissing;

        var result = await _service.AddExampleAsync(_example.Id, Token);

        result.Succeeded.ShouldBeTrue();
        result.PassagesDropped.ShouldBeTrue();
    }

    [Fact]
    public async Task ACopyOfAnExampleWithoutPassages_HasNone()
    {
        var result = await _service.AddExampleAsync(_example.Id, Token);

        result.PassagesDropped.ShouldBeFalse();
        _index.Books.ShouldBeEmpty();
    }

    // ---- The index of an example ----

    private Task<PassagesView?> ExampleViewAsync() => _service.DescribeExamplePassagesAsync(_example, Token);

    [Fact]
    public async Task AnExampleWithPassages_OffersToBeIndexed_OnceAModelIsChosen()
    {
        (await ExampleViewAsync()).ShouldBeNull(); // No passages.
        _examples.Passages[_example.Id] = Set();

        (await ExampleViewAsync()).ShouldBe(new PassagesView(PassagesState.NotIndexed, 3, null, null, 0, 0));

        Choose(null);
        (await ExampleViewAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task AskingToIndexAnExample_QueuesIt_ApartFromTheLibrary()
    {
        _examples.Passages[_example.Id] = Set();

        (await _service.ReindexAsync(_example.Id, Token)).ShouldBeTrue();
        (await _service.ReindexAsync(_example.Id, Token)).ShouldBeFalse(); // Already queued.

        _scheduler.IndexingExamples.ShouldBe([_example.Id]);
        _scheduler.Indexing.ShouldBeEmpty();
        var view = (await ExampleViewAsync())!;
        (view.State, view.Passages, view.Total).ShouldBe((PassagesState.Indexing, 3, 3));
    }

    [Fact]
    public async Task AnExampleWithoutPassages_CannotBeIndexed()
    {
        (await _service.ReindexAsync(_example.Id, Token)).ShouldBeFalse();
    }

    [Fact]
    public async Task IndexingAnExample_ReusesTheVectorsItIsShippedWith()
    {
        _examples.Passages[_example.Id] = Set();

        await _service.IndexExampleAsync(_example.Id, Token);

        TextsVectorized.ShouldBe(1);
        _index.Books[ExampleKey].Count.ShouldBe(3);
        _index.Unchecked.ShouldBeEmpty();
        (await ExampleViewAsync()).ShouldBe(new PassagesView(PassagesState.Indexed, 3, "bge-m3", null, 0, 0));
        (await _service.ReindexAsync(_example.Id, Token)).ShouldBeFalse(); // Nothing left to do.
    }

    [Theory]
    [InlineData("other vectors")]
    [InlineData("no vectors")]
    [InlineData("other model")]
    public async Task IndexingAnExample_ComputesVectors_WhenThoseShippedCannotServe(string shipped)
    {
        _examples.Passages[_example.Id] = shipped switch
        {
            "other vectors" => Set(first: [1f, 18f]),
            "no vectors" => Set(model: null),
            _ => Set(model: "other-model"),
        };

        await _service.IndexExampleAsync(_example.Id, Token);

        _index.Vectors[ExampleKey].Model.ShouldBe("bge-m3");
        _index.Vectors[ExampleKey].Vectors[0].ShouldBe(Made);
        (await ExampleViewAsync())!.State.ShouldBe(PassagesState.Indexed);
    }

    [Fact]
    public async Task AFailedIndexingOfAnExample_IsTold_AndTheExampleStaysAsItIs()
    {
        _examples.Passages[_example.Id] = Set(model: null);
        _embeddings.Reply = (_, _) => Task.FromException<IReadOnlyList<float[]>>(new ChatProviderException(ChatFailureKind.Http, "refused", 401));

        await _service.IndexExampleAsync(_example.Id, Token);

        var view = (await ExampleViewAsync())!;
        (view.State, view.Failure).ShouldBe((PassagesState.NotIndexed, IndexFailure.KeyRefused));
        (_example.IndexErrorCode, _example.EmbeddingTokens, _example.IndexTotal).ShouldBe((null, 0, 0));
        _books.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task ABookOfTheLibraryWithTheAddressOfAnExample_KeepsItsOwnPassages()
    {
        var mine = new BookKey(BookShelf.Library, _example.Id.ToString());
        await _index.SavePassagesAsync(mine, Set(passages: 1).Passages, Token);
        _examples.Passages[_example.Id] = Set();

        await _service.IndexExampleAsync(_example.Id, Token);

        _index.Books[mine].Count.ShouldBe(1);
        _index.Books[ExampleKey].Count.ShouldBe(3);
    }

    [Fact]
    public async Task AtTheStart_TheIndexOfAnExampleThatChanged_IsForgotten()
    {
        _examples.Passages[_example.Id] = Set();
        await _service.IndexExampleAsync(_example.Id, Token);

        await _service.RecoverAsync(Token);
        _index.Books.ShouldContainKey(ExampleKey);

        _examples.Passages[_example.Id] = new PassageSet([new StoredPassage(1, "A new text.", [])], null, []);
        await _service.RecoverAsync(Token);

        _index.Books.ShouldNotContainKey(ExampleKey);
    }

    [Theory]
    [InlineData("scenes")]
    [InlineData("gone")]
    public async Task AtTheStart_ADifferenceInTheScenes_OrNoPassageAnyMore_ForgetsTheIndexToo(string change)
    {
        _examples.Passages[_example.Id] = Set();
        await _service.IndexExampleAsync(_example.Id, Token);
        if (change == "gone")
        {
            _examples.Passages.Remove(_example.Id);
        }
        else
        {
            var set = Set();
            _examples.Passages[_example.Id] = set with { Passages = [.. set.Passages.Select(passage => passage with { Scenes = [new PassageScene("New.", ["Emma"])] })] };
        }

        await _service.RecoverAsync(Token);

        _index.Books.ShouldNotContainKey(ExampleKey);
    }
}
