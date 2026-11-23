using Shouldly;

namespace LivingPaper.Evals.Tests;

public class RunLatencyTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 20, 0, 0, TimeSpan.Zero);

    private static RunRecord Run(string model = "model-a", int answers = 4) => new(
        1,
        "run-1",
        Start,
        Start.AddMinutes(10),
        "madame-bovary-emma",
        "madame-bovary",
        "Emma Bovary",
        1,
        new RunModel("Provider", model),
        new RunJudge("Judge", "judge-model", "2", "character-fidelity"),
        new RunTokens(new TokenUsage(0, 0, 0), new TokenUsage(0, 0, 0)),
        [.. Enumerable.Range(1, answers).Select(index => new EvalResult("q-" + index, "normal", 1, 5, "r", null))],
        Label: "baseline");

    private static ChatTiming Call(double seconds, double? firstToken = null, string model = "model-a", int minute = 1) =>
        new(Start.AddMinutes(minute), model, seconds, firstToken);

    [Fact]
    public void Of_GivesTheMeanTheMedianAndTheSlowestTenthOfTheCalls()
    {
        var timings = new[] { Call(1, 0.5), Call(2, 1), Call(3, 1.5), Call(10, 2) };

        var latency = RunLatency.Of(Run(), timings)!;

        latency.RunId.ShouldBe("run-1");
        latency.Label.ShouldBe("baseline");
        latency.Answers.ShouldBe(4);
        latency.Calls.ShouldBe(4);
        latency.MeanSeconds.ShouldBe(4.0, 1e-9);
        latency.MedianSeconds.ShouldBe(2.5, 1e-9);
        latency.P90Seconds.ShouldBe(3.0, 1e-9); // the value at the 90th percentile by rank: index floor(0.9 * 3) = 2
        latency.MeanFirstTokenSeconds.ShouldNotBeNull().ShouldBe(1.25, 1e-9);
        latency.FirstTokenCalls.ShouldBe(4);
    }

    [Fact]
    public void Of_KeepsOnlyTheCallsOfTheModelThatStartedWhileTheRunWas_Running()
    {
        var timings = new[]
        {
            Call(2),
            Call(99, model: "another-model"),
            Call(99, minute: -5), // before the run
            Call(99, minute: 11), // after it
        };

        var latency = RunLatency.Of(Run(), timings)!;

        latency.Calls.ShouldBe(1);
        latency.MeanSeconds.ShouldBe(2);
    }

    [Fact]
    public void Of_AveragesTheFirstTextOnlyOverTheCallsThatHaveIt()
    {
        var latency = RunLatency.Of(Run(), [Call(2, 1), Call(2, null), Call(2, 3)])!;

        latency.MeanFirstTokenSeconds.ShouldNotBeNull().ShouldBe(2.0, 1e-9);
        latency.FirstTokenCalls.ShouldBe(2);
        RunLatency.Of(Run(), [Call(2)])!.MeanFirstTokenSeconds.ShouldBeNull();
    }

    [Fact]
    public void Of_IsNullWhenNoCallWasFound() => RunLatency.Of(Run(), [Call(2, model: "another-model")]).ShouldBeNull();

    [Fact]
    public void Of_MedianOfAnOddNumberOfCallsIsTheMiddleOne() =>
        RunLatency.Of(Run(), [Call(5), Call(1), Call(2)])!.MedianSeconds.ShouldBe(2);
}
