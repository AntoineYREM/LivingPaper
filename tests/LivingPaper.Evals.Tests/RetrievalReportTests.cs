using LivingPaper.Application;
using Shouldly;

namespace LivingPaper.Evals.Tests;

// PAPER-87: the options that choose how the passages are searched, and what the report and the comparison say about it.
public class RetrievalReportTests
{
    private static EvalOptions Parse(params string[] args) => EvalOptions.Parse(["run", .. args], _ => null);

    [Fact]
    public void Parse_WithoutAModelForVectorization_RunsWithTheWholeStory()
    {
        var options = Parse("--book-data", "work");

        options.BookDataPath.ShouldBe("work");
        options.Retrieval.ShouldBeNull();
    }

    [Fact]
    public void Parse_ReadsTheSearchOptions_WithTheDefaultsOfTheApplication()
    {
        Parse("--book-data", "work", "--embedding", "My vectors").Retrieval
            .ShouldBe(new RetrievalOptions("My vectors", null, QueryMode.Recent, 5));
        Parse("--book-data", "work", "--embedding", "bge-m3", "--embedding-url", "http://localhost:11434/v1", "--query-mode", "Recent", "--passages", "5")
            .Retrieval.ShouldBe(new RetrievalOptions("bge-m3", "http://localhost:11434/v1", QueryMode.Recent, 5));
    }

    [Theory]
    [InlineData("--embedding", "v")]
    [InlineData("--query-mode", "last")]
    [InlineData("--passages", "3")]
    [InlineData("--embedding-url", "http://localhost:11434/v1")]
    public void Parse_RefusesASearchOptionThatHasNothingToActOn(string option, string value) =>
        Should.Throw<EvalException>(() => Parse(option, value));

    [Theory]
    [InlineData("--query-mode", "magic")]
    [InlineData("--query-mode", "1")]
    [InlineData("--passages", "0")]
    [InlineData("--passages", "11")]
    [InlineData("--passages", "three")]
    [InlineData("--embedding-url", "ftp://localhost/v1")]
    [InlineData("--embedding-url", "localhost:11434")]
    public void Parse_RefusesABadSearchOption(string option, string value) =>
        Should.Throw<EvalException>(() => Parse("--book-data", "work", "--embedding", "v", option, value));

    private static EvalResult Try(string id, int score, int replyInput, double seconds, bool? found, int queryOutput = 0, params string[] notices) =>
        new(id, "fact", 1, score, "r", null)
        {
            Trace = new TryTrace(
                "q", [1], [0.1], found, notices.Length == 0 ? null : notices, new TokenUsage(replyInput, 0, 50),
                queryOutput == 0 ? null : new TokenUsage(300, 0, queryOutput), seconds),
        };

    private static RunRecord Run(string id, RunRetrieval? retrieval, params EvalResult[] results) => new(
        1, id, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "d", "madame-bovary", "Emma", 1, new RunModel("p", "m"),
        new RunJudge("j", "jm", "1", "factual-accuracy"), new RunTokens(new TokenUsage(0, 0, 0), new TokenUsage(0, 0, 0)), results, Retrieval: retrieval);

    [Fact]
    public void Summary_AveragesTheCostOfAReply_AndCountsThePassagesFound()
    {
        var summary = TraceSummary.Of(
        [
            Try("a", 5, 3000, 2, true, queryOutput: 900),
            Try("b", 2, 3600, 4, false, notices: "SearchFailed"),
            Try("c", 4, 3300, 30, null, notices: "RewriteFailed"),
            new EvalResult("d", "fact", 1, null, null, "reply:ModelUnreachable"),
        ])!;

        summary.Tries.ShouldBe(3);
        summary.ReplyInput.ShouldBe(3300);
        summary.MaxReplyInput.ShouldBe(3600);
        summary.ReplyOutput.ShouldBe(50);
        summary.QueryInput.ShouldBe(100);
        summary.QueryOutput.ShouldBe(300);
        summary.MedianSeconds.ShouldBe(4);
        summary.Searched.ShouldBe(2);
        summary.Found.ShouldBe(1);
        summary.RewriteFailed.ShouldBe(1);
        summary.SearchFailed.ShouldBe(1);
    }

    [Fact]
    public void Summary_IsNullForARunWithNoTrace() =>
        TraceSummary.Of([new EvalResult("a", "fact", 1, 4, "r", null)]).ShouldBeNull();

    [Fact]
    public void TheReportOfARunSaysWhatAReplyCostsAndHowOftenThePassageWasFound()
    {
        using var writer = new StringWriter();
        EvalResult[] results = [Try("a", 5, 3000, 2, true), Try("b", 2, 3600, 4, false)];

        ReportWriter.Write(writer, ReportWriter.Summarize(results, ["fact"]), results, new TokenUsage(0, 0, 0), new TokenUsage(0, 0, 0));

        var text = writer.ToString();
        text.ShouldContain("prompt of the reply: 3300 tokens on average, 3600 at most");
        text.ShouldContain("expected passage found: 1 of 2 (50%)");
        text.ShouldContain("time of a reply: 3.0 s (median)");
    }

    [Fact]
    public void TheComparisonSaysHowEachRunReadTheBook()
    {
        var story = Run("story", null, Try("a", 3, 7200, 2, null), Try("b", 3, 7200, 2, null));
        var passages = Run("passages", new RunRetrieval("bge-m3", "rewrite", 3), Try("a", 5, 3000, 9, true), Try("b", 4, 3200, 9, false));
        using var writer = new StringWriter();

        ComparisonWriter.Write(writer, RunComparison.Compare(story, passages), prices: null);

        var text = writer.ToString();
        text.ShouldContain("the whole story");
        text.ShouldContain("3 passages (bge-m3), query mode rewrite");
        text.ShouldContain("prompt of the reply: 7200 tokens on average");
        text.ShouldContain("expected passage found: 1 of 2 (50%)");
    }

    [Fact]
    public async Task ARunFileKeepsTheSearchOfEachTry()
    {
        using var folder = new TempFolder();
        var run = Run("r", new RunRetrieval("bge-m3", "last", 5), Try("a", 5, 3000, 2, true));

        var read = await RunStore.LoadAsync(await RunStore.SaveAsync(folder.Path, run, CancellationToken.None), CancellationToken.None);

        read.Retrieval.ShouldBe(run.Retrieval);
        var trace = read.Results.Single().Trace!;
        trace.Query.ShouldBe("q");
        trace.Passages.ShouldBe([1]);
        trace.ExpectedFound.ShouldBe(true);
    }
}
