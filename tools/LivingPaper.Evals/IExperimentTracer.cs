namespace LivingPaper.Evals;

// The trace of one try (a question, once) as the experiment of Langfuse wants it. Without Langfuse nothing is traced: the runner
// calls the same methods on an item that does nothing.
internal interface IExperimentTracer
{
    // Starts the trace of a try. The caller disposes the item when the try is over; everything done in between (the answer of the
    // character, the grade of the judge) is a child of it.
    IExperimentItem BeginItem(EvalQuestion question, int repeat);
}

internal interface IExperimentItem : IDisposable
{
    // The ids of the trace and of its root span, as Langfuse knows them. Null when nothing is traced.
    string? TraceId { get; }

    string? SpanId { get; }

    // The output of the try: the reply and its score, or the failure.
    void Complete(string? reply, int? score, string? failure);
}

internal sealed class NoExperimentTracer : IExperimentTracer
{
    public static NoExperimentTracer Instance { get; } = new();

    public IExperimentItem BeginItem(EvalQuestion question, int repeat) => NoExperimentItem.Instance;

    private sealed class NoExperimentItem : IExperimentItem
    {
        public static NoExperimentItem Instance { get; } = new();

        public string? TraceId => null;

        public string? SpanId => null;

        public void Complete(string? reply, int? score, string? failure)
        {
        }

        public void Dispose()
        {
        }
    }
}
