namespace LivingPaper.Application;

// The outcome of asking a provider for its models. Like ConnectionTestResult, it never contains the key or the address.
// Failure NoModelList: the provider answered, but has no usable list (HTTP 404, 405, 501 or an answer that is no list): the
// user can still type the name of a model. The other failures say that the address or the key must be fixed.
public sealed record ModelListResult(IReadOnlyList<string> Models, ConnectionFailure? Failure, int? HttpStatus)
{
    public bool Success => Failure is null;

    public static ModelListResult Found(IReadOnlyList<string> models) => new(models, null, null);

    public static ModelListResult Failed(ConnectionFailure failure, int? httpStatus = null) => new([], failure, httpStatus);
}
