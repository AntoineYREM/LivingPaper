namespace LivingPaper.Persistence;

// Reading a file that another thread may be replacing at this very moment (see AtomicFile). On Windows a file that is open
// cannot be replaced, and a file that is being replaced cannot be opened: the analysis, which writes the book at each step,
// would fail whenever the page reads it to show the progress, or the other way round. Every read and every replace of a
// file in this application goes through the same lock, so they take turns. A read is a few milliseconds long.
// The locks are shared by a few dozen stripes chosen from the path: two files may share one, which only makes them wait for
// each other a little, and there is nothing to clean up when a book is deleted.
internal static class SharedFile
{
    private const int Stripes = 64;

    private static readonly SemaphoreSlim[] Gates = [.. Enumerable.Range(0, Stripes).Select(_ => new SemaphoreSlim(1, 1))];

    public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        var gate = GateOf(path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    // Runs the replacement of a file while no read of it is going on.
    public static async Task ExclusiveAsync(string path, Func<Task> change, CancellationToken cancellationToken)
    {
        var gate = GateOf(path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await change().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    // Deletes a file (nothing to do when it is not there) while no read of it is going on. File.Delete throws when the
    // directory itself does not exist yet, which is why the existence is checked first.
    public static Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken) =>
        ExclusiveAsync(
            path,
            () =>
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return Task.CompletedTask;
            },
            cancellationToken);

    private static SemaphoreSlim GateOf(string path) =>
        Gates[(int)((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(Path.GetFullPath(path)) % Stripes)];
}
