using Shouldly;

namespace LivingPaper.Evals.Tests;

public class ChartDataTests
{
    private const string Baseline = "aaaaaaaaaaaa";
    private const string Current = "bbbbbbbbbbbb";

    private static readonly string PriceJson = """
        {
          "schemaVersion": 1, "currency": "USD", "updated": "2026-11-23",
          "models": {
            "paid/model": { "name": "Paid Model", "input": 1, "output": 10 },
            "other/paid": { "input": 2, "output": 4 },
            "free/model:free": { "name": "Free Model", "input": 0, "output": 0 }
          }
        }
        """;

    private static async Task<PriceList> PricesAsync()
    {
        using var folder = new TempFolder();
        return await PriceList.ReadAsync(folder.Write("prices.json", PriceJson), CancellationToken.None);
    }

    private static RunRecord Run(string id, string model, string? hash, int[] grades, string dataset = "emma", string character = "Emma Bovary", int chatInput = 1_000_000, int chatOutput = 100_000) => new(
        1,
        id,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        dataset,
        "book",
        character,
        1,
        new RunModel("Provider", model),
        new RunJudge("Judge", "judge-model", "2", "character-fidelity"),
        new RunTokens(new TokenUsage(chatInput, 0, chatOutput), new TokenUsage(0, 0, 0)),
        [.. grades.Select((grade, index) => new EvalResult("q-" + index, "normal", 1, grade, "r", null))],
        PromptsHash: hash);

    [Fact]
    public async Task Build_PairsTheGradeBeforeAndAfterOfEachModel_FromTheRunsOfTheTwoPrompts()
    {
        var runs = new[]
        {
            Run("a1", "paid/model", Baseline, [3, 4]),
            Run("a2", "paid/model", Baseline, [4, 5]), // the two runs of the baseline are pooled: 4.0
            Run("b1", "paid/model", Current, [5, 5]),
            Run("b2", "free/model:free", Current, [4]), // no baseline run: no row
            Run("c1", "other/paid", "cccccccccccc", [1]), // another prompt: ignored
        };

        var data = ChartData.Build(runs, [], await PricesAsync(), Baseline, Current);

        data.Quality.ShouldBe([new QualityRow("Paid Model", 4.0, 5.0)]);
    }

    [Fact]
    public async Task Build_LeavesOutTheAnswersThatWereNotGraded()
    {
        var run = Run("b1", "paid/model", Current, [4, 5]);
        run = run with { Results = [.. run.Results, new EvalResult("q-9", "normal", 1, null, null, "judge:invalid-verdict")] };

        var data = ChartData.Build([run, Run("a1", "paid/model", Baseline, [2])], [], await PricesAsync(), Baseline, Current);

        data.Quality.Single().After.ShouldBe(4.5);
    }

    [Fact]
    public async Task Build_NamesTheRowsOfAnotherCharacter_AndPutsThemAfterTheMainOnes()
    {
        var runs = new[]
        {
            Run("a1", "paid/model", Baseline, [3], dataset: "cyrano", character: "Cyrano de Bergerac"),
            Run("b1", "paid/model", Current, [4], dataset: "cyrano", character: "Cyrano de Bergerac"),
            Run("a2", "other/paid", Baseline, [1]),
            Run("b2", "other/paid", Current, [2]),
            Run("b3", "paid/model", Current, [5]),
        };

        var data = ChartData.Build(runs, [], await PricesAsync(), Baseline, Current);

        data.Quality.Select(row => row.Label).ShouldBe(["other/paid", "Paid Model · Cyrano"]); // the main dataset first, then the other character
        data.Notes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Build_GivesThePriceOf1000RepliesFromTheTokensOfTheChat_AndMarksFreeModels()
    {
        // 2 answers, 2,000,000 input tokens at 1 and 200,000 output tokens at 10 per million: 2 + 2 = 4 USD, so 2,000 USD for 1,000 replies.
        var runs = new[]
        {
            Run("b1", "paid/model", Current, [5, 5], chatInput: 2_000_000, chatOutput: 200_000),
            Run("b2", "free/model:free", Current, [4]),
        };

        var data = ChartData.Build(runs, [], await PricesAsync(), Baseline, Current);

        data.Value.Select(point => point.Name).ShouldBe(["Free Model", "Paid Model"]);
        data.Value[0].IsFree.ShouldBeTrue();
        data.Value[1].CostPer1000.ShouldNotBeNull().ShouldBe(2000.0, 1e-6);
        data.Value[1].IsFree.ShouldBeFalse();
        data.Currency.ShouldBe("USD");
    }

    [Fact]
    public async Task Build_SaysWhenAModelHasNoPrice_AndKeepsItWithoutACost()
    {
        var data = ChartData.Build([Run("b1", "unknown/model", Current, [4])], [], await PricesAsync(), Baseline, Current);

        data.Value.Single().CostPer1000.ShouldBeNull();
        data.Notes.ShouldContain(note => note.Contains("unknown/model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Build_TakesTheLatestTimeOfEachModel_FastestFirst_OnTheMainDatasetOnly()
    {
        var runs = new[] { Run("b1", "paid/model", Current, [5]), Run("b2", "free/model:free", Current, [4]) };
        LatencyEntry[] latencies =
        [
            new("20261123-100000-paid", "paid/model", "emma", 9.0, 8.0),
            new("20261123-110000-paid", "paid/model", "emma", 4.0, null), // the latest run of this model wins
            new("20261123-090000-free", "free/model:free", "emma", 2.0, 1.0),
            new("20261123-120000-paid", "paid/model", "cyrano", 99.0, 99.0), // another dataset: ignored
        ];

        var data = ChartData.Build(runs, latencies, await PricesAsync(), Baseline, Current);

        data.Latency.ShouldBe([new LatencyBar("Free Model", 2.0, 1.0), new LatencyBar("Paid Model", 4.0, null)]);
    }

    [Fact]
    public async Task Build_IsEmptyAndSaysWhy_WhenNoRunUsedTheCurrentPrompt()
    {
        var data = ChartData.Build([Run("a1", "paid/model", Baseline, [3])], [], await PricesAsync(), Baseline, Current);

        data.Quality.ShouldBeEmpty();
        data.Value.ShouldBeEmpty();
        data.Latency.ShouldBeEmpty();
        data.Notes.ShouldNotBeEmpty();
    }
}
