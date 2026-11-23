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
        [
            new EvalResult("q-1", "normal", 1, 4, "One slip.", null, "Hello?", "Bonjour.", "trace", "span"),
            new EvalResult("q-2", "normal", 1, null, null, "judge:invalid-verdict", "Hi?", "Hello.", "trace-2", "span-2", "What the judge wrote."),
        ]);

    [Fact]
    public async Task TheLabelAndThePromptsHashAreKept_AndAnOlderFileWithoutThemStillLoads()
    {
        using var folder = new TempFolder();

        var path = await RunStore.SaveAsync(folder.Path, Record() with { Label = "rule-no-ai", PromptsHash = "0123456789ab" }, CancellationToken.None);
        var loaded = await RunStore.LoadAsync(path, CancellationToken.None);
        loaded.Label.ShouldBe("rule-no-ai");
        loaded.PromptsHash.ShouldBe("0123456789ab");

        var older = await RunStore.SaveAsync(folder.Path, Record("older"), CancellationToken.None);
        var text = await File.ReadAllTextAsync(older, CancellationToken.None);
        await File.WriteAllTextAsync(older, text.Replace("\"label\": null,", string.Empty, StringComparison.Ordinal).Replace("\"promptsHash\": null", "\"x\": 1", StringComparison.Ordinal), CancellationToken.None);
        var loadedOlder = await RunStore.LoadAsync(older, CancellationToken.None);
        loadedOlder.Label.ShouldBeNull();
        loadedOlder.PromptsHash.ShouldBeNull();
    }

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
    public async Task SaveAsync_WritesAccentsAsTheyAreSoTheFileCanBeRead()
    {
        using var folder = new TempFolder();
        var record = Record() with
        {
            Results = [new EvalResult("q-1", "language", 1, 5, "Réponse en français.", null, "Racontez-moi votre première rencontre.", "C'était un jour de marché, où l'on s'ennuie.", "t", "s")],
        };

        var path = await RunStore.SaveAsync(folder.Path, record, CancellationToken.None);

        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        text.ShouldContain("première rencontre");
        text.ShouldContain("C'était un jour de marché");
        text.ShouldNotContain("\\u00");
        (await RunStore.LoadAsync(path, CancellationToken.None)).Results.ShouldBe(record.Results);
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
