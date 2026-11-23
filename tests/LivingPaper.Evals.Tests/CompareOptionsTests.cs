using Shouldly;

namespace LivingPaper.Evals.Tests;

public class CompareOptionsTests
{
    [Fact]
    public void Parse_TakesTwoRuns()
    {
        var options = CompareOptions.Parse(["compare", "run-a", "run-b"]);

        options.ShouldBe(new CompareOptions(false, "run-a", "run-b", null));
    }

    [Fact]
    public void Parse_TakesAPriceFileAnywhere()
    {
        CompareOptions.Parse(["compare", "a", "--prices", "p.json", "b"]).ShouldBe(new CompareOptions(false, "a", "b", "p.json"));
        CompareOptions.Parse(["compare", "--prices", "p.json", "a", "b"]).PricesPath.ShouldBe("p.json");
    }

    [Theory]
    [InlineData("compare", "--help")]
    [InlineData("compare")]
    public void Parse_ShowsTheHelp(params string[] args) => CompareOptions.Parse(args).ShowHelp.ShouldBeTrue();

    [Theory]
    [InlineData("compare", "only-one")]
    [InlineData("compare", "a", "b", "c")]
    [InlineData("compare", "a", "b", "--prices")]
    [InlineData("compare", "a", "b", "--bogus", "x")]
    [InlineData("compare", "a", "b", "--prices", "p1", "--prices", "p2")]
    public void Parse_RefusesWhatItDoesNotUnderstand(params string[] args) =>
        Should.Throw<EvalException>(() => CompareOptions.Parse(args));
}
