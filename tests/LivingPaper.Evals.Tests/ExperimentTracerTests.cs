using System.Diagnostics;
using LivingPaper.Application;
using Shouldly;

namespace LivingPaper.Evals.Tests;

// The traces of an experiment as Langfuse wants them (OpenTelemetry attributes): one root span per question and per try, which holds the
// experiment, the dataset item and the input and output, and passes the identity of the experiment to the model calls below it.
public sealed class ExperimentTracerTests : IDisposable
{
    private readonly List<Activity> _stopped = [];
    private readonly ActivityListener _listener;

    public ExperimentTracerTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LlmTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private static readonly ExperimentContext Context = new(
        "20261123-140512-gpt-5-mini",
        "madame-bovary-emma",
        "ds-1",
        "Questions put to Emma.",
        new Dictionary<string, string> { ["chat_model"] = "gpt-5-mini", ["repeats"] = "3" });

    private static readonly EvalQuestion Question = new("bovary-role-01", "role-break-trap", "Are you an AI?", "Stays Emma.");

    private static string Tag(Activity activity, string key) => activity.GetTagItem(key)?.ToString() ?? string.Empty;

    [Fact]
    public void BeginItem_StartsANewTraceWithTheExperimentAndTheItem()
    {
        using var outer = new Activity("outer").Start();
        using var item = new LangfuseExperimentTracer(Context).BeginItem(Question, repeat: 2);
        item.Complete("Non.", 5, null);
        item.Dispose();

        var root = _stopped.Single(activity => activity.OperationName == "experiment-item");
        root.TraceId.ToString().ShouldNotBe(outer.TraceId.ToString());
        root.ParentSpanId.ToString().ShouldBe(default(ActivitySpanId).ToString());
        Tag(root, "langfuse.experiment.id").ShouldBe("20261123-140512-gpt-5-mini");
        Tag(root, "langfuse.experiment.name").ShouldBe("20261123-140512-gpt-5-mini");
        Tag(root, "langfuse.experiment.dataset.id").ShouldBe("ds-1");
        Tag(root, "langfuse.experiment.description").ShouldBe("Questions put to Emma.");
        Tag(root, "langfuse.experiment.metadata.chat_model").ShouldBe("gpt-5-mini");
        Tag(root, "langfuse.experiment.item.id").ShouldBe(LangfuseNames.ItemId("madame-bovary-emma", "bovary-role-01"));
        Tag(root, "langfuse.experiment.item.expected_output").ShouldBe("\"Stays Emma.\"");
        Tag(root, "langfuse.experiment.item.metadata.category").ShouldBe("role-break-trap");
        Tag(root, "langfuse.observation.input").ShouldBe("\"Are you an AI?\"");
        Tag(root, "langfuse.observation.output").ShouldBe("\"Non.\"");
        Tag(root, "langfuse.environment").ShouldBe("experiment");
    }

    [Fact]
    public void BeginItem_TheRootObservationIdIsTheSpanIdOfTheRoot()
    {
        using var item = new LangfuseExperimentTracer(Context).BeginItem(Question, 1);
        var spanId = item.SpanId;
        var traceId = item.TraceId;
        item.Dispose();

        var root = _stopped.Single(activity => activity.OperationName == "experiment-item");
        Tag(root, "langfuse.experiment.item.root_observation_id").ShouldBe(spanId);
        root.SpanId.ToHexString().ShouldBe(spanId);
        root.TraceId.ToHexString().ShouldBe(traceId);
        spanId!.Length.ShouldBe(16);
        traceId!.Length.ShouldBe(32);
    }

    [Fact]
    public void BeginItem_PassesTheIdentityOfTheExperimentToTheCallsBelow()
    {
        using var item = new LangfuseExperimentTracer(Context).BeginItem(Question, 1);

        // TracingChatModel copies the "langfuse.*" baggage of the current activity onto each model call.
        var baggage = Activity.Current!.Baggage.ToDictionary(pair => pair.Key, pair => pair.Value);
        baggage["langfuse.experiment.id"].ShouldBe("20261123-140512-gpt-5-mini");
        baggage["langfuse.experiment.dataset.id"].ShouldBe("ds-1");
        baggage["langfuse.experiment.item.id"].ShouldBe(LangfuseNames.ItemId("madame-bovary-emma", "bovary-role-01"));
        baggage["langfuse.experiment.item.root_observation_id"].ShouldBe(item.SpanId);
        baggage["langfuse.experiment.metadata.repeats"].ShouldBe("3");
        baggage["langfuse.environment"].ShouldBe("experiment");
    }

    [Fact]
    public async Task AModelCallMadeDuringATry_IsAChildOfTheRootAndCarriesTheExperiment()
    {
        var provider = new LivingPaper.Domain.LlmProvider("id", "P", "https://x.example/v1", "key", "gpt-5-mini");
        var model = new LivingPaper.Infrastructure.TracingChatModel(new FakeChatModel(_ => "reply"), provider, new LivingPaper.Infrastructure.LlmTracingOptions());
        string? traceId;
        string? spanId;
        using (var item = new LangfuseExperimentTracer(Context).BeginItem(Question, 1))
        {
            traceId = item.TraceId;
            spanId = item.SpanId;
            await model.CompleteAsync([new ChatMessage(ChatRole.User, "hi")], usage: null, TestContext.Current.CancellationToken);
        }

        var call = _stopped.Single(activity => activity.OperationName == "llm-call");
        call.TraceId.ToHexString().ShouldBe(traceId);
        call.ParentSpanId.ToHexString().ShouldBe(spanId);
        Tag(call, "langfuse.experiment.id").ShouldBe("20261123-140512-gpt-5-mini");
        Tag(call, "langfuse.experiment.dataset.id").ShouldBe("ds-1");
        Tag(call, "langfuse.experiment.item.root_observation_id").ShouldBe(spanId);
        Tag(call, "langfuse.environment").ShouldBe("experiment");
    }

    [Fact]
    public void Complete_RecordsAFailureAsAnError()
    {
        using var item = new LangfuseExperimentTracer(Context).BeginItem(Question, 1);
        item.Complete(null, null, "reply:ModelUnreachable");
        item.Dispose();

        var root = _stopped.Single(activity => activity.OperationName == "experiment-item");
        Tag(root, "langfuse.observation.level").ShouldBe("ERROR");
        Tag(root, "langfuse.observation.status_message").ShouldBe("reply:ModelUnreachable");
    }

    [Fact]
    public void TheTraceOfEachTry_IsItsOwn()
    {
        var tracer = new LangfuseExperimentTracer(Context);
        string?[] ids;
        using (var first = tracer.BeginItem(Question, 1))
        {
            ids = [first.TraceId];
        }

        using var second = tracer.BeginItem(Question, 2);

        second.TraceId.ShouldNotBe(ids[0]);
    }

    [Fact]
    public void WithoutTracing_ThereIsNoTraceAndNothingHappens()
    {
        using var item = NoExperimentTracer.Instance.BeginItem(Question, 1);

        item.TraceId.ShouldBeNull();
        item.SpanId.ShouldBeNull();
        item.Complete("x", 1, null);
    }
}
