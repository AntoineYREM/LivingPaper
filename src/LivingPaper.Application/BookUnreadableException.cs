namespace LivingPaper.Application;

public sealed class BookUnreadableException(string storageName, string reason)
    : Exception($"The book '{storageName}' cannot be read: {reason}")
{
    public string StorageName { get; } = storageName;

    public string Reason { get; } = reason;
}
