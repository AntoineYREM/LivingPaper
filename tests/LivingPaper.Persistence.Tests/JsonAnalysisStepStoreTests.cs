using System.Text.Json;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

public sealed class JsonAnalysisStepStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly JsonAnalysisStepStore _store;
    private readonly BookId _bookId = BookId.New();

    public JsonAnalysisStepStoreTests() => _store = new JsonAnalysisStepStore(_temp.BooksPath);

    public void Dispose()
    {
        _store.Dispose();
        _temp.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string StepsFile => Path.Combine(_temp.BooksPath, _bookId + ".steps.json");

    [Fact]
    public async Task GetAll_ReturnsNothing_WhenNoStepWasSaved()
    {
        (await _store.GetAllAsync(_bookId, Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task SaveThenGetAll_ReturnsEveryStep()
    {
        await _store.SaveAsync(_bookId, "part:1", "notes on part 1", Token);
        await _store.SaveAsync(_bookId, "merge", "merged characters", Token);

        var steps = await _store.GetAllAsync(_bookId, Token);

        steps.Count.ShouldBe(2);
        steps["part:1"].ShouldBe("notes on part 1");
        steps["merge"].ShouldBe("merged characters");
    }

    [Fact]
    public async Task Save_ReplacesTheResultOfAStepSavedAgain()
    {
        await _store.SaveAsync(_bookId, "part:1", "first try", Token);
        await _store.SaveAsync(_bookId, "part:1", "second try", Token);

        (await _store.GetAllAsync(_bookId, Token))["part:1"].ShouldBe("second try");
    }

    [Fact]
    public async Task Save_WritesASchemaVersion_AndLeavesNoTemporaryFile()
    {
        await _store.SaveAsync(_bookId, "merge", "x", Token);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(StepsFile, Token));
        json.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        Directory.GetFiles(_temp.BooksPath).ShouldBe([StepsFile]);
    }

    [Fact]
    public async Task Steps_OfDifferentBooksDoNotMix()
    {
        var other = BookId.New();
        await _store.SaveAsync(_bookId, "merge", "mine", Token);
        await _store.SaveAsync(other, "merge", "theirs", Token);

        (await _store.GetAllAsync(_bookId, Token))["merge"].ShouldBe("mine");
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("{\"schemaVersion\": 999, \"steps\": {\"merge\": \"x\"}}")]
    public async Task GetAll_ReturnsNothing_WhenTheStepsFileCannotBeRead(string content)
    {
        Directory.CreateDirectory(_temp.BooksPath);
        await File.WriteAllTextAsync(StepsFile, content, Token);

        (await _store.GetAllAsync(_bookId, Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteAll_IsHarmless_WhenTheDirectoryDoesNotExistYet()
    {
        await Should.NotThrowAsync(() => _store.DeleteAllAsync(_bookId, Token));
    }

    [Fact]
    public async Task DeleteAll_RemovesTheFile_AndIsHarmlessWhenThereIsNone()
    {
        await _store.SaveAsync(_bookId, "merge", "x", Token);

        await _store.DeleteAllAsync(_bookId, Token);
        await _store.DeleteAllAsync(_bookId, Token);

        File.Exists(StepsFile).ShouldBeFalse();
    }
}
