namespace LivingPaper.Application;

// The outcome of a connection test. It never contains the key or the address: it is shown to the user and logged.
// HttpStatus: for failures that come with one (401, 404, 503...). Detail: short technical text in English, only for
// ConnectionFailure.PromptFile (the file name and the prompt to fix).
public sealed record ConnectionTestResult(bool Success, ConnectionFailure? Failure, int? HttpStatus, string? Detail)
{
    // The provider answered no, and would say no again: a 4xx other than "too slow" and "too many requests", or "not
    // implemented". A slow, unreachable or failing provider is not a refusal.
    public bool IsRefusal => HttpStatus is (>= 400 and < 500 and not 408 and not 429) or 501;

    public static ConnectionTestResult Ok() => new(true, null, null, null);

    public static ConnectionTestResult Failed(ConnectionFailure failure, int? httpStatus = null, string? detail = null) =>
        new(false, failure, httpStatus, detail);
}
