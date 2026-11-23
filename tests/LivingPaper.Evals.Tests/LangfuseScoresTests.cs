using Shouldly;

namespace LivingPaper.Evals.Tests;

public class LangfuseScoresTests
{
    private static RunRecord Record(params EvalResult[] results) => new(
        1,
        "run-1",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        "d",
        "b",
        "Emma",
        2,
        new RunModel("P", "m"),
        new RunJudge("J", "jm", "7", "character-fidelity"),
        new RunTokens(new TokenUsage(0, 0, 0), new TokenUsage(0, 0, 0)),
        results);

    [Fact]
    public void From_MakesOneScorePerGradedAnswerOnItsRootSpan()
    {
        var scores = LangfuseScores.From(Record(
            new EvalResult("q-1", "normal", 1, 4, "One slip.", null, "Hi?", "Hello.", "trace-a", "span-a"),
            new EvalResult("q-1", "normal", 2, 5, "Fine.", null, "Hi?", "Hello.", "trace-b", "span-b")));

        scores.Count.ShouldBe(2);
        scores[0].ShouldBe(new LangfuseScore("run-1-q-1-1", "trace-a", "span-a", "character-fidelity", 4, "One slip.", "experiment", "7"));
        scores[1].Id.ShouldBe("run-1-q-1-2");
    }

    [Fact]
    public void From_SkipsWhatWasNotGradedAndWhatHasNoTrace()
    {
        var scores = LangfuseScores.From(Record(
            new EvalResult("q-1", "normal", 1, null, null, "reply:NoModel", "Hi?", null, "trace", "span"),
            new EvalResult("q-2", "normal", 1, 3, "ok", null, "Hi?", "Hello.", null, null)));

        scores.ShouldBeEmpty();
    }

    [Fact]
    public void From_GivesAnEmptyCommentRatherThanAnEmptyText()
    {
        var scores = LangfuseScores.From(Record(new EvalResult("q-1", "normal", 1, 3, string.Empty, null, "Hi?", "Hello.", "t", "s")));

        scores.Single().Comment.ShouldBeNull();
    }
}
