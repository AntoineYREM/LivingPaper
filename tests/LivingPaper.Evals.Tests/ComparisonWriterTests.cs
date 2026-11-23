using Shouldly;

namespace LivingPaper.Evals.Tests;

public class ComparisonWriterTests
{
    private static EvalResult Graded(string question, string category, int score) => new(question, category, 1, score, "r", null);

    private static RunRecord Run(string id, string chatModel, TokenUsage chat, TokenUsage judge, params EvalResult[] results) => new(
        1,
        id,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        "d",
        "b",
        "Emma",
        1,
        new RunModel("Chat provider", chatModel),
        new RunJudge("Judge provider", "judge-model", "2", "character-fidelity"),
        new RunTokens(chat, judge),
        results);

    private static string Write(RunRecord a, RunRecord b, PriceList? prices = null)
    {
        var writer = new StringWriter();
        ComparisonWriter.Write(writer, RunComparison.Compare(a, b), prices);
        return writer.ToString();
    }

    private static readonly TokenUsage Chat = new(1000, 0, 100);
    private static readonly TokenUsage Judge = new(500, 0, 50);

    private static readonly EvalResult[] Better =
    [
        Graded("q1", "normal", 5), Graded("q2", "normal", 5), Graded("q3", "trap", 4), Graded("q4", "trap", 4),
    ];

    private static readonly EvalResult[] Worse =
    [
        Graded("q1", "normal", 3), Graded("q2", "normal", 4), Graded("q3", "trap", 1), Graded("q4", "trap", 2),
    ];

    [Fact]
    public void Write_ShowsTheLabelAndThePromptsOfARunThatHasThem()
    {
        var a = Run("run-a", "model-a", Chat, Judge, Better) with { Label = "baseline", PromptsHash = "0123456789ab" };

        var text = Write(a, Run("run-b", "model-b", Chat, Judge, Worse));

        text.ShouldContain("label \"baseline\"; chat prompts 0123456789ab.");
    }

    [Fact]
    public void Write_NamesBothRunsAndTheirModels()
    {
        var text = Write(Run("run-a", "model-a", Chat, Judge, Better), Run("run-b", "model-b", Chat, Judge, Worse));

        text.ShouldContain("run-a");
        text.ShouldContain("run-b");
        text.ShouldContain("model-a");
        text.ShouldContain("model-b");
        text.ShouldContain("judge-model");
    }

    [Fact]
    public void Write_ShowsEachCategoryWithItsSignedDifference()
    {
        var text = Write(Run("a", "ma", Chat, Judge, Better), Run("b", "mb", Chat, Judge, Worse));

        text.ShouldContain("normal");
        text.ShouldContain("5.00");
        text.ShouldContain("3.50");
        text.ShouldContain("-1.50");
        text.ShouldContain("trap");
        text.ShouldContain("-2.50");
    }

    [Fact]
    public void Write_SaysWhetherTheDifferenceIsMoreThanNoise()
    {
        var clear = Write(
            Run("a", "ma", Chat, Judge, Graded("q1", "c", 3), Graded("q2", "c", 3), Graded("q3", "c", 3)),
            Run("b", "mb", Chat, Judge, Graded("q1", "c", 4), Graded("q2", "c", 4), Graded("q3", "c", 4)));
        var noisy = Write(
            Run("a", "ma", Chat, Judge, Graded("q1", "c", 3), Graded("q2", "c", 5), Graded("q3", "c", 3), Graded("q4", "c", 5)),
            Run("b", "mb", Chat, Judge, Graded("q1", "c", 5), Graded("q2", "c", 3), Graded("q3", "c", 5), Graded("q4", "c", 3)));

        clear.ShouldContain("95% interval");
        clear.ShouldContain("does not include 0");
        noisy.ShouldContain("includes 0");
        noisy.ShouldContain("noise");
    }

    [Fact]
    public void Write_PrintsTheWarnings()
    {
        var a = Run("a", "ma", Chat, Judge, Better);
        var b = Run("b", "mb", Chat, Judge, Worse) with { Judge = new RunJudge("J", "judge-model", "3", "character-fidelity") };

        Write(a, b).ShouldContain("judge prompt versions differ");
    }

    [Fact]
    public void Write_ListsTheBiggestDifferencesPerQuestion()
    {
        var text = Write(Run("a", "ma", Chat, Judge, Better), Run("b", "mb", Chat, Judge, Worse));

        text.ShouldContain("q3");
        text.ShouldContain("-3.0");
    }

    [Fact]
    public void Write_ShowsTokensAndTheCostWhenThereAreAPriceForEachModel()
    {
        var prices = new PriceList(
            "USD",
            new Dictionary<string, ModelPrice>
            {
                ["ma"] = new(2, null, 10),
                ["mb"] = new(1, null, 4),
                ["judge-model"] = new(1, null, 2),
            });

        var text = Write(Run("a", "ma", Chat, Judge, Better), Run("b", "mb", Chat, Judge, Worse), prices);

        text.ShouldContain("1000 in");
        text.ShouldContain("500 in");

        // A: chat (1000 x 2 + 100 x 10) / 1e6 = 0.003, judge (500 x 1 + 50 x 2) / 1e6 = 0.0006, total 0.0036.
        text.ShouldContain("USD 0.0036");

        // B: chat (1000 x 1 + 100 x 4) / 1e6 = 0.0014, plus the judge 0.0006: 0.0020.
        text.ShouldContain("USD 0.0020");
    }

    [Fact]
    public void Write_SaysWhenThereIsNoPriceRatherThanGuessing()
    {
        var prices = new PriceList("USD", new Dictionary<string, ModelPrice> { ["judge-model"] = new(1, null, 2) });

        var text = Write(Run("a", "unpriced", Chat, Judge, Better), Run("b", "mb", Chat, Judge, Worse), prices);

        text.ShouldContain("no price for unpriced");
    }

    [Fact]
    public void Write_ShowsNoCostWithoutAPriceFile()
    {
        var text = Write(Run("a", "ma", Chat, Judge, Better), Run("b", "mb", Chat, Judge, Worse));

        text.ShouldContain("no price file");
        text.ShouldContain("1000 in");
    }
}
