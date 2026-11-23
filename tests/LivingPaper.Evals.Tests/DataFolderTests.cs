using Shouldly;

namespace LivingPaper.Evals.Tests;

public class DataFolderTests
{
    private const string SiteData = "src/LivingPaper.Web/data";

    private static string WithSettings(string root, string relativeFolder)
    {
        var folder = Path.GetFullPath(Path.Combine(root, relativeFolder));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "settings.json"), "{}");
        return folder;
    }

    [Fact]
    public void Resolve_UsesTheConfiguredFolder()
    {
        using var root = new TempFolder();
        WithSettings(root.Path, "data");

        DataFolder.Resolve(Path.Combine(root.Path, "elsewhere"), root.Path).ShouldBe(Path.Combine(root.Path, "elsewhere"));
    }

    [Fact]
    public void Resolve_UsesTheDataFolderOfTheCurrentFolderWhenItHoldsSettings()
    {
        using var root = new TempFolder();
        var data = WithSettings(root.Path, "data");
        WithSettings(root.Path, SiteData);

        DataFolder.Resolve(null, root.Path).ShouldBe(data);
    }

    [Fact]
    public void Resolve_FindsTheDataFolderOfTheSiteWhenRunFromTheRootOfTheRepository()
    {
        using var root = new TempFolder();
        var site = WithSettings(root.Path, SiteData);

        DataFolder.Resolve(null, root.Path).ShouldBe(site);
    }

    [Fact]
    public void Resolve_FindsItFromAFolderBelowTheRepositoryRoot()
    {
        using var root = new TempFolder();
        var site = WithSettings(root.Path, SiteData);
        var below = Path.Combine(root.Path, "tools", "deeper");
        Directory.CreateDirectory(below);

        DataFolder.Resolve(null, below).ShouldBe(site);
    }

    [Fact]
    public void Resolve_FallsBackToDataOfTheCurrentFolderWhenNothingIsFound()
    {
        using var root = new TempFolder();

        DataFolder.Resolve(null, root.Path).ShouldBe(Path.Combine(root.Path, "data"));
    }
}
