using LivingPaper.Domain;

namespace LivingPaper.Application;

// The outcome of "get the models": a draft to choose from (Pending), or fields to fix (Errors), or the reason why the
// provider did not give its models (Failure, with the HTTP status when there is one).
public sealed record FetchModelsResult(PendingProviderView? Pending, IReadOnlyList<FieldError> Errors, ConnectionFailure? Failure, int? HttpStatus)
{
    public bool Succeeded => Pending is not null;
}
