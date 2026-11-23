using System.Xml.Linq;
using Shouldly;

namespace LivingPaper.Evals.Tests;

public class ChartsCommandTests
{
    [Fact]
    public void Parse_ReadsTheOutputFolder_AndTheHelp()
    {
        ChartsOptions.Parse(["charts", "--out", "pictures"]).OutFolder.ShouldBe("pictures");
        ChartsOptions.Parse(["charts"]).OutFolder.ShouldBeNull();
        ChartsOptions.Parse(["charts", "--help"]).ShowHelp.ShouldBeTrue();
    }

    [Theory]
    [InlineData("charts", "--out")]
    [InlineData("charts", "--out", "a", "--out", "b")]
    [InlineData("charts", "--bogus")]
    public void Parse_RefusesWhatItDoesNotUnderstand(params string[] args) => Should.Throw<EvalException>(() => ChartsOptions.Parse(args));

    [Fact]
    public async Task RunAsync_DrawsTheSixPicturesOfThePublishedRuns_AsValidSvg()
    {
        using var folder = new TempFolder();
        var output = new StringWriter();

        var exitCode = await ChartsCommand.RunAsync(new ChartsOptions(false, folder.Path), output, CancellationToken.None);

        exitCode.ShouldBe(0);
        string[] names = ["quality", "value", "latency"];
        foreach (var name in names)
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                var path = Path.Combine(folder.Path, $"{name}-{theme}.svg");
                File.Exists(path).ShouldBeTrue(path);
                XDocument.Parse(await File.ReadAllTextAsync(path, CancellationToken.None)).Root!.Name.LocalName.ShouldBe("svg");
            }
        }

        // The figures of the pictures are those of the published files: the first rows of the chart "before and after".
        var quality = await File.ReadAllTextAsync(Path.Combine(folder.Path, "quality-light.svg"), CancellationToken.None);
        quality.ShouldContain("DeepSeek V4.1 Flash");
        quality.ShouldContain("Claude Opus 5.5");
    }
}
