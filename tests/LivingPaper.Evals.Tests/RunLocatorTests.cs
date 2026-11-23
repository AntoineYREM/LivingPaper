using Shouldly;

namespace LivingPaper.Evals.Tests;

public class RunLocatorTests
{
    [Fact]
    public void Resolve_AcceptsAPathToAFile()
    {
        using var folder = new TempFolder();
        var path = folder.Write("run.json", "{}");

        RunLocator.Resolve(path, []).ShouldBe(path);
    }

    [Theory]
    [InlineData("20261123-140512-gpt-5-mini")]
    [InlineData("20261123-140512-gpt-5-mini.json")]
    public void Resolve_FindsARunByItsIdInTheFolders(string name)
    {
        using var runs = new TempFolder();
        using var published = new TempFolder();
        var expected = published.Write("20261123-140512-gpt-5-mini.json", "{}");

        RunLocator.Resolve(name, [runs.Path, published.Path]).ShouldBe(expected);
    }

    [Fact]
    public void Resolve_PrefersTheFirstFolder()
    {
        using var first = new TempFolder();
        using var second = new TempFolder();
        var expected = first.Write("run.json", "{}");
        second.Write("run.json", "{}");

        RunLocator.Resolve("run", [first.Path, second.Path]).ShouldBe(expected);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("a/b")]
    public void Resolve_NeverLooksOutsideTheFoldersForAnId(string name)
    {
        using var folder = new TempFolder();

        Should.Throw<EvalException>(() => RunLocator.Resolve(name, [folder.Path]));
    }

    [Fact]
    public void Resolve_SaysWhereItLooked_WhenThereIsNoSuchRun()
    {
        using var folder = new TempFolder();

        var error = Should.Throw<EvalException>(() => RunLocator.Resolve("nope", [folder.Path]));

        error.Message.ShouldContain("nope");
        error.Message.ShouldContain(folder.Path);
    }
}
