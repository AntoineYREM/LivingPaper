namespace LivingPaper.Persistence;

internal static class AtomicFile
{
    // Writes to a temporary file in the same directory, then renames it over the target. A crash in the middle of the
    // write leaves the previous file intact (the rename is atomic on one volume) and, at worst, a stray .tmp file.
    // The rename takes turns with the reads of the file (see SharedFile).
    public static async Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            await SharedFile.ExclusiveAsync(path, () => ReplaceAsync(temporary, path, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    // On Windows the rename can still fail for a moment while another program (an antivirus, a backup) holds the target open.
    // It is tried a few times, a few milliseconds apart, before the failure is reported.
    private static async Task ReplaceAsync(string temporary, string path, CancellationToken cancellationToken)
    {
        const int attempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (attempt < attempts && exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
