using Shouldly;

namespace LivingPaper.Evals.Tests;

public class RunStoreTests
{
    private static RunRecord Record(string id = "20261123-140512-gpt-5-mini") => new(
        1,
        id,
        new DateTimeOffset(2026, 11, 23, 14, 5, 12, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 23, 14, 9, 0, TimeSpan.Zero),
        "madame-bovary-emma",
        "madame-bovary",
        "Emma Bovary",
        3,
        new RunModel("OpenAI", "gpt-5-mini"),
        new RunJudge("Other", "judge-model", "1", "character-fidelity"),
        new RunTokens(new TokenUsage(1200, 300, 90), new TokenUsage(800, 0, 40)),
        [new EvalResult("q-1", "normal", 1, 4, "One slip.", null, "Hello?", "Bonjour.", "trace", "span")]);

    [Fact]
    public async Task SaveAsync_WritesAFileNamedAfterTheRun_AndLoadAsyncReadsItBack()
    {
        using var folder = new TempFolder();
        var record = Record();

        var path = await RunStore.SaveAsync(Path.Combine(folder.Path, "runs"), record, CancellationToken.None);

        Path.GetFileName(path).ShouldBe("20261123-140512-gpt-5-mini.json");
        var loaded = await RunStore.LoadAsync(path, CancellationToken.None);
        loaded.Id.ShouldBe(record.Id);
        loaded.Chat.ShouldBe(record.Chat);
        loaded.Judge.ShouldBe(record.Judge);
        loaded.Tokens.ShouldBe(record.Tokens);
        loaded.Results.ShouldBe(record.Results);
        loaded.StartedAt.ShouldBe(record.StartedAt);
    }

    [Fact]
    public async Task SaveAsync_KeepsTheTextOfTheRepliesAndOfTheQuestions()
    {
        using var folder = new TempFolder();

        var path = await RunStore.SaveAsync(folder.Path, Record(), CancellationToken.None);

        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        text.ShouldContain("Bonjour.");
        text.ShouldContain("Hello?");
    }

    [Fact]
    public async Task SaveAsync_NeverOverwritesAnotherRun()
    {
        using var folder = new TempFolder();

        var first = await RunStore.SaveAsync(folder.Path, Record(), CancellationToken.None);
        var second = await RunStore.SaveAsync(folder.Path, Record(), CancellationToken.None);

        second.ShouldNotBe(first);
        Directory.GetFiles(folder.Path).Length.ShouldBe(2);
    }

    [Theory]
    [InlineData("gpt-5-mini", "gpt-5-mini")]
    [InlineData("Qwen/Qwen3 32B:latest", "qwen-qwen3-32b-latest")]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("", "model")]
    public void NewId_IsMadeOfTheTimeAndAFileSafeNameOfTheModel(string model, string expectedSlug) =>
        RunStore.NewId(new DateTimeOffset(2026, 11, 23, 14, 5, 12, TimeSpan.Zero), model).ShouldBe($"20261123-140512-{expectedSlug}");

    [Fact]
    public void NewId_CutsAVeryLongModelName() =>
        RunStore.NewId(DateTimeOffset.UnixEpoch, new string('a', 200)).Length.ShouldBeLessThan(80);

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "schemaVersion": 2 }""")]
    [InlineData("""{ "schemaVersion": 1 }""")]
    public async Task LoadAsync_RefusesAFileThatIsNotARun(string json)
    {
        using var folder = new TempFolder();

        await Should.ThrowAsync<EvalException>(() => RunStore.LoadAsync(folder.Write("run.json", json), CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_RefusesAMissingFile() =>
        await Should.ThrowAsync<EvalException>(() => RunStore.LoadAsync(Path.Combine(Path.GetTempPath(), "no-such-run.json"), CancellationToken.None));
}
