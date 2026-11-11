using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

// The text of a book waiting for its analysis, in data/uploads/{id}.txt (PAPER-7).
public sealed class FileBookTextStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FileBookTextStore _store;
    private readonly BookId _id = BookId.New();

    public FileBookTextStoreTests() => _store = new FileBookTextStore(Path.Combine(_temp.Path, "uploads"));

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string FileOf(BookId id) => Path.Combine(_temp.Path, "uploads", id + ".txt");

    [Fact]
    public async Task Read_ReturnsNull_WhenThereIsNoText() =>
        (await _store.ReadAsync(_id, Token)).ShouldBeNull();

    [Fact]
    public async Task SaveThenRead_ReturnsTheTextExactly_AccentsAndPageBreaksIncluded()
    {
        const string text = "Chapitre un : l’été à Paris.\fPage deux — « fin ».\n";

        await _store.SaveAsync(_id, text, Token);

        (await _store.ReadAsync(_id, Token)).ShouldBe(text);
        File.Exists(FileOf(_id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Save_ReplacesTheEarlierText_AndLeavesNoTemporaryFile()
    {
        await _store.SaveAsync(_id, "first", Token);
        await _store.SaveAsync(_id, "second", Token);

        (await _store.ReadAsync(_id, Token)).ShouldBe("second");
        Directory.GetFiles(Path.Combine(_temp.Path, "uploads")).ShouldBe([FileOf(_id)]);
    }

    [Fact]
    public async Task Delete_RemovesTheText_AndDoingItAgainIsHarmless()
    {
        await _store.SaveAsync(_id, "text", Token);

        await _store.DeleteAsync(_id, Token);
        await _store.DeleteAsync(_id, Token);

        (await _store.ReadAsync(_id, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task Delete_WhenTheDirectoryDoesNotExistYet_DoesNothing() =>
        await Should.NotThrowAsync(() => _store.DeleteAsync(_id, Token));

    [Fact]
    public async Task TextsOfTwoBooks_AreKeptApart()
    {
        var other = BookId.New();
        await _store.SaveAsync(_id, "one", Token);
        await _store.SaveAsync(other, "two", Token);

        await _store.DeleteAsync(_id, Token);

        (await _store.ReadAsync(other, Token)).ShouldBe("two");
    }
}
