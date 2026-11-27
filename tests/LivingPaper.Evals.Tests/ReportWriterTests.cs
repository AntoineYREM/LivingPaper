using Shouldly;

namespace LivingPaper.Evals.Tests;

public class ReportWriterTests
{
    private static EvalResult Result(string id, string category, int? score, string? failure = null, string reason = "") =>
        new(id, category, 1, score, reason, failure);

    private static readonly EvalResult[] Results =
    [
        Result("n-1", "normal", 5),
        Result("n-2", "normal", 4),
        Result("t-1", "trap", 1, reason: "Says it is an AI."),
        Result("t-2", "trap", 3),
        Result("t-3", "trap", null, "reply:ModelUnreachable"),
    ];

    [Fact]
    public void Summarize_AveragesByCategoryInTheOrderOfTheQuestions()
    {
        var summary = ReportWriter.Summarize(Results, ["normal", "trap"]);

        summary.Categories.Select(category => category.Category).ShouldBe(["normal", "trap"]);
        summary.Categories[0].ShouldBe(new CategorySummary("normal", Scored: 2, Failed: 0, Average: 4.5, Min: 4));
        summary.Categories[1].ShouldBe(new CategorySummary("trap", Scored: 2, Failed: 1, Average: 2.0, Min: 1));
    }

    [Fact]
    public void Summarize_AveragesOnlyWhatWasGraded_AndCountsTheFailures()
    {
        var summary = ReportWriter.Summarize(Results, ["normal", "trap"]);

        summary.Scored.ShouldBe(4);
        summary.Failed.ShouldBe(1);
        summary.Average.ShouldBe(3.25);
    }

    [Fact]
    public void Summarize_HasNoAverageWhenNothingWasGraded()
    {
        var summary = ReportWriter.Summarize([Result("q", "normal", null, "reply:NoModel")], ["normal"]);

        summary.Average.ShouldBeNull();
        summary.Categories[0].Average.ShouldBe(0);
    }

    [Fact]
    public void Write_ShowsTheCategoriesTheTokensAndTheLowestScores()
    {
        var summary = ReportWriter.Summarize(Results, ["normal", "trap"]);
        var writer = new StringWriter();

        ReportWriter.Write(writer, summary, Results, new TokenUsage(1200, 300, 90), new TokenUsage(800, 0, 40));

        var text = writer.ToString();
        text.ShouldContain("normal");
        text.ShouldContain("4.50");
        text.ShouldContain("trap");
        text.ShouldContain("2.00");
        text.ShouldContain("Average: 3.25");
        text.ShouldContain("1 failed");
        text.ShouldContain("reply:ModelUnreachable");
        text.ShouldContain("t-1");
        text.ShouldContain("Says it is an AI.");
        text.ShouldContain("1200 in");
        text.ShouldContain("800 in");
    }

    [Fact]
    public void Write_CountsTheRepliesOfEachGrade_TheFailedOnesApart()
    {
        var writer = new StringWriter();

        ReportWriter.Write(writer, ReportWriter.Summarize(Results, ["normal", "trap"]), Results, new TokenUsage(0, 0, 0), new TokenUsage(0, 0, 0));

        writer.ToString().ShouldContain("Replies by grade: 5: 1, 4: 1, 3: 1, 2: 0, 1: 1");
    }

    [Fact]
    public void Write_DoesNotListTheGoodScoresAsProblems()
    {
        var results = new[] { Result("n-1", "normal", 5, reason: "Perfect.") };
        var writer = new StringWriter();

        ReportWriter.Write(writer, ReportWriter.Summarize(results, ["normal"]), results, new TokenUsage(0, 0, 0), new TokenUsage(0, 0, 0));

        writer.ToString().ShouldNotContain("Perfect.");
    }
}
