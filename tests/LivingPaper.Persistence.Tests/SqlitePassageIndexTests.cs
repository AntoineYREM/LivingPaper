using LivingPaper.Application;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

// The passages of the books and their vectors, in data/rag.db: a real SQLite file with the vector extension, in a temporary
// directory.
public sealed class SqlitePassageIndexTests : IDisposable
{
    private static readonly BookKey Bovary = new(BookShelf.Library, "madame-bovary");
    private static readonly BookKey Cyrano = new(BookShelf.Library, "cyrano-de-bergerac");
    private static readonly BookKey ExampleBovary = new(BookShelf.Examples, "madame-bovary");

    private readonly TempDirectory _temp = new();
    private readonly SqlitePassageIndex _index;

    public SqlitePassageIndexTests() => _index = new SqlitePassageIndex(DatabasePath);

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DatabasePath => Path.Combine(_temp.Path, "rag.db");

    private static StoredPassage Passage(int number, string? text = null) => new(number, text ?? $"Passage {number}", []);

    // Three passages whose vectors point east, north and north-east.
    private async Task IndexAsync(BookKey book, string prefix = "")
    {
        await _index.SavePassagesAsync(book, [Passage(1, prefix + "east"), Passage(2, prefix + "north"), Passage(3, prefix + "north-east")], Token);
        await _index.SaveVectorsAsync(book, "bge-m3", [[1, 0], [0, 1], [1, 1]], Token);
    }

    private async Task<IReadOnlyList<string>> SearchAsync(BookKey book, float[] query, int count = 3) =>
        [.. (await _index.SearchAsync(book, query, count, Token)).Select(found => found.Passage.Text)];

    // ---- Nothing yet ----

    [Fact]
    public async Task ABookThatWasNeverIndexed_HasNothing_AndAskingCreatesNoFile()
    {
        _index.Check().ShouldBeNull();
        (await _index.GetStateAsync(Bovary, Token)).ShouldBeNull();
        (await _index.GetPassagesAsync(Bovary, Token)).ShouldBeEmpty();
        (await _index.SearchAsync(Bovary, [1, 0], 3, Token)).ShouldBeEmpty();
        await _index.DeleteAsync(Bovary, Token);

        File.Exists(DatabasePath).ShouldBeFalse("a user who never chooses a model for vectorization gets no file");
    }

    // ---- Passages ----

