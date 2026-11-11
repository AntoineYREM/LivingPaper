using LivingPaper.Domain;

namespace LivingPaper.Application;

// The outcome of adding a language model: it was saved (Added), or it was refused because of fields to fix (Errors),
// or the connection test failed (Connection). Nothing is saved unless the test succeeded.
public sealed record AddProviderResult(LlmProviderSummary? Added, IReadOnlyList<FieldError> Errors, ConnectionTestResult? Connection)
{
    public bool Succeeded => Added is not null;
}
