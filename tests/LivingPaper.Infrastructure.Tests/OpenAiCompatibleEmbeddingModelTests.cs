using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace LivingPaper.Infrastructure.Tests;

// The client that turns texts into vectors (POST {baseUrl}/embeddings), built by its factory, against a fake provider.
public sealed class OpenAiCompatibleEmbeddingModelTests : IAsyncLifetime
{
    private const string Key = "sk-embedding-secret-key";

    private static readonly IReadOnlyList<string> TwoTexts = ["a ball at the castle", "a letter"];

    private FakeLlmServer _server = null!;
    private OpenAiCompatibleEmbeddingModelFactory _factory = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _server = await FakeLlmServer.StartAsync();
        _factory = NewFactory();
    }

    public async ValueTask DisposeAsync()
    {
        _factory.Dispose();
        await _server.DisposeAsync();
    }

    // Retries without any real wait, so a test that exhausts them stays instant.
    private static OpenAiCompatibleEmbeddingModelFactory NewFactory(TimeSpan? headersTimeout = null) => new(
        NullLoggerFactory.Instance,
        new OpenAiCompatibleOptions
        {
            RetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero],
            HeadersTimeout = headersTimeout ?? TimeSpan.FromSeconds(30),
        });

    private LlmProvider Provider(string? baseUrl = null, string key = Key) =>
        new("id", "Fake", baseUrl ?? _server.BaseUrl, key, "bge-m3");

    private IEmbeddingModel Model(bool retry = true) => _factory.Create(Provider(), retry);

    private static Task Json(HttpContext context, string body)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(body, context.RequestAborted);
    }

    private static Func<HttpContext, Task> Answers(string body) => context => Json(context, body);

    private const string TwoVectors =
        """{"object":"list","data":[{"index":0,"embedding":[1,0,0]},{"index":1,"embedding":[0,0.5,-2]}],"usage":{"prompt_tokens":9,"total_tokens":9}}""";

    // ---- The request ----

    [Fact]
    public async Task Embed_SendsTheModelAndTheTexts_WithTheKeyAsABearerToken()
    {
        _server.Handler = Answers(TwoVectors);

        await Model().EmbedAsync(TwoTexts, null, Token);

        var request = _server.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe("POST");
        request.Path.ShouldBe("/v1/embeddings");
        request.Authorization.ShouldBe("Bearer " + Key);
        using var body = request.Json();
        body.RootElement.EnumerateObject().Select(property => property.Name).Order().ShouldBe(["input", "model"]);
        body.RootElement.GetProperty("model").GetString().ShouldBe("bge-m3");
        body.RootElement.GetProperty("input").EnumerateArray().Select(text => text.GetString()).ShouldBe(TwoTexts);
    }

    [Fact]
    public async Task Embed_SendsNoAuthorization_WhenThereIsNoKey_ForALocalServer()
    {
        _server.Handler = Answers(TwoVectors);

        await _factory.Create(Provider(key: string.Empty)).EmbedAsync(TwoTexts, null, Token);

        _server.Requests.ShouldHaveSingleItem().Authorization.ShouldBeEmpty();
    }

    [Fact]
    public async Task Embed_AsksNothing_ForNoText()
    {
        (await Model().EmbedAsync([], null, Token)).ShouldBeEmpty();

        _server.Requests.ShouldBeEmpty();
    }

    // ---- The answer ----

    [Fact]
    public async Task Embed_GivesOneVectorForEachText_AndCountsTheTokens()
    {
        _server.Handler = Answers(TwoVectors);
        var usage = new ChatUsageMeter();

        var vectors = await Model().EmbedAsync(TwoTexts, usage, Token);

        vectors.Count.ShouldBe(2);
        vectors[0].ShouldBe([1f, 0f, 0f]);
        vectors[1].ShouldBe([0f, 0.5f, -2f]);
        usage.InputTokens.ShouldBe(9);
        usage.OutputTokens.ShouldBe(0);
    }

    [Fact]
    public async Task Embed_PutsTheVectorsInTheOrderOfTheTexts_WhateverTheOrderOfTheAnswer()
    {
        _server.Handler = Answers("""{"data":[{"index":1,"embedding":[2]},{"index":0,"embedding":[1]}]}""");

        var vectors = await Model().EmbedAsync(TwoTexts, null, Token);

        vectors[0].ShouldBe([1f]);
        vectors[1].ShouldBe([2f]);
    }

    [Fact]
    public async Task Embed_TakesTheOrderOfTheAnswer_WhenTheProviderGivesNoIndex()
    {
        _server.Handler = Answers("""{"data":[{"embedding":[1]},{"embedding":[2]}]}""");

        var vectors = await Model().EmbedAsync(TwoTexts, null, Token);

        vectors[0].ShouldBe([1f]);
        vectors[1].ShouldBe([2f]);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"data":"nope"}""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":[{"index":0,"embedding":[1]}]}""")] // One vector for two texts.
    [InlineData("""{"data":[{"index":0,"embedding":[1]},{"index":1,"embedding":[2]},{"index":2,"embedding":[3]}]}""")]
    [InlineData("""{"data":[{"index":0,"embedding":[1]},{"index":0,"embedding":[2]}]}""")]
    [InlineData("""{"data":[{"index":0,"embedding":[1]},{"index":2,"embedding":[2]}]}""")]
    [InlineData("""{"data":[{"index":-1,"embedding":[1]},{"index":1,"embedding":[2]}]}""")]
    [InlineData("""{"data":[{"index":0,"embedding":[1]},{"index":1}]}""")]
    [InlineData("""{"data":[{"index":0,"embedding":[1]},{"index":1,"embedding":[]}]}""")]
    [InlineData("""{"data":[{"index":0,"embedding":[1,2]},{"index":1,"embedding":[1]}]}""")] // Two sizes.
    [InlineData("""{"data":[{"index":0,"embedding":[1]},{"index":1,"embedding":["2"]}]}""")]
    [InlineData("""{"data":[{"index":0,"embedding":[1]},{"index":1,"embedding":[null]}]}""")]
    [InlineData("""{"data":[{"index":0,"embedding":[1]},{"index":1,"embedding":[1e39]}]}""")] // Too big for a vector.
    [InlineData("""{"data":[{"index":0,"embedding":[1]},{"index":1,"embedding":[0,0]}]}""")]
    [InlineData("""{"data":[{"index":0,"embedding":[1,0]},{"index":1,"embedding":[0,0]}]}""")] // No direction.
    [InlineData("""{"data":[{"index":0,"embedding":"AACAPw=="},{"index":1,"embedding":"AACAPw=="}]}""")]
    public async Task Embed_RefusesAnAnswerThatCannotBeUsed_WithoutTryingAgain(string answer)
    {
        _server.Handler = Answers(answer);

        var exception = await Should.ThrowAsync<ChatProviderException>(() => Model().EmbedAsync(TwoTexts, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.InvalidResponse);
        _server.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Embed_RefusesAVectorThatIsLongerThanAnyModelMakes()
    {
        var huge = string.Join(',', Enumerable.Repeat("1", OpenAiCompatibleEmbeddingModel.MaxDimensions + 1));
        _server.Handler = Answers($$"""{"data":[{"embedding":[{{huge}}]}]}""");

        var exception = await Should.ThrowAsync<ChatProviderException>(() => Model().EmbedAsync(["a"], null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.InvalidResponse);
    }

    [Fact]
    public async Task Embed_StopsReadingAnAnswerThatIsTooBig()
    {
        _server.Handler = async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"data\":[],\"padding\":\"", context.RequestAborted);
            var chunk = new string('x', 1024 * 1024);
            for (var count = 0; count <= OpenAiCompatibleEmbeddingModel.MaxResponseBytes / chunk.Length; count++)
            {
                await context.Response.WriteAsync(chunk, context.RequestAborted);
            }

            await context.Response.WriteAsync("\"}", context.RequestAborted);
        };

        var exception = await Should.ThrowAsync<ChatProviderException>(() => Model(retry: false).EmbedAsync(["a"], null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.InvalidResponse);
    }

    // ---- Failures ----

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    public async Task Embed_ReportsARefusalAtOnce_WithItsStatusAndNeverItsBody(int status)
    {
        _server.Handler = context => FakeReplies.Status(context, status, "{\"error\":\"echo " + Key + "\"}");

        var exception = await Should.ThrowAsync<ChatProviderException>(() => Model().EmbedAsync(TwoTexts, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.Http);
        exception.StatusCode.ShouldBe(status);
        exception.ToString().ShouldNotContain(Key);
        _server.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Embed_TriesAgainAfterAPassingFailure()
    {
        var calls = 0;
        _server.Handler = context => ++calls == 1 ? FakeReplies.Status(context, 503, "busy") : Json(context, TwoVectors);

        var vectors = await Model().EmbedAsync(TwoTexts, null, Token);

        vectors.Count.ShouldBe(2);
        _server.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Embed_GivesUpAfterThreeRetries()
    {
        _server.Handler = context => FakeReplies.Status(context, 429, "slow down");

        var exception = await Should.ThrowAsync<ChatProviderException>(() => Model().EmbedAsync(TwoTexts, null, Token));

        exception.StatusCode.ShouldBe(429);
        _server.Requests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Embed_WithoutRetries_TriesOnlyOnce()
    {
        _server.Handler = context => FakeReplies.Status(context, 503, "busy");

        await Should.ThrowAsync<ChatProviderException>(() => Model(retry: false).EmbedAsync(TwoTexts, null, Token));

        _server.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Embed_SaysTheProviderDidNotAnswerInTime()
    {
        using var factory = NewFactory(headersTimeout: TimeSpan.FromMilliseconds(300));
        _server.Handler = async context => await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);

        var exception = await Should.ThrowAsync<ChatProviderException>(
            () => factory.Create(Provider(), retryPassingFailures: false).EmbedAsync(TwoTexts, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.TimedOut);
    }

    [Fact]
    public async Task Embed_SaysTheAnswerWasCut_WhenTheConnectionDropsInTheMiddle()
    {
        _server.Handler = async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.ContentLength = 1000;
            await context.Response.WriteAsync("{\"data\":[", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            // Let the client read the headers before the connection breaks: an abort that arrives first is a reset before
            // any answer (Unreachable), not an answer cut in the middle.
            await Task.Delay(300, context.RequestAborted);
            context.Abort();
        };

        var exception = await Should.ThrowAsync<ChatProviderException>(() => Model(retry: false).EmbedAsync(TwoTexts, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.StreamCut);
    }

    [Fact]
    public async Task Embed_IsUnreachable_WhenNothingListensAtTheAddress()
    {
        var address = _server.BaseUrl;
        await _server.DisposeAsync();
        _server = await FakeLlmServer.StartAsync(); // So that DisposeAsync of the test has something to stop.

        var exception = await Should.ThrowAsync<ChatProviderException>(
            () => _factory.Create(Provider(address), retryPassingFailures: false).EmbedAsync(TwoTexts, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.Unreachable);
        exception.Message.ShouldNotContain(address);
    }

    [Fact]
    public async Task Embed_DoesNotFollowARedirect_SoTheKeyNeverGoesToAnotherAddress()
    {
        _server.Handler = context =>
        {
            context.Response.StatusCode = 307;
            context.Response.Headers.Location = "/elsewhere/v1/chat/completions";
            return Task.CompletedTask;
        };

        var exception = await Should.ThrowAsync<ChatProviderException>(() => Model().EmbedAsync(TwoTexts, null, Token));

        exception.StatusCode.ShouldBe(307);
        _server.ElsewhereHits.ShouldBe(0);
    }

    // ---- What the factory refuses ----

    [Theory]
    [InlineData("ftp://user:secret-password@example.com/v1")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Create_RejectsAnAddressThatIsNotAnAbsoluteHttpOrHttpsUrl_WithoutEchoingIt(string baseUrl)
    {
        var exception = Should.Throw<ArgumentException>(() => _factory.Create(Provider(baseUrl)));

        exception.Message.ShouldNotContain("secret-password");
    }

    [Fact]
    public void Create_RejectsAKeyWithAControlCharacter_WithoutEchoingIt()
    {
        var exception = Should.Throw<ArgumentException>(() => _factory.Create(Provider(key: "sk-leaky\nbreak")));

        exception.Message.ShouldNotContain("sk-leaky");
    }
}
