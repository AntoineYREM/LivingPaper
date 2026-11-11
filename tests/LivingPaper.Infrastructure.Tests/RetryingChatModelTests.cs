using LivingPaper.Application;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace LivingPaper.Infrastructure.Tests;

public class RetryingChatModelTests
{
    private static readonly IReadOnlyList<ChatMessage> Messages = [new(ChatRole.User, "Hello")];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ChatProviderException Http(int status) => new(ChatFailureKind.Http, $"The language model answered HTTP {status}.", status);

    private static ChatProviderException Failure(ChatFailureKind kind) => new(kind, "failure");

    // The waits the model was asked to make; none is really waited, so the tests are instant.
    private sealed class Waits
    {
        public List<TimeSpan> Asked { get; } = [];

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Asked.Add(delay);
            return Task.CompletedTask;
        }
    }

    private static RetryingChatModel Retrying(ScriptedChatModel inner, Waits waits, ILogger<RetryingChatModel>? logger = null) =>
        new(inner, RetryingChatModel.DefaultDelays, logger ?? new ListLogger<RetryingChatModel>(), waits.WaitAsync);

    [Fact]
    public void DefaultDelays_AreTwoFiveAndTenSeconds()
    {
        RetryingChatModel.DefaultDelays.ShouldBe([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)]);
    }

    // ---- CompleteAsync ----

    [Fact]
    public async Task Complete_ReturnsTheReplyWithoutWaiting_WhenTheFirstTrySucceeds()
    {
        var inner = new ScriptedChatModel().ThenReply("Hi!");
        var waits = new Waits();

        var reply = await Retrying(inner, waits).CompleteAsync(Messages, null, Token);

        reply.ShouldBe("Hi!");
        inner.Calls.ShouldBe(1);
        waits.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Complete_PassesTheMessagesAndTheUsageMeterToTheModel()
    {
        var inner = new ScriptedChatModel().ThenReply("Hi!");
        var meter = new ChatUsageMeter();

        await Retrying(inner, new Waits()).CompleteAsync(Messages, meter, Token);

        inner.LastMessages.ShouldBeSameAs(Messages);
        inner.LastUsage.ShouldBeSameAs(meter);
    }

    [Fact]
    public async Task Complete_TriesAgainAfterAPassingFailure_AndReturnsTheNextReply()
    {
        var inner = new ScriptedChatModel().ThenFail(Http(503)).ThenReply("Second try");
        var waits = new Waits();

        var reply = await Retrying(inner, waits).CompleteAsync(Messages, null, Token);

        reply.ShouldBe("Second try");
        inner.Calls.ShouldBe(2);
        waits.Asked.ShouldBe([TimeSpan.FromSeconds(2)]);
    }

    [Fact]
    public async Task Complete_GivesUpAfterThreeRetries_WaitingTwoFiveAndTenSeconds_AndThrowsTheLastFailure()
    {
        var inner = new ScriptedChatModel().ThenFail(Http(500)).ThenFail(Http(502)).ThenFail(Http(503)).ThenFail(Http(504)).ThenReply("never reached");
        var waits = new Waits();

        var exception = await Should.ThrowAsync<ChatProviderException>(() => Retrying(inner, waits).CompleteAsync(Messages, null, Token));

        exception.StatusCode.ShouldBe(504);
        inner.Calls.ShouldBe(4); // The first try and three retries.
        waits.Asked.ShouldBe([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)]);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Complete_RetriesTheTransientHttpStatuses(int status)
    {
        var inner = new ScriptedChatModel().ThenFail(Http(status)).ThenReply("ok");

        (await Retrying(inner, new Waits()).CompleteAsync(Messages, null, Token)).ShouldBe("ok");
    }

    [Theory]
    [InlineData(ChatFailureKind.Unreachable)]
    [InlineData(ChatFailureKind.TimedOut)]
    [InlineData(ChatFailureKind.StreamCut)]
    [InlineData(ChatFailureKind.EmptyReply)]
    public async Task Complete_RetriesNetworkTimeoutCutAndEmptyReplies(ChatFailureKind kind)
    {
        var inner = new ScriptedChatModel().ThenFail(Failure(kind)).ThenReply("ok");

        (await Retrying(inner, new Waits()).CompleteAsync(Messages, null, Token)).ShouldBe("ok");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task Complete_DoesNotRetryARefusal(int status)
    {
        var inner = new ScriptedChatModel().ThenFail(Http(status)).ThenReply("never reached");
        var waits = new Waits();

        var exception = await Should.ThrowAsync<ChatProviderException>(() => Retrying(inner, waits).CompleteAsync(Messages, null, Token));

        exception.StatusCode.ShouldBe(status);
        inner.Calls.ShouldBe(1);
        waits.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Complete_DoesNotRetryAContentFilter()
    {
        var inner = new ScriptedChatModel().ThenFail(new ContentFilteredException()).ThenReply("never reached");

        await Should.ThrowAsync<ContentFilteredException>(() => Retrying(inner, new Waits()).CompleteAsync(Messages, null, Token));

        inner.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Complete_DoesNotRetryAnUnreadableAnswer()
    {
        var inner = new ScriptedChatModel().ThenFail(Failure(ChatFailureKind.InvalidResponse)).ThenReply("never reached");

        await Should.ThrowAsync<ChatProviderException>(() => Retrying(inner, new Waits()).CompleteAsync(Messages, null, Token));

        inner.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Complete_DoesNotTouchAFailureThatIsNotFromTheProvider()
    {
        var inner = new ScriptedChatModel().ThenFail(new InvalidOperationException("a bug")).ThenReply("never reached");

        await Should.ThrowAsync<InvalidOperationException>(() => Retrying(inner, new Waits()).CompleteAsync(Messages, null, Token));

        inner.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Complete_StopsWaitingWhenTheCallerCancels()
    {
        var inner = new ScriptedChatModel().ThenFail(Http(503)).ThenReply("never reached");
        using var cancellation = new CancellationTokenSource();
        var model = new RetryingChatModel(
            inner,
            RetryingChatModel.DefaultDelays,
            new ListLogger<RetryingChatModel>(),
            (_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        await Should.ThrowAsync<OperationCanceledException>(() => model.CompleteAsync(Messages, null, cancellation.Token));

        inner.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Complete_LogsEachRetryWithTheKindAndTheStatus_ButNoText()
    {
        var inner = new ScriptedChatModel().ThenFail(Http(429)).ThenReply("ok");
        var logger = new ListLogger<RetryingChatModel>();

        await Retrying(inner, new Waits(), logger).CompleteAsync([new ChatMessage(ChatRole.User, "TEXT OF THE BOOK")], null, Token);

        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToList();
        warnings.Count.ShouldBe(1);
        warnings[0].ShouldContain("429");
        warnings[0].ShouldContain("Http");
        warnings[0].ShouldNotContain("TEXT OF THE BOOK");
    }

    // ---- StreamReplyAsync ----

    private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> stream)
    {
        var pieces = new List<string>();
        await foreach (var piece in stream.WithCancellation(Token))
        {
            pieces.Add(piece);
        }

        return pieces;
    }

    [Fact]
    public async Task Stream_PassesThePiecesThroughInOrder()
    {
        var inner = new ScriptedChatModel().ThenStream("Hel", "lo", "!");

        (await CollectAsync(Retrying(inner, new Waits()).StreamReplyAsync(Messages, null, Token))).ShouldBe(["Hel", "lo", "!"]);
    }

    [Fact]
    public async Task Stream_PassesTheMessagesAndTheUsageMeterToTheModel()
    {
        var inner = new ScriptedChatModel().ThenStream("x");
        var meter = new ChatUsageMeter();

        await CollectAsync(Retrying(inner, new Waits()).StreamReplyAsync(Messages, meter, Token));

        inner.LastMessages.ShouldBeSameAs(Messages);
        inner.LastUsage.ShouldBeSameAs(meter);
    }

    [Fact]
    public async Task Stream_TriesAgain_WhenAPassingFailureHappensBeforeTheFirstPiece()
    {
        var inner = new ScriptedChatModel().ThenStreamFailingAfter([], Http(429)).ThenStream("ok");
        var waits = new Waits();

        (await CollectAsync(Retrying(inner, waits).StreamReplyAsync(Messages, null, Token))).ShouldBe(["ok"]);

        inner.Calls.ShouldBe(2);
        waits.Asked.ShouldBe([TimeSpan.FromSeconds(2)]);
    }

    [Fact]
    public async Task Stream_DoesNotTryAgain_OnceAPieceWasHandedOut()
    {
        var inner = new ScriptedChatModel().ThenStreamFailingAfter(["Hel"], Failure(ChatFailureKind.StreamCut)).ThenStream("a whole other reply");
        var received = new List<string>();

        var exception = await Should.ThrowAsync<ChatProviderException>(async () =>
        {
            await foreach (var piece in Retrying(inner, new Waits()).StreamReplyAsync(Messages, null, Token))
            {
                received.Add(piece);
            }
        });

        exception.Kind.ShouldBe(ChatFailureKind.StreamCut);
        received.ShouldBe(["Hel"]); // The reader got the start, then the error: never a mixed reply.
        inner.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Stream_GivesUpAfterThreeRetries()
    {
        var inner = new ScriptedChatModel()
            .ThenStreamFailingAfter([], Http(500)).ThenStreamFailingAfter([], Http(500))
            .ThenStreamFailingAfter([], Http(500)).ThenStreamFailingAfter([], Http(502)).ThenStream("never reached");
        var waits = new Waits();

        var exception = await Should.ThrowAsync<ChatProviderException>(
            () => CollectAsync(Retrying(inner, waits).StreamReplyAsync(Messages, null, Token)));

        exception.StatusCode.ShouldBe(502);
        inner.Calls.ShouldBe(4);
        waits.Asked.ShouldBe([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)]);
    }

    [Fact]
    public async Task Stream_DoesNotRetryARefusalOrAContentFilter()
    {
        var refused = new ScriptedChatModel().ThenStreamFailingAfter([], Http(401)).ThenStream("never reached");
        var filtered = new ScriptedChatModel().ThenStreamFailingAfter([], new ContentFilteredException()).ThenStream("never reached");

        await Should.ThrowAsync<ChatProviderException>(() => CollectAsync(Retrying(refused, new Waits()).StreamReplyAsync(Messages, null, Token)));
        await Should.ThrowAsync<ContentFilteredException>(() => CollectAsync(Retrying(filtered, new Waits()).StreamReplyAsync(Messages, null, Token)));

        refused.Calls.ShouldBe(1);
        filtered.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Stream_StopsTheUnderlyingStream_WhenTheReaderStopsEarly()
    {
        var inner = new ScriptedChatModel().ThenStream("first", "second", "third");

        await foreach (var piece in Retrying(inner, new Waits()).StreamReplyAsync(Messages, null, Token))
        {
            piece.ShouldBe("first");
            break;
        }

        inner.StreamsDisposed.ShouldBe(1);
    }
}
