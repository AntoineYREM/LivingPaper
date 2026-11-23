using System.Diagnostics;
using System.Text.Json;
using LivingPaper.Application;

namespace LivingPaper.Evals;

// One root span per try, in a trace of its own, with the attributes Langfuse reads to group traces as an experiment
// (https://langfuse.com/integrations/native/opentelemetry/experiments):
//  - the experiment and the dataset, passed on by the baggage to the model calls below (TracingChatModel copies the "langfuse.*"
//    baggage of the current activity onto each call);
//  - the dataset item, its expected output, and the input and output of the try, on the root span itself.
// The root span is also the node the scores are attached to, so its span id is given back to the caller.
internal sealed class LangfuseExperimentTracer(ExperimentContext context) : IExperimentTracer
{
    public IExperimentItem BeginItem(EvalQuestion question, int repeat)
    {
        // Each try is the root of a trace of its own, whatever is running around it. With an empty parent context .NET would still take
        // Activity.Current as the parent, so it is cleared for the start and put back when the try ends.
        var outer = Activity.Current;
        Activity.Current = null;
        var root = LlmTelemetry.Source.StartActivity("experiment-item", ActivityKind.Internal, default(ActivityContext));
        if (root is null)
        {
            Activity.Current = outer;
            return NoExperimentTracer.Instance.BeginItem(question, repeat);
        }

        var spanId = root.SpanId.ToHexString();
        PutOnTheRootAndBelow(root, "langfuse.environment", LangfuseNames.Environment);
        PutOnTheRootAndBelow(root, "langfuse.trace.name", LangfuseNames.TraceName);
        PutOnTheRootAndBelow(root, "langfuse.experiment.id", context.RunId);
        PutOnTheRootAndBelow(root, "langfuse.experiment.name", context.RunId);
        PutOnTheRootAndBelow(root, "langfuse.experiment.dataset.id", context.DatasetId);
        foreach (var (key, value) in context.Metadata)
        {
            PutOnTheRootAndBelow(root, $"langfuse.experiment.metadata.{key}", value);
        }

        PutOnTheRootAndBelow(root, "langfuse.experiment.item.id", LangfuseNames.ItemId(context.DatasetName, question.Id));
        PutOnTheRootAndBelow(root, "langfuse.experiment.item.root_observation_id", spanId);

        root.SetTag("langfuse.observation.type", "span");
        root.SetTag("langfuse.experiment.description", context.Description);
        root.SetTag("langfuse.experiment.item.expected_output", JsonSerializer.Serialize(question.ExpectedOutput));
        root.SetTag("langfuse.experiment.item.metadata.category", question.Category);
        root.SetTag("langfuse.experiment.item.metadata.repeat", repeat.ToString(System.Globalization.CultureInfo.InvariantCulture));
        root.SetTag("langfuse.observation.input", JsonSerializer.Serialize(question.Input));
        return new Item(root, outer, root.TraceId.ToHexString(), spanId);
    }

    private static void PutOnTheRootAndBelow(Activity root, string key, string value)
    {
        root.SetTag(key, value);
        root.AddBaggage(key, value);
    }

    private sealed class Item(Activity root, Activity? outer, string traceId, string spanId) : IExperimentItem
    {
        public string? TraceId { get; } = traceId;

        public string? SpanId { get; } = spanId;

        public void Complete(string? reply, int? score, string? failure)
        {
            if (reply is not null)
            {
                root.SetTag("langfuse.observation.output", JsonSerializer.Serialize(reply));
            }

            if (failure is not null)
            {
                root.SetTag("langfuse.observation.level", "ERROR");
                root.SetTag("langfuse.observation.status_message", failure);
                root.SetStatus(ActivityStatusCode.Error, failure);
            }
        }

        public void Dispose()
        {
            root.Dispose();
            Activity.Current = outer;
        }
    }
}
