using System.Security.Cryptography;

namespace LivingPaper.Application;

// Keeps, for a few minutes and in memory only, a language model between the two steps of adding it: "get the models" and
// "save the chosen one". The key travels from the form to here and never back to the browser; the page only carries a
// random identifier. Nothing is written to disk: a restart forgets every draft, and the user starts again.
// Few drafts, short life: a forgotten draft does not keep a key in memory for long.
public sealed class PendingProviderStore(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public const int MaxDrafts = 5;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    // The identifier is 128 random bits: it cannot be guessed.
    public PendingProviderView Add(string name, string baseUrl, string apiKey, IReadOnlyList<string> models)
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var now = clock.GetUtcNow();
        lock (_lock)
        {
            Purge(now);
            while (_entries.Count >= MaxDrafts)
            {
                var oldest = _entries.MinBy(pair => pair.Value.Created).Key;
                _entries.Remove(oldest);
            }

            var view = new PendingProviderView(id, name, baseUrl, models);
            _entries[id] = new Entry(view, apiKey, now);
            return view;
        }
    }

    public PendingProviderView? Get(string? id) => Find(id)?.View;

    // The draft with its key, for the server to save it. Null when there is no such draft, or it expired.
    internal (PendingProviderView View, string ApiKey)? Take(string? id) =>
        Find(id) is { } entry ? (entry.View, entry.ApiKey) : null;

    public void Remove(string? id)
    {
        if (id is null)
        {
            return;
        }

        lock (_lock)
        {
            _entries.Remove(id);
        }
    }

    private Entry? Find(string? id)
    {
        if (id is null)
        {
            return null;
        }

        lock (_lock)
        {
            Purge(clock.GetUtcNow());
            return _entries.GetValueOrDefault(id);
        }
    }

    private void Purge(DateTimeOffset now)
    {
        foreach (var id in _entries.Where(pair => now - pair.Value.Created > Lifetime).Select(pair => pair.Key).ToList())
        {
            _entries.Remove(id);
        }
    }

    private sealed record Entry(PendingProviderView View, string ApiKey, DateTimeOffset Created)
    {
        // Never prints the key.
        public override string ToString() => $"Entry {View.Id}";
    }
}
