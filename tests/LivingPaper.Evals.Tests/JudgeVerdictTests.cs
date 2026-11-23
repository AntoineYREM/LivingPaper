using Shouldly;

namespace LivingPaper.Evals.Tests;

public class JudgeVerdictTests
{
    [Fact]
    public void TryParse_ReadsAScoreAndItsReason() =>
        JudgeVerdict.TryParse("""{"score": 4, "reason": "One slip."}""").ShouldBe(new JudgeVerdict(4, "One slip."));

    [Theory]
    [InlineData("```json\n{\"score\": 5, \"reason\": \"Fine.\"}\n```")]
    [InlineData("Here is my grade: {\"score\": 5, \"reason\": \"Fine.\"} Hope it helps.")]
    public void TryParse_FindsTheObjectInsideOtherText(string text) =>
        JudgeVerdict.TryParse(text).ShouldBe(new JudgeVerdict(5, "Fine."));

    [Fact]
    public void TryParse_AcceptsAScoreWrittenWithADecimalPointWhenItIsWhole() =>
        JudgeVerdict.TryParse("""{"score": 3.0, "reason": "ok"}""")!.Score.ShouldBe(3);

    [Fact]
    public void TryParse_AcceptsAMissingReason() =>
        JudgeVerdict.TryParse("""{"score": 2}""").ShouldBe(new JudgeVerdict(2, string.Empty));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no json at all")]
    [InlineData("""{"score": 0, "reason": "x"}""")]
    [InlineData("""{"score": 6, "reason": "x"}""")]
    [InlineData("""{"score": 3.5, "reason": "x"}""")]
    [InlineData("""{"score": "4", "reason": "x"}""")]
    [InlineData("""{"reason": "no score"}""")]
    [InlineData("""{"score": 4, "reason": """)]
    public void TryParse_ReturnsNullForAnythingElse(string? text) => JudgeVerdict.TryParse(text).ShouldBeNull();

    [Fact]
    public void TryParse_CutsAVeryLongReason()
    {
        var verdict = JudgeVerdict.TryParse("{\"score\": 1, \"reason\": \"" + new string('x', 2000) + "\"}");

        verdict!.Reason.Length.ShouldBe(JudgeVerdict.MaxReasonLength);
    }
}
