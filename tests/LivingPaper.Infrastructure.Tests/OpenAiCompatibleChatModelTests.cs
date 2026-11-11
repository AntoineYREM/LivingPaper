using System.Net.Sockets;
using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace LivingPaper.Infrastructure.Tests;

// The adapter alone (one call, no retry) against a fake provider that is a real HTTP server on a local port.
public sealed class OpenAiCompatibleChatModelTests : IAsyncLifetime
{
    private const string Key = "sk-test-key-123";

    private static readonly IReadOnlyList<ChatMessage> Messages =
    [
        new(ChatRole.System, "You play Argan."),
        new(ChatRole.User, "Hello"),
    ];

    private readonly List<HttpClient> _clients = [];
    private FakeLlmServer _server = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _server = await FakeLlmServer.StartAsync();

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        await _server.DisposeAsync();
    }

    private OpenAiCompatibleChatModel NewModel(OpenAiCompatibleOptions? options = null, string? baseUrl = null)
    {
        var url = baseUrl ?? _server.BaseUrl;
        // Configured as the factory does: the adapter relies on a client that follows no redirect and has no timeout.
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _clients.Add(client);
        return new OpenAiCompatibleChatModel(client, new LlmProvider("id", "Fake", url, Key, "test-model"), options ?? new OpenAiCompatibleOptions());
    }

    private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> stream)
    {
        var pieces = new List<string>();
        await foreach (var piece in stream.WithCancellation(Token))
        {
            pieces.Add(piece);
        }

        return pieces;
    }

    private static OpenAiCompatibleOptions Fast(int headersMs = 5000, int idleMs = 5000) => new()
    {
        HeadersTimeout = TimeSpan.FromMilliseconds(headersMs),
        IdleTimeout = TimeSpan.FromMilliseconds(idleMs),
    };

    // ---- What is sent ----

    [Fact]
    public async Task Complete_SendsAStreamingChatCompletionRequest_WithTheKeyInTheAuthorizationHeader()
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "Hi");

        await NewModel().CompleteAsync(Messages, null, Token);

        var request = _server.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe("POST");
        request.Path.ShouldBe("/v1/chat/completions");
        request.Authorization.ShouldBe("Bearer " + Key);
        request.ContentType.ShouldNotBeNull().ShouldStartWith("application/json");
        using var body = request.Json();
        body.RootElement.GetProperty("model").GetString().ShouldBe("test-model");
        body.RootElement.GetProperty("stream").GetBoolean().ShouldBeTrue();
        body.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean().ShouldBeTrue();
        var messages = body.RootElement.GetProperty("messages").EnumerateArray().ToList();
        messages.Count.ShouldBe(2);
        messages[0].GetProperty("role").GetString().ShouldBe("system");
        messages[0].GetProperty("content").GetString().ShouldBe("You play Argan.");
        messages[1].GetProperty("role").GetString().ShouldBe("user");
        messages[1].GetProperty("content").GetString().ShouldBe("Hello");
    }

    [Fact]
    public async Task Complete_SendsNothingThatAReasoningModelWouldRefuse()
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "Hi");

        await NewModel().CompleteAsync(Messages, null, Token);

        using var body = _server.Requests.Single().Json();
        var properties = body.RootElement.EnumerateObject().Select(property => property.Name).Order().ToList();
        properties.ShouldBe(["messages", "model", "stream", "stream_options"]); // No max_tokens, temperature, thinking...
    }

    [Fact]
    public async Task Complete_SendsAssistantMessagesWithTheAssistantRole()
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "Hi");

        await NewModel().CompleteAsync([new(ChatRole.User, "a"), new(ChatRole.Assistant, "b"), new(ChatRole.User, "c")], null, Token);

        using var body = _server.Requests.Single().Json();
        body.RootElement.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("role").GetString())
            .ShouldBe(["user", "assistant", "user"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_WorksWhetherOrNotTheBaseUrlEndsWithASlash(bool trailingSlash)
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "Hi");

        var reply = await NewModel(baseUrl: _server.BaseUrl + (trailingSlash ? "/" : string.Empty)).CompleteAsync(Messages, null, Token);

        reply.ShouldBe("Hi");
        _server.Requests.Single().Path.ShouldBe("/v1/chat/completions");
    }

    [Fact]
    public async Task Complete_SendsNoAuthorizationHeader_WhenTheProviderNeedsNoKey()
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "Hi");
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(_server.BaseUrl + "/") };
        var model = new OpenAiCompatibleChatModel(client, new LlmProvider("id", "Local", _server.BaseUrl, string.Empty, "llama"), new OpenAiCompatibleOptions());

        await model.CompleteAsync(Messages, null, Token);

        _server.Requests.Single().Authorization.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("sk-abc\nX-Injected: 1")]
    [InlineData("sk-abc\r\n")]
    [InlineData("sk-\u0000abc")]
    public void Constructor_RejectsAKeyWithAControlCharacter_WithoutEchoingIt(string key)
    {
        using var client = new HttpClient();

        var exception = Should.Throw<ArgumentException>(
            () => new OpenAiCompatibleChatModel(client, new LlmProvider("id", "Fake", _server.BaseUrl, key, "m"), new OpenAiCompatibleOptions()));

        exception.Message.ShouldNotContain("sk-");
    }

    // ---- What is read ----

    [Fact]
    public async Task StreamReply_ReturnsThePiecesInOrder()
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "Hel", "lo", ", Argan", "!");

        (await CollectAsync(NewModel().StreamReplyAsync(Messages, null, Token))).ShouldBe(["Hel", "lo", ", Argan", "!"]);
    }

    [Fact]
    public async Task Complete_ReturnsTheWholeReply()
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "Hel", "lo", " there");

        (await NewModel().CompleteAsync(Messages, null, Token)).ShouldBe("Hello there");
    }

    [Fact]
    public async Task StreamReply_KeepsAccentsEmojisAndNewlines()
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "Très bien, ", "mon ami 😀", "\nÀ bientôt ! « ok »");

        (await NewModel().CompleteAsync(Messages, null, Token)).ShouldBe("Très bien, mon ami 😀\nÀ bientôt ! « ok »");
    }

    [Fact]
    public async Task StreamReply_IgnoresWhatIsNotAPieceOfText()
    {
        _server.Handler = async context =>
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.StartAsync(context.RequestAborted);
            await context.Response.WriteAsync(": a comment line that keeps the connection alive\n\n", context.RequestAborted);
            await context.Response.WriteAsync("event: message\ndata: " + FakeReplies.Chunk("") + "\n\n", context.RequestAborted); // role-only first chunk
            await context.Response.WriteAsync("data: {\"choices\":[{\"delta\":{\"role\":\"assistant\",\"reasoning_content\":\"thinking...\"}}]}\n\n", context.RequestAborted);
            await context.Response.WriteAsync("data:" + FakeReplies.Chunk("Hi") + "\n\n", context.RequestAborted); // no space after "data:"
            await context.Response.WriteAsync("data: {\"choices\":[]}\n\n", context.RequestAborted);
            await context.Response.WriteAsync("data: " + FakeReplies.Chunk(null, "stop") + "\n\n", context.RequestAborted);
            await context.Response.WriteAsync("data: [DONE]  \n\n", context.RequestAborted); // trailing spaces
        };

        (await CollectAsync(NewModel().StreamReplyAsync(Messages, null, Token))).ShouldBe(["Hi"]);
    }

    [Fact]
    public async Task StreamReply_AcceptsWindowsLineEndings()
    {
        _server.Handler = async context =>
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.StartAsync(context.RequestAborted);
            await context.Response.WriteAsync("data: " + FakeReplies.Chunk("Hi") + "\r\n\r\ndata: [DONE]\r\n\r\n", context.RequestAborted);
        };

        (await NewModel().CompleteAsync(Messages, null, Token)).ShouldBe("Hi");
    }

    // ---- Tokens used ----

    [Fact]
    public async Task Complete_AddsTheTokensTheProviderReports()
    {
        _server.Handler = context => FakeReplies.StreamAsync(context, [FakeReplies.Chunk("Hi"), FakeReplies.Chunk(null, "stop"), FakeReplies.Usage(120, 30, cached: 100)]);
        var meter = new ChatUsageMeter();

        await NewModel().CompleteAsync(Messages, meter, Token);

        meter.InputTokens.ShouldBe(120);
        meter.CachedInputTokens.ShouldBe(100);
        meter.OutputTokens.ShouldBe(30);
    }

    [Fact]
    public async Task StreamReply_AddsTheTokensOfEveryCallToTheSameMeter()
    {
        _server.Handler = context => FakeReplies.StreamAsync(context, [FakeReplies.Chunk("Hi"), FakeReplies.Usage(10, 5)]);
        var meter = new ChatUsageMeter();
        var model = NewModel();

        await CollectAsync(model.StreamReplyAsync(Messages, meter, Token));
        await model.CompleteAsync(Messages, meter, Token);

        meter.InputTokens.ShouldBe(20);
        meter.OutputTokens.ShouldBe(10);
        meter.CachedInputTokens.ShouldBe(0);
    }

    [Fact]
    public async Task Complete_AddsNothing_WhenTheProviderReportsNoUsage()
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "Hi");
        var meter = new ChatUsageMeter();

        await NewModel().CompleteAsync(Messages, meter, Token);

        (meter.InputTokens + meter.CachedInputTokens + meter.OutputTokens).ShouldBe(0);
    }

    [Fact]
    public async Task Complete_AcceptsAUsageWithMissingFields()
    {
        _server.Handler = context => FakeReplies.StreamAsync(context, [FakeReplies.Chunk("Hi"), "{\"choices\":[],\"usage\":{\"completion_tokens\":7}}"]);
        var meter = new ChatUsageMeter();

        await NewModel().CompleteAsync(Messages, meter, Token);

        meter.OutputTokens.ShouldBe(7);
        meter.InputTokens.ShouldBe(0);
    }

    // ---- A reply that does not end well ----

    [Fact]
    public async Task Complete_ThrowsStreamCut_WhenTheReplyEndsBeforeDone()
    {
        _server.Handler = context => FakeReplies.StreamAsync(context, [FakeReplies.Chunk("Half of a reply")], done: false);

        var exception = await Should.ThrowAsync<ChatProviderException>(() => NewModel().CompleteAsync(Messages, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.StreamCut);
        exception.IsTransient.ShouldBeTrue();
    }

    [Fact]
    public async Task StreamReply_ThrowsStreamCutAfterThePiecesThatCame_NeverAShorterReply()
    {
        _server.Handler = context => FakeReplies.StreamAsync(context, [FakeReplies.Chunk("Hel"), FakeReplies.Chunk("lo")], done: false);
        var received = new List<string>();

        var exception = await Should.ThrowAsync<ChatProviderException>(async () =>
        {
            await foreach (var piece in NewModel().StreamReplyAsync(Messages, null, Token))
            {
                received.Add(piece);
            }
        });

        exception.Kind.ShouldBe(ChatFailureKind.StreamCut);
        received.ShouldBe(["Hel", "lo"]);
    }

    [Fact]
    public async Task Complete_ThrowsStreamCut_WhenTheConnectionIsBrokenInTheMiddle()
    {
        _server.Handler = async context =>
        {
            await FakeReplies.StreamAsync(context, [FakeReplies.Chunk("Half")], done: false);
            context.Abort();
        };

        var exception = await Should.ThrowAsync<ChatProviderException>(() => NewModel().CompleteAsync(Messages, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.StreamCut);
    }

    [Fact]
    public async Task Complete_ThrowsStreamCut_WhenNothingAtAllArrivesBeforeTheConnectionCloses()
    {
        _server.Handler = context => FakeReplies.StreamAsync(context, [], done: false);

        var exception = await Should.ThrowAsync<ChatProviderException>(() => NewModel().CompleteAsync(Messages, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.StreamCut);
    }

    [Fact]
    public async Task Complete_ThrowsEmptyReply_WhenTheReplyEndsWithoutAnyText()
    {
        _server.Handler = context => FakeReplies.StreamAsync(context, [FakeReplies.Chunk(null, "stop"), FakeReplies.Usage(5, 0)]);

        var exception = await Should.ThrowAsync<ChatProviderException>(() => NewModel().CompleteAsync(Messages, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.EmptyReply);
        exception.IsTransient.ShouldBeTrue();
    }

    [Fact]
    public async Task Complete_ThrowsContentFilteredException_WhenTheProviderFiltersTheReply()
    {
        _server.Handler = context => FakeReplies.StreamAsync(context, [FakeReplies.Chunk("I will tell you how"), FakeReplies.Chunk(null, "content_filter")]);

        var exception = await Should.ThrowAsync<ContentFilteredException>(() => NewModel().CompleteAsync(Messages, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.ContentFiltered);
        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task StreamReply_ThrowsContentFilteredException_AfterThePiecesThatCame()
    {
        _server.Handler = context => FakeReplies.StreamAsync(context, [FakeReplies.Chunk("A few words"), FakeReplies.Chunk(null, "content_filter")]);
        var received = new List<string>();

        await Should.ThrowAsync<ContentFilteredException>(async () =>
        {
            await foreach (var piece in NewModel().StreamReplyAsync(Messages, null, Token))
            {
                received.Add(piece);
            }
        });

        received.ShouldBe(["A few words"]);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"choices\":[{\"delta\":{\"content\":42}}]}")]
    public async Task Complete_ThrowsInvalidResponse_WhenAnEventCannotBeRead(string data)
    {
        _server.Handler = context => FakeReplies.StreamAsync(context, [FakeReplies.Chunk("Hi"), data]);

        var exception = await Should.ThrowAsync<ChatProviderException>(() => NewModel().CompleteAsync(Messages, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.InvalidResponse);
        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldNotContain("not json");
    }

    [Fact]
    public async Task Complete_ThrowsInvalidResponse_WhenTheProviderAnswersWithAPlainJsonBodyInsteadOfAStream()
    {
        _server.Handler = context =>
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync("{\"choices\":[{\"message\":{\"content\":\"Hi\"}}]}", context.RequestAborted);
        };

        var exception = await Should.ThrowAsync<ChatProviderException>(() => NewModel().CompleteAsync(Messages, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.InvalidResponse);
    }

    // ---- HTTP errors ----

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Complete_ThrowsHttpWithTheStatus_AndNeverEchoesTheBodyOfTheError(int status)
    {
        _server.Handler = context => FakeReplies.Status(context, status, "{\"error\":{\"message\":\"Incorrect API key provided: sk-test-key-123 SECRET-BODY-TEXT\"}}");

        var exception = await Should.ThrowAsync<ChatProviderException>(() => NewModel().CompleteAsync(Messages, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.Http);
        exception.StatusCode.ShouldBe(status);
        exception.Message.ShouldContain(status.ToString(System.Globalization.CultureInfo.InvariantCulture));
        exception.Message.ShouldNotContain("SECRET-BODY-TEXT");
        exception.Message.ShouldNotContain(Key);
        exception.IsTransient.ShouldBe(status is 408 or 429 or >= 500);
    }

    [Fact]
    public async Task StreamReply_ThrowsHttpBeforeAnyPiece()
    {
        _server.Handler = context => FakeReplies.Status(context, 401, "nope");
        var received = new List<string>();

        var exception = await Should.ThrowAsync<ChatProviderException>(async () =>
        {
            await foreach (var piece in NewModel().StreamReplyAsync(Messages, null, Token))
            {
                received.Add(piece);
            }
        });

        exception.StatusCode.ShouldBe(401);
        received.ShouldBeEmpty();
    }

    [Fact]
    public async Task Complete_DoesNotFollowARedirect()
    {
        _server.Handler = context =>
        {
            context.Response.StatusCode = 302;
            context.Response.Headers.Location = "/elsewhere/v1/chat/completions";
            return Task.CompletedTask;
        };

        var exception = await Should.ThrowAsync<ChatProviderException>(() => NewModel().CompleteAsync(Messages, null, Token));

        exception.StatusCode.ShouldBe(302);
        exception.IsTransient.ShouldBeFalse();
        _server.ElsewhereHits.ShouldBe(0); // The key was not sent to another address.
    }

    // ---- Network and time ----

    [Fact]
    public async Task Complete_ThrowsUnreachable_WhenNothingListensAtTheAddress_AndNeverEchoesTheUrl()
    {
        var port = FreeLocalPort();
        var model = NewModel(baseUrl: $"http://user:pw-secret@127.0.0.1:{port}/v1");

        var exception = await Should.ThrowAsync<ChatProviderException>(() => model.CompleteAsync(Messages, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.Unreachable);
        exception.IsTransient.ShouldBeTrue();
        exception.Message.ShouldNotContain("pw-secret");
        exception.Message.ShouldNotContain(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Complete_ThrowsTimedOut_WhenTheProviderDoesNotAnswerInTime()
    {
        _server.Handler = async context => await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);

        var exception = await Should.ThrowAsync<ChatProviderException>(() => NewModel(Fast(headersMs: 200)).CompleteAsync(Messages, null, Token));

        exception.Kind.ShouldBe(ChatFailureKind.TimedOut);
        exception.IsTransient.ShouldBeTrue();
    }

    [Fact]
    public async Task StreamReply_ThrowsTimedOutAfterThePiecesThatCame_WhenTheProviderGoesSilent()
    {
        _server.Handler = async context =>
        {
            await FakeReplies.StreamAsync(context, [FakeReplies.Chunk("Hel")], done: false);
            await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
        };
        var received = new List<string>();

        var exception = await Should.ThrowAsync<ChatProviderException>(async () =>
        {
            await foreach (var piece in NewModel(Fast(idleMs: 200)).StreamReplyAsync(Messages, null, Token))
            {
                received.Add(piece);
            }
        });

        exception.Kind.ShouldBe(ChatFailureKind.TimedOut);
        received.ShouldBe(["Hel"]);
    }

    [Fact]
    public async Task Complete_KeepsWaiting_AsLongAsEventsKeepComing()
    {
        // Each gap is shorter than the idle limit although the whole reply takes longer than it.
        _server.Handler = async context =>
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.StartAsync(context.RequestAborted);
            for (var i = 0; i < 6; i++)
            {
                await Task.Delay(120, context.RequestAborted);
                await context.Response.WriteAsync("data: " + FakeReplies.Chunk("x") + "\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }

            await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
        };

        (await NewModel(Fast(idleMs: 400)).CompleteAsync(Messages, null, Token)).ShouldBe("xxxxxx");
    }

    [Fact]
    public async Task Complete_ThrowsOperationCanceled_NotAProviderError_WhenTheCallerCancels()
    {
        _server.Handler = async context => await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(() => NewModel().CompleteAsync(Messages, null, cancellation.Token));
    }

    [Fact]
    public async Task StreamReply_ThrowsOperationCanceled_WhenTheCallerCancelsInTheMiddleOfAReply()
    {
        _server.Handler = async context =>
        {
            await FakeReplies.StreamAsync(context, [FakeReplies.Chunk("Hel")], done: false);
            await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var piece in NewModel().StreamReplyAsync(Messages, null, cancellation.Token))
            {
                piece.ShouldBe("Hel");
                await cancellation.CancelAsync();
            }
        });
    }

    private static int FreeLocalPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
