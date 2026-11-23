namespace LivingPaper.Evals;

// How long one call of the model of the chat took, as Langfuse recorded it: from the request to the end of the streamed reply (the judge is
// another call and is not in it), and until the first piece of text arrived (null when it was not recorded).
internal sealed record ChatTiming(DateTimeOffset Start, string Model, double LatencySeconds, double? FirstTokenSeconds);
