using Shouldly;

namespace LivingPaper.Evals.Tests;

public class EvalOptionsTests
{
    private static EvalOptions Parse(string[] args, Dictionary<string, string>? environment = null) =>
        EvalOptions.Parse(args, name => environment is not null && environment.TryGetValue(name, out var value) ? value : null);

    [Fact]
    public void Parse_UsesTheDefaults()
    {
        var options = Parse(["run"]);

        options.ShowHelp.ShouldBeFalse();
        options.Repeats.ShouldBe(3);
        options.ChatProvider.ShouldBeNull();
        options.JudgeProvider.ShouldBeNull();
        options.QuestionsPath.ShouldBeNull();
    }

    [Fact]
    public void Parse_ReadsTheOptions()
    {
        var options = Parse(["run", "--repeats", "5", "--chat", "Strong", "--judge", "Cheap", "--questions", "q.json", "--judge-prompt", "j.json"]);

        options.ShouldBe(new EvalOptions(false, 5, "q.json", "j.json", "Strong", "Cheap", LocalOnly: false));
    }

    [Fact]
    public void Parse_LocalOnlyIsAFlagWithNoValue()
    {
        Parse(["run", "--local-only"]).LocalOnly.ShouldBeTrue();
        Parse(["run", "--local-only", "--repeats", "2"]).Repeats.ShouldBe(2);
        Parse(["run", "--repeats", "2", "--local-only"]).LocalOnly.ShouldBeTrue();
        Parse(["run"]).LocalOnly.ShouldBeFalse();
    }

    [Fact]
    public void Parse_TakesTheModelsFromTheEnvironment_ButTheOptionsWin()
    {
        var environment = new Dictionary<string, string> { ["EVAL_CHAT_PROVIDER"] = " FromEnv ", ["EVAL_JUDGE_PROVIDER"] = "JudgeEnv" };

        var options = Parse(["run", "--chat", "FromOption"], environment);

        options.ChatProvider.ShouldBe("FromOption");
        options.JudgeProvider.ShouldBe("JudgeEnv");
        Parse(["run"], environment).ChatProvider.ShouldBe("FromEnv");
    }

    [Theory]
    [InlineData()]
    [InlineData("--help")]
    [InlineData("run", "--help")]
    public void Parse_ShowsTheHelp(params string[] args) => Parse(args).ShowHelp.ShouldBeTrue();

    [Theory]
    [InlineData("compare")]
    [InlineData("run", "--bogus", "x")]
    [InlineData("run", "--chat")]
    [InlineData("run", "--chat", "A", "--chat", "B")]
    [InlineData("run", "--repeats", "0")]
    [InlineData("run", "--repeats", "21")]
    [InlineData("run", "--repeats", "many")]
    public void Parse_RefusesWhatItDoesNotUnderstand(params string[] args) =>
        Should.Throw<EvalException>(() => Parse(args));
}
