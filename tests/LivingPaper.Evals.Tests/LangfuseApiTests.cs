using System.Net;
using System.Text;
using System.Text.Json;
using LivingPaper.Infrastructure;
using Shouldly;

namespace LivingPaper.Evals.Tests;

public class LangfuseApiTests
{
    private static readonly LangfuseOptions Options = new(new Uri("http://langfuse.test:3000/api/public/otel/v1/traces"), "pk-test", "sk-test", false);

    private sealed class FakeHandler(Func<HttpRequestMessage, string?, (HttpStatusCode, string)> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body, string? Authorization)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, body, request.Headers.Authorization?.ToString()));
            var (status, text) = answer(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static LangfuseApi Api(FakeHandler handler) => new(Options, handler);

    [Fact]
    public async Task EnsureDatasetAsync_ReturnsTheIdOfAnExistingDataset_WithoutCreatingAnother()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.OK, """{"id": "ds-1", "name": "madame-bovary-emma"}"""));

        var id = await Api(handler).EnsureDatasetAsync("madame-bovary-emma", "About Emma.", CancellationToken.None);

        id.ShouldBe("ds-1");
        handler.Requests.Single().Method.ShouldBe(HttpMethod.Get);
        handler.Requests.Single().Url.ShouldBe("http://langfuse.test:3000/api/public/v2/datasets/madame-bovary-emma");
    }

    [Fact]
    public async Task EnsureDatasetAsync_CreatesTheDatasetWhenItDoesNotExist()
    {
        var handler = new FakeHandler((request, _) => request.Method == HttpMethod.Get
            ? (HttpStatusCode.NotFound, "{}")
            : (HttpStatusCode.OK, """{"id": "ds-new"}"""));

        var id = await Api(handler).EnsureDatasetAsync("a b", "About Emma.", CancellationToken.None);

        id.ShouldBe("ds-new");
        handler.Requests[0].Url.ShouldEndWith("/v2/datasets/a%20b");
        var create = handler.Requests[1];
        create.Method.ShouldBe(HttpMethod.Post);
        create.Url.ShouldBe("http://langfuse.test:3000/api/public/v2/datasets");
        using var body = JsonDocument.Parse(create.Body!);
        body.RootElement.GetProperty("name").GetString().ShouldBe("a b");
        body.RootElement.GetProperty("description").GetString().ShouldBe("About Emma.");
    }

    [Fact]
    public async Task UpsertItemAsync_SendsTheQuestionUnderAStableId()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.OK, """{"id": "x"}"""));
        var question = new EvalQuestion("bovary-normal-01", "normal", "Hello?", "Stays Emma.");

        await Api(handler).UpsertItemAsync("madame-bovary-emma", question, CancellationToken.None);

        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Url.ShouldBe("http://langfuse.test:3000/api/public/dataset-items");
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        root.GetProperty("id").GetString().ShouldBe(LangfuseNames.ItemId("madame-bovary-emma", "bovary-normal-01"));
        root.GetProperty("datasetName").GetString().ShouldBe("madame-bovary-emma");
        root.GetProperty("input").GetString().ShouldBe("Hello?");
        root.GetProperty("expectedOutput").GetString().ShouldBe("Stays Emma.");
        root.GetProperty("metadata").GetProperty("category").GetString().ShouldBe("normal");
    }

    [Fact]
    public async Task PostScoresAsync_SendsAListOfNumericScores()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.Accepted, """{"message": "Accepted"}"""));
        var score = new LangfuseScore("score-1", "trace", "span", "character-fidelity", 4, "One slip.", "experiment", "1");

        await Api(handler).PostScoresAsync([score], CancellationToken.None);

        var request = handler.Requests.Single();
        request.Url.ShouldBe("http://langfuse.test:3000/api/public/scores");
        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        var sent = body.RootElement[0];
        sent.GetProperty("id").GetString().ShouldBe("score-1");
        sent.GetProperty("traceId").GetString().ShouldBe("trace");
        sent.GetProperty("observationId").GetString().ShouldBe("span");
        sent.GetProperty("name").GetString().ShouldBe("character-fidelity");
        sent.GetProperty("value").GetDouble().ShouldBe(4);
        sent.GetProperty("dataType").GetString().ShouldBe("NUMERIC");
        sent.GetProperty("comment").GetString().ShouldBe("One slip.");
        sent.GetProperty("environment").GetString().ShouldBe("experiment");
        sent.GetProperty("metadata").GetProperty("judgePromptVersion").GetString().ShouldBe("1");
    }

    [Fact]
    public async Task PostScoresAsync_SendsNothingForNoScores()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.Accepted, "{}"));

        await Api(handler).PostScoresAsync([], CancellationToken.None);

        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task PostScoresAsync_SplitsALongListInBatches()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.Accepted, "{}"));
        var scores = Enumerable.Range(0, 120).Select(index => new LangfuseScore($"s{index}", "t", "o", "n", 3, null, "experiment", "1")).ToList();

        await Api(handler).PostScoresAsync(scores, CancellationToken.None);

        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task PostScoresAsync_ReportsRejectedScoresByCountOnly()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.MultiStatus, """{"accepted": 1, "rejected": 2, "errors": [{"message": "secret-looking text"}]}"""));

        var error = await Should.ThrowAsync<EvalException>(() => Api(handler).PostScoresAsync(
            [new LangfuseScore("s", "t", "o", "n", 3, null, "experiment", "1")], CancellationToken.None));

        error.Message.ShouldContain("2");
        error.Message.ShouldNotContain("secret-looking");
    }

    [Fact]
    public async Task EveryRequest_UsesTheKeysAsBasicAuthentication()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.OK, """{"id": "ds-1"}"""));

        await Api(handler).EnsureDatasetAsync("d", "x", CancellationToken.None);

        handler.Requests.Single().Authorization.ShouldBe(Options.AuthorizationHeader);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "keys")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    [InlineData(HttpStatusCode.Found, "302")]
    public async Task AFailedRequest_BecomesAnErrorWithTheStatusAndNeverTheBody(HttpStatusCode status, string mention)
    {
        var handler = new FakeHandler((_, _) => (status, "the-body-with-sk-test-in-it"));

        var error = await Should.ThrowAsync<EvalException>(() => Api(handler).UpsertItemAsync("d", new EvalQuestion("q", "c", "i", "e"), CancellationToken.None));

        error.Message.ShouldContain(mention);
        error.Message.ShouldNotContain("the-body");
        error.Message.ShouldNotContain("sk-test");
    }

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused to http://langfuse.test:3000 with sk-test");
    }

    [Fact]
    public async Task AnUnreachableLangfuse_BecomesAnErrorThatRepeatsNothingOfTheException()
    {
        var error = await Should.ThrowAsync<EvalException>(() => new LangfuseApi(Options, new Unreachable()).EnsureDatasetAsync("d", "x", CancellationToken.None));

        error.Message.ShouldNotContain("sk-test");
        error.Message.ShouldNotContain("refused");
    }

    [Fact]
    public async Task GetChatTimingsAsync_ReadsTheTimingsOfTheChatCalls_PageAfterPage()
    {
        var handler = new FakeHandler((request, _) => request.RequestUri!.Query.Contains("cursor=next-page", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, """{"data":[{"startTime":"2026-11-23T20:02:00.000Z","model":"model-b","latency":4.5,"timeToFirstToken":null}],"meta":{}}""")
            : (HttpStatusCode.OK, """{"data":[{"startTime":"2026-11-23T20:01:00.000Z","model":"model-a","latency":2.25,"timeToFirstToken":0.5},{"startTime":"2026-11-23T20:01:30.000Z","model":"model-a","latency":null}],"meta":{"cursor":"next-page"}}"""));

        var timings = await Api(handler).GetChatTimingsAsync(new DateTimeOffset(2026, 11, 23, 19, 0, 0, TimeSpan.Zero), CancellationToken.None);

        timings.ShouldBe(
        [
            new ChatTiming(new DateTimeOffset(2026, 11, 23, 20, 1, 0, TimeSpan.Zero), "model-a", 2.25, 0.5),
            new ChatTiming(new DateTimeOffset(2026, 11, 23, 20, 2, 0, TimeSpan.Zero), "model-b", 4.5, null),
        ]);
        handler.Requests.Count.ShouldBe(2);
        var first = handler.Requests[0];
        first.Method.ShouldBe(HttpMethod.Get);
        first.Url.ShouldStartWith("http://langfuse.test:3000/api/public/v2/observations?");
        first.Url.ShouldContain("type=GENERATION");
        first.Url.ShouldContain("name=chat-reply");
        first.Url.ShouldContain("environment=experiment");
        first.Url.ShouldContain("fromStartTime=2026-11-23T19%3A00%3A00.0000000Z");
        first.Authorization.ShouldNotBeNull().ShouldStartWith("Basic ");
        handler.Requests[1].Url.ShouldContain("cursor=next-page");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "refused the keys")]
    [InlineData(HttpStatusCode.NotFound, "HTTP 404")]
    public async Task GetChatTimingsAsync_SaysWhatWentWrong_WithoutRepeatingTheAnswerOfTheServer(HttpStatusCode status, string expected)
    {
        var handler = new FakeHandler((_, _) => (status, "secret-in-the-answer"));

        var exception = await Should.ThrowAsync<EvalException>(() => Api(handler).GetChatTimingsAsync(DateTimeOffset.UnixEpoch, CancellationToken.None));

        exception.Message.ShouldContain(expected);
        exception.Message.ShouldNotContain("secret-in-the-answer");
    }

    [Fact]
    public async Task GetChatTimingsAsync_RefusesAnAnswerThatIsNotWhatWasExpected()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.OK, "not json"));

        await Should.ThrowAsync<EvalException>(() => Api(handler).GetChatTimingsAsync(DateTimeOffset.UnixEpoch, CancellationToken.None));
    }
}
