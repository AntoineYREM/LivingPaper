using LivingPaper.Application;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

// PAPER-88: vectors that came with a file wait to be checked, vectors can be read back to be exported, and an index made by
// the version before is brought up to date without losing anything.
public sealed class SqliteImportedVectorsTests : IDisposable
{
    private static readonly BookKey Bovary = new(BookShelf.Library, "madame-bovary");

    private readonly TempDirectory _temp = new();
    private readonly SqlitePassageIndex _index;

    public SqliteImportedVectorsTests()
    {
        Directory.CreateDirectory(_temp.Path);
        _index = new SqlitePassageIndex(DatabasePath);
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DatabasePath => Path.Combine(_temp.Path, "rag.db");

    private Task SaveTwoPassagesAsync() =>
        _index.SavePassagesAsync(Bovary, [new(1, "east", [new PassageScene("A walk.", ["Emma"])]), new(2, "north", [])], Token);

    [Fact]
    public async Task VectorsFromAFile_AreKept_AndWaitToBeChecked()
    {
        await SaveTwoPassagesAsync();

        await _index.SaveUncheckedVectorsAsync(Bovary, "bge-m3", [[1, 0], [0, 1]], Token);

        (await _index.GetStateAsync(Bovary, Token)).ShouldBe(new PassageIndexState(2, "bge-m3", 2, Unchecked: true));
        var vectors = await _index.GetVectorsAsync(Bovary, Token);
        vectors.Count.ShouldBe(2);
        vectors[0].ShouldBe([1f, 0f]);
        vectors[1].ShouldBe([0f, 1f]);
    }

    [Fact]
    public async Task OnceChecked_TheyAreVectorsLikeAnyOther()
    {
        await SaveTwoPassagesAsync();
        await _index.SaveUncheckedVectorsAsync(Bovary, "bge-m3", [[1, 0], [0, 1]], Token);

        await _index.MarkCheckedAsync(Bovary, Token);

        (await _index.GetStateAsync(Bovary, Token)).ShouldBe(new PassageIndexState(2, "bge-m3", 2));
        (await _index.SearchAsync(Bovary, [0, 1], 1, Token)).ShouldHaveSingleItem().Passage.Text.ShouldBe("north");
    }

    [Fact]
    public async Task NewVectors_NeedNoCheck_AndNewPassagesForgetEverything()
    {
        await SaveTwoPassagesAsync();
        await _index.SaveUncheckedVectorsAsync(Bovary, "bge-m3", [[1, 0], [0, 1]], Token);

        await _index.SaveVectorsAsync(Bovary, "other", [[1, 1], [0, 1]], Token);
        (await _index.GetStateAsync(Bovary, Token)).ShouldBe(new PassageIndexState(2, "other", 2));

        await _index.SaveUncheckedVectorsAsync(Bovary, "bge-m3", [[1, 0], [0, 1]], Token);
        await SaveTwoPassagesAsync();
        (await _index.GetStateAsync(Bovary, Token)).ShouldBe(new PassageIndexState(2, null, 0));
        (await _index.GetVectorsAsync(Bovary, Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ABookWithoutVectorsOrUnknown_HasNoneToRead_AndNothingToCheck()
    {
        (await _index.GetVectorsAsync(Bovary, Token)).ShouldBeEmpty();
        await _index.MarkCheckedAsync(Bovary, Token);
        File.Exists(DatabasePath).ShouldBeFalse();

        await SaveTwoPassagesAsync();
        await _index.MarkCheckedAsync(Bovary, Token);

        (await _index.GetVectorsAsync(Bovary, Token)).ShouldBeEmpty();
        (await _index.GetStateAsync(Bovary, Token)).ShouldBe(new PassageIndexState(2, null, 0));
    }

    [Fact]
    public async Task UncheckedVectorsMustMatchThePassages_AsOthersMust()
    {
        await SaveTwoPassagesAsync();

        await Should.ThrowAsync<ArgumentException>(() => _index.SaveUncheckedVectorsAsync(Bovary, "bge-m3", [[1, 0]], Token));
        await Should.ThrowAsync<ArgumentException>(() => _index.SaveUncheckedVectorsAsync(Bovary, "bge-m3", [[1, 0], [0, 0]], Token));
    }

    [Fact]
    public async Task AnIndexOfTheVersionBefore_IsBroughtUpToDate_AndKeepsEverything()
    {
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync(Token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                create table book (shelf text not null, id text not null, model text, dimensions integer, primary key (shelf, id));
                create table passage (shelf text not null, id text not null, number integer not null, text text not null,
                    scenes text not null, vector blob, primary key (shelf, id, number));
                insert into book values ('library', 'madame-bovary', 'bge-m3', 2);
                insert into passage values ('library', 'madame-bovary', 1, 'east', '[]', x'0000803F00000000');
                pragma user_version = 1;
                """;
            await command.ExecuteNonQueryAsync(Token);
        }

        _index.Check().ShouldBeNull();
        (await _index.GetStateAsync(Bovary, Token)).ShouldBe(new PassageIndexState(1, "bge-m3", 2));

        (await _index.SearchAsync(Bovary, [1, 0], 1, Token)).ShouldHaveSingleItem().Passage.Text.ShouldBe("east");
        (await _index.GetVectorsAsync(Bovary, Token)).ShouldHaveSingleItem().ShouldBe([1f, 0f]);
        await _index.SaveUncheckedVectorsAsync(Bovary, "bge-m3", [[0, 1]], Token);
        (await _index.GetStateAsync(Bovary, Token))!.Unchecked.ShouldBeTrue();
        new SqlitePassageIndex(DatabasePath).Check().ShouldBeNull();
    }
}
