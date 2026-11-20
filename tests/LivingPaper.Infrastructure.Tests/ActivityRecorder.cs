using System.Diagnostics;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Infrastructure.Tests;

// Listens to the activities of the application like an exporter would, and keeps those of one provider that have ended.
// The activity source is shared by the whole process and tests run in parallel: each test gives its provider its own model name.
internal sealed class ActivityRecorder : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public ActivityRecorder(LlmProvider provider)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is LlmTelemetry.SourceName or "tracing-tests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (Tag(activity, "langfuse.observation.model.name") == provider.Model)
                {
                    lock (_activities)
                    {
                        _activities.Add(activity);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Generations
    {
        get
        {
            lock (_activities)
            {
                return [.. _activities];
            }
        }
    }

    public static string? Tag(Activity activity, string key) => activity.GetTagItem(key)?.ToString();

    public void Dispose() => _listener.Dispose();
}
