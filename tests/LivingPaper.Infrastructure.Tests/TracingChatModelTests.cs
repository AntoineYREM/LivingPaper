using System.Diagnostics;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Infrastructure.Tests;

// TracingChatModel records every call to a language model as an Activity (a "generation" for Langfuse), without ever
// changing what the call does. The tests listen to the activities with an ActivityListener, like an exporter would.
public class TracingChatModelTests
{
    private static readonly IReadOnlyList<ChatMessage> Messages = [new(ChatRole.System, "You are Cyrano."), new(ChatRole.User, "Hello there")];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Each test has its own model name: the activity source is shared by the whole process and tests run in parallel.
    private static LlmProvider NewProvider() => new("id", "My provider", "https://example.test/v1", "sk-secret-key", "model-" + Guid.NewGuid().ToString("N"));

    private static TracingChatModel Tracing(IChatModel inner, LlmProvider provider, bool captureContent = false) =>
        new(inner, provider, new LlmTracingOptions { CaptureContent = captureContent });

    private static string? Tag(Activity activity, string key) => ActivityRecorder.Tag(activity, key);

    // ---- CompleteAsync ----

    [Fact]
    public async Task Complete_ReturnsTheReplyOfTheModel_AndRecordsOneGeneration()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var inner = new ScriptedChatModel().ThenReply("Hi!");

        var reply = await Tracing(inner, provider).CompleteAsync(Messages, null, Token);

