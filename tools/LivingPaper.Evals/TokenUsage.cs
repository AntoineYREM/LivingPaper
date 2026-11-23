using LivingPaper.Application;

namespace LivingPaper.Evals;

// The tokens a role (the chat, or the judge) used during a run, as the provider reported them. A provider that reports nothing adds 0.
internal sealed record TokenUsage(int Input, int CachedInput, int Output)
{
    public static TokenUsage From(ChatUsageMeter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        return new TokenUsage(meter.InputTokens, meter.CachedInputTokens, meter.OutputTokens);
    }
}