    [Fact]
    public async Task SavedPassages_ComeBackInOrder_WithTheirScenes_AndHaveNoVectorsYet()
    {
        var ball = new PassageScene("At the ball, Emma accepts a waltz from the Viscount.", ["Emma Bovary", "Charles Bovary", "Viscount"]);
        var window = new PassageScene("Emma looks at the night.", ["Emma Bovary"]);
        const string Text = "« Elle savait valser, celle-là ! »\nIt's \"quoted\"; -- drop table passage; 字 \U0001F600";

        await _index.SavePassagesAsync(Bovary, [new(2, "Later", []), new(1, Text, [ball, window])], Token);

        var passages = await _index.GetPassagesAsync(Bovary, Token);
        passages.Select(passage => passage.Number).ShouldBe([1, 2]);
        passages[0].Text.ShouldBe(Text);
        passages[0].Scenes.Select(scene => scene.Summary).ShouldBe([ball.Summary, window.Summary]);
        passages[0].Scenes[0].Present.ShouldBe(ball.Present);
        passages[1].Scenes.ShouldBeEmpty();
        (await _index.GetStateAsync(Bovary, Token)).ShouldBe(new PassageIndexState(2, null, 0));
        (await _index.SearchAsync(Bovary, [1, 0], 3, Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ThePassages_SurviveARestart()
    {
        await IndexAsync(Bovary);

        var again = new SqlitePassageIndex(DatabasePath);

        again.Check().ShouldBeNull();
        (await again.GetStateAsync(Bovary, Token)).ShouldBe(new PassageIndexState(3, "bge-m3", 2));
        (await again.SearchAsync(Bovary, [1, 0], 1, Token)).ShouldHaveSingleItem().Passage.Text.ShouldBe("east");
    }

    [Fact]
    public async Task SavingPassagesAgain_ReplacesThem_AndForgetsTheVectors()
    {
        await IndexAsync(Bovary);

        await _index.SavePassagesAsync(Bovary, [Passage(1, "new")], Token);

        (await _index.GetPassagesAsync(Bovary, Token)).ShouldHaveSingleItem().Text.ShouldBe("new");
        (await _index.GetStateAsync(Bovary, Token)).ShouldBe(new PassageIndexState(1, null, 0));
    }

    [Fact]
    public async Task SavingNoPassage_LeavesTheBookWithNothing()
    {
        await IndexAsync(Bovary);

        await _index.SavePassagesAsync(Bovary, [], Token);

        (await _index.GetStateAsync(Bovary, Token)).ShouldBeNull();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    public async Task PassagesWithANumberThatCannotBeUsed_AreRefused_AndNothingChanges(int first, int second)
    {
        await IndexAsync(Bovary);

        await Should.ThrowAsync<ArgumentException>(() => _index.SavePassagesAsync(Bovary, [Passage(first), Passage(second)], Token));

        (await _index.GetStateAsync(Bovary, Token))!.Passages.ShouldBe(3);
    }

    // ---- Vectors and search ----

    [Fact]
    public async Task Search_GivesTheClosestPassagesFirst_WithTheirDistance()
    {
        await IndexAsync(Bovary);

        var found = await _index.SearchAsync(Bovary, [10, 1], 3, Token);

        found.Select(result => result.Passage.Number).ShouldBe([1, 3, 2]);
        found.Select(result => result.Distance).ShouldBeInOrder();
        found[0].Distance.ShouldBeInRange(0, 0.01);
        found[2].Distance.ShouldBeInRange(0.85, 0.95);
    }

    [Fact]
    public async Task Search_GivesNoMoreThanAsked_AndAllThereIsWhenAskedForMore()
    {
        await IndexAsync(Bovary);

        (await SearchAsync(Bovary, [0, 1], 1)).ShouldBe(["north"]);
        (await SearchAsync(Bovary, [0, 1], 50)).ShouldBe(["north", "north-east", "east"]);
    }

    [Fact]
    public async Task Search_LooksInOneBookOnly_AndKeepsAnExampleApartFromABookOfTheLibrary()
    {
        await IndexAsync(Bovary, "mine: ");
        await IndexAsync(Cyrano, "cyrano: ");
        await IndexAsync(ExampleBovary, "example: ");

        (await SearchAsync(Bovary, [1, 0])).ShouldAllBe(text => text.StartsWith("mine: ", StringComparison.Ordinal));
        (await SearchAsync(ExampleBovary, [1, 0])).ShouldAllBe(text => text.StartsWith("example: ", StringComparison.Ordinal));
        (await SearchAsync(Cyrano, [1, 0])).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Vectors_AreGivenToThePassagesInTheOrderOfTheirNumbers()
    {
        await _index.SavePassagesAsync(Bovary, [Passage(7, "seven"), Passage(3, "three")], Token);

        await _index.SaveVectorsAsync(Bovary, "bge-m3", [[1, 0], [0, 1]], Token);

        (await SearchAsync(Bovary, [1, 0], 1)).ShouldBe(["three"]);
    }

    [Fact]
    public async Task NewVectors_ReplaceTheOldOnes_AndCanComeFromAnotherModelWithAnotherSize()
    {
        await IndexAsync(Bovary);

        await _index.SaveVectorsAsync(Bovary, "other-model", [[0, 0, 1], [0, 1, 0], [1, 0, 0]], Token);

        (await _index.GetStateAsync(Bovary, Token)).ShouldBe(new PassageIndexState(3, "other-model", 3));
        (await SearchAsync(Bovary, [1, 0, 0], 1)).ShouldBe(["north-east"]);
    }

    public static TheoryData<float[][]> VectorsThatDoNotFit() => new()
    {
        Array.Empty<float[]>(),
        new float[][] { [1, 0], [0, 1] }, // One missing.
        new float[][] { [1, 0], [0, 1], [1, 1], [1, 1] },
        new float[][] { [1, 0], [0, 1], [1, 1, 1] },
        new float[][] { [1, 0], [0, 1], [] },
        new float[][] { [1, 0], [0, 1], [float.NaN, 1] },
        new float[][] { [1, 0], [0, 1], [float.PositiveInfinity, 1] },
        new float[][] { [1, 0], [0, 1], [0, 0] }, // No direction.
    };

    [Theory]
    [MemberData(nameof(VectorsThatDoNotFit))]
    public async Task VectorsThatDoNotFitThePassages_AreRefused_AndTheEarlierOnesStay(float[][] vectors)
    {
        await IndexAsync(Bovary);

        await Should.ThrowAsync<ArgumentException>(() => _index.SaveVectorsAsync(Bovary, "other-model", vectors, Token));

        (await _index.GetStateAsync(Bovary, Token)).ShouldBe(new PassageIndexState(3, "bge-m3", 2));
    }

    [Fact]
    public async Task Vectors_NeedPassagesAndTheNameOfTheirModel()
    {
        await Should.ThrowAsync<ArgumentException>(() => _index.SaveVectorsAsync(Bovary, "bge-m3", [[1, 0]], Token));
        await IndexAsync(Bovary);
        await Should.ThrowAsync<ArgumentException>(() => _index.SaveVectorsAsync(Bovary, " ", [[1, 0], [0, 1], [1, 1]], Token));
    }

    public static TheoryData<float[]> QueriesThatCannotBeSearched() => new()
    {
        Array.Empty<float>(),
        new float[] { 1, 0, 0 },
        new float[] { 0, 0 },
        new float[] { float.NaN, 1 },
    };

    [Theory]
    [MemberData(nameof(QueriesThatCannotBeSearched))]
    public async Task AVectorThatCannotBeCompared_IsAnError(float[] query)
    {
        await IndexAsync(Bovary);

        await Should.ThrowAsync<PassageIndexException>(() => _index.SearchAsync(Bovary, query, 3, Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Search_NeedsACountOfAtLeastOne(int count)
    {
        await IndexAsync(Bovary);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => _index.SearchAsync(Bovary, [1, 0], count, Token));
    }

    // ---- Deleting ----

    [Fact]
    public async Task Delete_RemovesThePassagesAndTheVectorsOfThatBookOnly()
    {
        await IndexAsync(Bovary);
        await IndexAsync(ExampleBovary);

        await _index.DeleteAsync(Bovary, Token);

        (await _index.GetStateAsync(Bovary, Token)).ShouldBeNull();
        (await _index.GetPassagesAsync(Bovary, Token)).ShouldBeEmpty();
        (await _index.GetStateAsync(ExampleBovary, Token)).ShouldNotBeNull().Passages.ShouldBe(3);
    }

    // ---- Several at the same time ----

    [Fact]
    public async Task BooksSavedAtTheSameTime_AreAllKept()
    {
        var books = Enumerable.Range(0, 8).Select(number => new BookKey(BookShelf.Library, $"book-{number}")).ToList();

        await Task.WhenAll(books.Select(book => IndexAsync(book)));

        foreach (var book in books)
        {
            (await _index.GetStateAsync(book, Token)).ShouldBe(new PassageIndexState(3, "bge-m3", 2));
        }
    }

    // ---- When it cannot be used ----

    [Fact]
    public async Task WithoutTheExtension_TheIndexSaysSo_AndRefusesEveryCall()
    {
        var index = new SqlitePassageIndex(DatabasePath, extension: "no-such-extension");

        index.Check().ShouldBe(PassageIndexProblem.ExtensionMissing);
        await Should.ThrowAsync<PassageIndexException>(() => index.SavePassagesAsync(Bovary, [Passage(1)], Token));
        await Should.ThrowAsync<PassageIndexException>(() => index.GetStateAsync(Bovary, Token));
        await Should.ThrowAsync<PassageIndexException>(() => index.SearchAsync(Bovary, [1, 0], 1, Token));
        File.Exists(DatabasePath).ShouldBeFalse();
    }

    [Fact]
    public async Task ADamagedFile_IsReportedAndLeftAlone()
    {
        Directory.CreateDirectory(_temp.Path);
        var garbage = new string('x', 8192);
        await File.WriteAllTextAsync(DatabasePath, garbage, Token);

        _index.Check().ShouldBe(PassageIndexProblem.FileUnusable);
        var exception = await Should.ThrowAsync<PassageIndexException>(() => _index.SavePassagesAsync(Bovary, [Passage(1, "secret text")], Token));

        exception.ToString().ShouldNotContain("secret text");
        (await File.ReadAllTextAsync(DatabasePath, Token)).ShouldBe(garbage);
    }

    [Fact]
    public async Task AFileWrittenByALaterVersion_IsReportedAndLeftAlone()
    {
        await IndexAsync(Bovary);
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync(Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99";
            await command.ExecuteNonQueryAsync(Token);
        }

        var before = await File.ReadAllBytesAsync(DatabasePath, Token);
        var index = new SqlitePassageIndex(DatabasePath);

        index.Check().ShouldBe(PassageIndexProblem.FileUnusable);
        await Should.ThrowAsync<PassageIndexException>(() => index.DeleteAsync(Bovary, Token));
        (await File.ReadAllBytesAsync(DatabasePath, Token)).ShouldBe(before);
    }

    [Fact]
    public async Task AFileThatIsNotOurDatabase_IsNotWrittenInto()
    {
        Directory.CreateDirectory(_temp.Path);
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync(Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "create table passage (something text)";
            await command.ExecuteNonQueryAsync(Token);
        }

        _index.Check().ShouldBe(PassageIndexProblem.FileUnusable);
    }

    [Fact]
    public async Task AnEmptyFile_IsUsedAsANewOne()
    {
        Directory.CreateDirectory(_temp.Path);
        await File.WriteAllBytesAsync(DatabasePath, [], Token);

        _index.Check().ShouldBeNull();
        await IndexAsync(Bovary);

        (await _index.GetStateAsync(Bovary, Token))!.Passages.ShouldBe(3);
    }

    [Fact]
    public async Task ACancelledCall_DoesNothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => _index.SavePassagesAsync(Bovary, [Passage(1)], cancelled.Token));

        (await _index.GetStateAsync(Bovary, Token)).ShouldBeNull();
    }
}
