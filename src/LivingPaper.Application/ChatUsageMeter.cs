namespace LivingPaper.Application;

// Adds up the tokens of several calls to a language model: one analysis makes many calls, some at the same time,
// hence the lock. The provider reports the counts when it can; calls that report nothing add nothing.
public sealed class ChatUsageMeter
{
    private readonly Lock _lock = new();
    private int _input;
    private int _cachedInput;
    private int _output;

    public int InputTokens
    {
        get { lock (_lock) { return _input; } }
    }

    public int CachedInputTokens
    {
        get { lock (_lock) { return _cachedInput; } }
    }

    public int OutputTokens
    {
        get { lock (_lock) { return _output; } }
    }

    public void Add(int input, int cachedInput, int output)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(input);
        ArgumentOutOfRangeException.ThrowIfNegative(cachedInput);
        ArgumentOutOfRangeException.ThrowIfNegative(output);
        lock (_lock)
        {
            _input += input;
            _cachedInput += cachedInput;
            _output += output;
        }
    }
}