        reply.ShouldBe("Hi!");
        var generation = recorder.Generations.ShouldHaveSingleItem();
        Tag(generation, "langfuse.observation.type").ShouldBe("generation");
        Tag(generation, "langfuse.observation.model.name").ShouldBe(provider.Model);
        Tag(generation, "langfuse.observation.metadata.provider").ShouldBe("My provider");
        generation.OperationName.ShouldBe("llm-call");
    }

    [Fact]
    public async Task Complete_RecordsTheTokensOfTheCall()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var inner = new ScriptedChatModel().ThenReply("Hi!").WithUsage(input: 120, cachedInput: 20, output: 34);

        await Tracing(inner, provider).CompleteAsync(Messages, null, Token);

        Tag(recorder.Generations.Single(), "langfuse.observation.usage_details").ShouldBe("""{"input":120,"output":34}""");
    }

    [Fact]
    public async Task Complete_AddsTheTokensToTheMeterOfTheCaller_AsWithoutTheDecorator()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var inner = new ScriptedChatModel().ThenReply("a").ThenReply("b").WithUsage(input: 100, cachedInput: 10, output: 5);
        var meter = new ChatUsageMeter();
        var model = Tracing(inner, provider);

        await model.CompleteAsync(Messages, meter, Token);
        await model.CompleteAsync(Messages, meter, Token);

        (meter.InputTokens, meter.CachedInputTokens, meter.OutputTokens).ShouldBe((200, 20, 10));
    }

    [Fact]
    public async Task Complete_RecordsNoTokens_WhenTheProviderReportsNone()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);

        await Tracing(new ScriptedChatModel().ThenReply("Hi!"), provider).CompleteAsync(Messages, null, Token);

        recorder.Generations.Single().GetTagItem("langfuse.observation.usage_details").ShouldBeNull();
    }

    [Fact]
    public async Task Complete_DoesNotRecordAnyText_ByDefault()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var inner = new ScriptedChatModel().ThenReply("A secret reply from the book");

        await Tracing(inner, provider).CompleteAsync(Messages, null, Token);

        var generation = recorder.Generations.Single();
        generation.GetTagItem("langfuse.observation.input").ShouldBeNull();
        generation.GetTagItem("langfuse.observation.output").ShouldBeNull();
        generation.Tags.Select(tag => tag.Value?.ToString()).ShouldAllBe(value => value == null || !value.Contains("secret reply") && !value.Contains("Hello there") && !value.Contains("Cyrano"));
    }

    [Fact]
    public async Task Complete_RecordsThePromptAndTheReply_WhenTheContentIsCaptured()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var inner = new ScriptedChatModel().ThenReply("Hi!");

        await Tracing(inner, provider, captureContent: true).CompleteAsync(Messages, null, Token);

        var generation = recorder.Generations.Single();
        Tag(generation, "langfuse.observation.input").ShouldBe("""[{"role":"system","content":"You are Cyrano."},{"role":"user","content":"Hello there"}]""");
        Tag(generation, "langfuse.observation.output").ShouldBe("Hi!");
    }

    [Fact]
    public async Task Complete_RethrowsTheFailure_AndRecordsAnErrorWithItsKindOnly()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var failure = new ChatProviderException(ChatFailureKind.Http, "The language model answered HTTP 503.", 503);
        var inner = new ScriptedChatModel().ThenFail(failure);

        var thrown = await Should.ThrowAsync<ChatProviderException>(() => Tracing(inner, provider).CompleteAsync(Messages, null, Token));

        thrown.ShouldBeSameAs(failure);
        var generation = recorder.Generations.Single();
        Tag(generation, "langfuse.observation.level").ShouldBe("ERROR");
        Tag(generation, "langfuse.observation.status_message").ShouldBe("Http (HTTP 503)");
        generation.Status.ShouldBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Complete_RecordsAWarning_WhenTheCallIsCancelled()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var inner = new ScriptedChatModel().ThenFail(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => Tracing(inner, provider).CompleteAsync(Messages, null, Token));

        Tag(recorder.Generations.Single(), "langfuse.observation.level").ShouldBe("WARNING");
    }

    [Fact]
    public async Task Complete_IsAChildOfTheCurrentActivity_SoAnAnalysisIsOneTrace()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        using var source = new ActivitySource("tracing-tests");
        using var step = source.StartActivity("read-part");
        var inner = new ScriptedChatModel().ThenReply("Hi!");

        await Tracing(inner, provider).CompleteAsync(Messages, null, Token);

        var generation = recorder.Generations.Single();
        generation.ParentId.ShouldBe(step!.Id);
        generation.TraceId.ShouldBe(step.TraceId);
        Activity.Current.ShouldBeSameAs(step);
    }

    [Fact]
    public async Task Complete_IsItsOwnTrace_WithoutACurrentActivity()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        Activity.Current = null;

        await Tracing(new ScriptedChatModel().ThenReply("Hi!"), provider).CompleteAsync(Messages, null, Token);

        recorder.Generations.Single().Parent.ShouldBeNull();
    }

    // ---- StreamReplyAsync ----

    private static async Task<List<string>> ReadAllAsync(IAsyncEnumerable<string> pieces)
    {
        var read = new List<string>();
        await foreach (var piece in pieces.WithCancellation(Token))
        {
            read.Add(piece);
        }

        return read;
    }

    [Fact]
    public async Task Stream_YieldsThePiecesOfTheModel_AndRecordsOneGeneration()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var inner = new ScriptedChatModel().ThenStream("Hel", "lo", "!").WithUsage(input: 50, cachedInput: 0, output: 3);

        var pieces = await ReadAllAsync(Tracing(inner, provider).StreamReplyAsync(Messages, null, Token));

        pieces.ShouldBe(["Hel", "lo", "!"]);
        var generation = recorder.Generations.ShouldHaveSingleItem();
        generation.OperationName.ShouldBe("chat-reply");
        Tag(generation, "langfuse.observation.type").ShouldBe("generation");
        Tag(generation, "langfuse.observation.usage_details").ShouldBe("""{"input":50,"output":3}""");
        generation.GetTagItem("langfuse.observation.completion_start_time").ShouldNotBeNull();
        generation.GetTagItem("langfuse.observation.level").ShouldBeNull();
    }

    [Fact]
    public async Task Stream_AddsTheTokensToTheMeterOfTheCaller()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var inner = new ScriptedChatModel().ThenStream("Hi").WithUsage(input: 7, cachedInput: 2, output: 1);
        var meter = new ChatUsageMeter();

        await ReadAllAsync(Tracing(inner, provider).StreamReplyAsync(Messages, meter, Token));

        (meter.InputTokens, meter.CachedInputTokens, meter.OutputTokens).ShouldBe((7, 2, 1));
    }

    [Fact]
    public async Task Stream_RecordsTheWholeReply_OnlyWhenTheContentIsCaptured()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var captured = NewProvider();
        using var capturedActivityRecorder = new ActivityRecorder(captured);

        await ReadAllAsync(Tracing(new ScriptedChatModel().ThenStream("Hel", "lo"), provider).StreamReplyAsync(Messages, null, Token));
        await ReadAllAsync(Tracing(new ScriptedChatModel().ThenStream("Hel", "lo"), captured, captureContent: true).StreamReplyAsync(Messages, null, Token));

        recorder.Generations.Single().GetTagItem("langfuse.observation.output").ShouldBeNull();
        recorder.Generations.Single().GetTagItem("langfuse.observation.input").ShouldBeNull();
        Tag(capturedActivityRecorder.Generations.Single(), "langfuse.observation.output").ShouldBe("Hello");
        Tag(capturedActivityRecorder.Generations.Single(), "langfuse.observation.input")!.ShouldContain("Hello there");
    }

    [Fact]
    public async Task Stream_RecordsAnError_AndRethrows_WhenTheStreamFailsAfterSomeText()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var failure = new ChatProviderException(ChatFailureKind.StreamCut, "The reply of the language model ended before its end.");
        var inner = new ScriptedChatModel().ThenStreamFailingAfter(["Hel"], failure);
        var seen = new List<string>();

        var thrown = await Should.ThrowAsync<ChatProviderException>(async () =>
        {
            await foreach (var piece in Tracing(inner, provider).StreamReplyAsync(Messages, null, Token))
            {
                seen.Add(piece);
            }
        });

        thrown.ShouldBeSameAs(failure);
        seen.ShouldBe(["Hel"]);
        var generation = recorder.Generations.Single();
        Tag(generation, "langfuse.observation.level").ShouldBe("ERROR");
        Tag(generation, "langfuse.observation.status_message").ShouldBe("StreamCut");
    }

    [Fact]
    public async Task Stream_RecordsAnError_WhenItFailsBeforeAnyText()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var inner = new ScriptedChatModel().ThenStreamFailingAfter([], new ChatProviderException(ChatFailureKind.Http, "The language model answered HTTP 429.", 429));

        await Should.ThrowAsync<ChatProviderException>(() => ReadAllAsync(Tracing(inner, provider).StreamReplyAsync(Messages, null, Token)));

        var generation = recorder.Generations.Single();
        Tag(generation, "langfuse.observation.level").ShouldBe("ERROR");
        Tag(generation, "langfuse.observation.status_message").ShouldBe("Http (HTTP 429)");
        generation.GetTagItem("langfuse.observation.completion_start_time").ShouldBeNull();
    }

    [Fact]
    public async Task Stream_RecordsAWarning_WhenTheReaderStopsBeforeTheEnd()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        var inner = new ScriptedChatModel().ThenStream("one", "two", "three");

        await foreach (var piece in Tracing(inner, provider).StreamReplyAsync(Messages, null, Token))
        {
            piece.ShouldBe("one");
            break;
        }

        var generation = recorder.Generations.Single();
        Tag(generation, "langfuse.observation.level").ShouldBe("WARNING");
        inner.StreamsDisposed.ShouldBe(1);
    }

    [Fact]
    public async Task Stream_LeavesTheCurrentActivityOfTheReaderAlone_BetweenThePieces()
    {
        var provider = NewProvider();
        using var recorder = new ActivityRecorder(provider);
        using var source = new ActivitySource("tracing-tests");
        using var request = source.StartActivity("request");
        var inner = new ScriptedChatModel().ThenStream("a", "b");

        await foreach (var _ in Tracing(inner, provider).StreamReplyAsync(Messages, null, Token))
        {
            Activity.Current.ShouldBeSameAs(request);
        }

        Activity.Current.ShouldBeSameAs(request);
        recorder.Generations.Single().ParentId.ShouldBe(request!.Id);
    }

    // ---- Nothing listens ----

    [Fact]
    public async Task WithoutAnyListener_TheCallsBehaveExactlyAsWithoutTheDecorator()
    {
        var provider = NewProvider();
        var inner = new ScriptedChatModel().ThenReply("Hi!").ThenStream("a", "b").WithUsage(input: 3, cachedInput: 1, output: 2);
        var meter = new ChatUsageMeter();
        var model = Tracing(inner, provider);

        (await model.CompleteAsync(Messages, meter, Token)).ShouldBe("Hi!");
        (await ReadAllAsync(model.StreamReplyAsync(Messages, meter, Token))).ShouldBe(["a", "b"]);

        (meter.InputTokens, meter.CachedInputTokens, meter.OutputTokens).ShouldBe((6, 2, 4));
    }
}
