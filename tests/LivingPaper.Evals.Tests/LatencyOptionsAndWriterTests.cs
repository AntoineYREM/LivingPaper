using System.Text.Json;
using Shouldly;

namespace LivingPaper.Evals.Tests;

public class LatencyOptionsAndWriterTests
{
    private static readonly RunLatency WithFirstText = new("run-1", "baseline", "model-a", "madame-bovary-emma", 40, 40, 2.2349, 2.06, 3.5, 1.5, 40);
    private static readonly RunLatency WithoutFirstText = new("run-2", null, "model-b", "madame-bovary-emma", 40, 42, 9.0, 8.7, 11.6, null, 0);

    [Fact]
    public void Parse_ReadsTheRunsAndTheOutputFile()
    {
        var options = LatencyOptions.Parse(["latency", "run-a", "--out", "latency.json", "run-b"]);

        options.ShowHelp.ShouldBeFalse();
        options.Runs.ShouldBe(["run-a", "run-b"]);
        options.OutPath.ShouldBe("latency.json");
    }

    [Theory]
    [InlineData("latency")]
    [InlineData("latency", "--help")]
    public void Parse_ShowsTheHelp(params string[] args) => LatencyOptions.Parse(args).ShowHelp.ShouldBeTrue();

    [Theory]
    [InlineData("latency", "--bogus", "run-a")]
    [InlineData("latency", "run-a", "--out")]
    [InlineData("latency", "run-a", "--out", "a.json", "--out", "b.json")]
    [InlineData("latency", "--out", "a.json")]
    public void Parse_RefusesWhatItDoesNotUnderstand(params string[] args) => Should.Throw<EvalException>(() => LatencyOptions.Parse(args));

    [Fact]
    public void Write_ShowsEachRunWithItsFigures_AndNamesTheRunsWithNoData()
    {
        var writer = new StringWriter();

        LatencyWriter.Write(writer, [WithFirstText, WithoutFirstText], ["run-3"]);

        var text = writer.ToString();
        text.ShouldContain("model-a");
        text.ShouldContain("baseline");
        text.ShouldContain("2.23"); // mean
        text.ShouldContain("2.06"); // median
        text.ShouldContain("1.50"); // first text
        text.ShouldContain("model-b");
        text.ShouldContain("run-3");
        text.ShouldContain("the judge is not counted");
    }

    [Fact]
    public void ToJson_KeepsTheFiguresOfEachRun_WithNoTextAndNoKey()
    {
        var json = LatencyWriter.ToJson([WithFirstText, WithoutFirstText], new DateTimeOffset(2026, 11, 23, 22, 0, 0, TimeSpan.Zero));

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        var runs = document.RootElement.GetProperty("runs").EnumerateArray().ToList();
        runs.Count.ShouldBe(2);
        runs[0].GetProperty("runId").GetString().ShouldBe("run-1");
        runs[0].GetProperty("meanSeconds").GetDouble().ShouldBe(2.23, 1e-9);
        runs[0].GetProperty("meanFirstTextSeconds").GetDouble().ShouldBe(1.5, 1e-9);
        runs[1].GetProperty("meanFirstTextSeconds").ValueKind.ShouldBe(JsonValueKind.Null);
        json.ShouldNotContain("pk-");
        json.ShouldNotContain("sk-");
    }
}
