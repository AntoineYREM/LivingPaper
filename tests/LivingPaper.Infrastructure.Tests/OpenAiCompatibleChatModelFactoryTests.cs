using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace LivingPaper.Infrastructure.Tests;

// The factory builds a ready-to-use client (HTTP adapter + retries) for a provider, against a fake provider.
public sealed class OpenAiCompatibleChatModelFactoryTests : IAsyncLifetime
{
    private static readonly IReadOnlyList<ChatMessage> Messages = [new(ChatRole.User, "Hello")];

    private FakeLlmServer _server = null!;
    private OpenAiCompatibleChatModelFactory _factory = null!;

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
    private static OpenAiCompatibleChatModelFactory NewFactory() => new(
        NullLoggerFactory.Instance,
        new OpenAiCompatibleOptions { RetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero] });

    private LlmProvider Provider(string? baseUrl = null, string key = "sk-factory-key") =>
        new("id", "Fake", baseUrl ?? _server.BaseUrl, key, "test-model");

    // ---- The base URL ----

    [Theory]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.1:1234/v1/")]
    [InlineData("http://192.168.1.20:8080/v1")]
    [InlineData("http://host.docker.internal:11434/v1")]
    [InlineData("  https://api.openai.com/v1  ")]
    public void Create_AcceptsHttpAndHttpsAddresses_PrivateOnesIncluded(string baseUrl)
    {
        // Private addresses are allowed on purpose: local models (Ollama, LM Studio) must work.
        _factory.Create(Provider(baseUrl)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/v1")]
    [InlineData("api.openai.com/v1")]
    [InlineData("ftp://user:secret-password@example.com/v1")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://")]
    public void Create_RejectsAnAddressThatIsNotAnAbsoluteHttpOrHttpsUrl_WithoutEchoingIt(string baseUrl)
    {
        var exception = Should.Throw<ArgumentException>(() => _factory.Create(Provider(baseUrl)));

        exception.Message.ShouldNotContain("secret-password");
        exception.Message.ShouldNotContain("passwd");
    }

    [Fact]
    public void Create_RejectsAKeyWithAControlCharacter_WithoutEchoingIt()
    {
        var exception = Should.Throw<ArgumentException>(() => _factory.Create(Provider(key: "sk-leaky\nbreak")));

        exception.Message.ShouldNotContain("sk-leaky");
    }

    // ---- A client that works ----

    [Fact]
    public async Task Create_GivesAClientThatTalksToTheProvider_WithItsOwnKeyAndModel()
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "Hello ", "there");

        var reply = await _factory.Create(Provider()).CompleteAsync(Messages, null, Token);

        reply.ShouldBe("Hello there");
        var request = _server.Requests.Single();
        request.Authorization.ShouldBe("Bearer sk-factory-key");
        using var body = request.Json();
        body.RootElement.GetProperty("model").GetString().ShouldBe("test-model");
    }

    [Fact]
    public async Task Create_GivesAClientForEachProvider_EachWithItsOwnKey()
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "ok");
        var first = _factory.Create(Provider(key: "key-of-first"));
        var second = _factory.Create(Provider(key: "key-of-second"));

        await Task.WhenAll(first.CompleteAsync(Messages, null, Token), second.CompleteAsync(Messages, null, Token));

        _server.Requests.Select(request => request.Authorization).Order().ShouldBe(["Bearer key-of-first", "Bearer key-of-second"]);
    }

    [Fact]
    public async Task Create_GivesAClientThatTriesAgainAfterAPassingFailure()
    {
        var calls = 0;
        _server.Handler = context => ++calls == 1 ? FakeReplies.Status(context, 503, "busy") : FakeReplies.TextAsync(context, "Second try");

        var reply = await _factory.Create(Provider()).CompleteAsync(Messages, null, Token);

        reply.ShouldBe("Second try");
        _server.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Create_GivesAClientThatGivesUpAfterThreeRetries()
    {
        _server.Handler = context => FakeReplies.Status(context, 429, "slow down");

        var exception = await Should.ThrowAsync<ChatProviderException>(() => _factory.Create(Provider()).CompleteAsync(Messages, null, Token));

        exception.StatusCode.ShouldBe(429);
        _server.Requests.Count.ShouldBe(4); // The first try and three retries.
    }

    [Fact]
    public async Task Create_WithoutRetries_GivesAClientThatTriesOnlyOnce()
    {
        _server.Handler = context => FakeReplies.Status(context, 503, "busy");

        var exception = await Should.ThrowAsync<ChatProviderException>(
            () => _factory.Create(Provider(), retryPassingFailures: false).CompleteAsync(Messages, null, Token));

        exception.StatusCode.ShouldBe(503);
        _server.Requests.Count.ShouldBe(1); // The connection test of the settings: no wait before showing the error.
    }

    [Fact]
    public async Task Create_WithoutRetries_StillWorksWhenTheProviderAnswers()
    {
        _server.Handler = context => FakeReplies.TextAsync(context, "pong");

        (await _factory.Create(Provider(), retryPassingFailures: false).CompleteAsync(Messages, null, Token)).ShouldBe("pong");
    }

    [Fact]
    public async Task Create_GivesAClientThatDoesNotRetryAWrongKey()
    {
        _server.Handler = context => FakeReplies.Status(context, 401, "wrong key");

        var exception = await Should.ThrowAsync<ChatProviderException>(() => _factory.Create(Provider()).CompleteAsync(Messages, null, Token));

        exception.StatusCode.ShouldBe(401);
        _server.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Create_GivesAStreamingClientThatTriesAgainBeforeTheFirstPiece()
    {
        var calls = 0;
        _server.Handler = context => ++calls == 1 ? FakeReplies.Status(context, 500, "oops") : FakeReplies.TextAsync(context, "Hel", "lo");
        var pieces = new List<string>();

        await foreach (var piece in _factory.Create(Provider()).StreamReplyAsync(Messages, null, Token))
        {
            pieces.Add(piece);
        }

        pieces.ShouldBe(["Hel", "lo"]);
        _server.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Create_GivesAClientThatDoesNotFollowARedirect_SoTheKeyNeverGoesToAnotherAddress()
    {
        _server.Handler = context =>
        {
            context.Response.StatusCode = 307;
            context.Response.Headers.Location = "/elsewhere/v1/chat/completions";
            return Task.CompletedTask;
        };

        var exception = await Should.ThrowAsync<ChatProviderException>(() => _factory.Create(Provider()).CompleteAsync(Messages, null, Token));

        exception.StatusCode.ShouldBe(307);
        _server.ElsewhereHits.ShouldBe(0);
        _server.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var factory = NewFactory();

        factory.Dispose();

        Should.NotThrow(factory.Dispose);
    }
}
