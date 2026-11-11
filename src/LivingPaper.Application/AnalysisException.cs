namespace LivingPaper.Application;

// An analysis step failed for a reason that stops the analysis. The detail holds no passage of the book and no secret.
public sealed class AnalysisException(AnalysisFailure failure, string? detail = null) : Exception($"The analysis stopped: {failure}.")
{
    public AnalysisFailure Failure { get; } = failure;

    public string? Detail { get; } = detail;
}
