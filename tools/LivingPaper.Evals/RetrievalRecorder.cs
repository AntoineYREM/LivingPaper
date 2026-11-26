using System.Runtime.CompilerServices;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Evals;

// What one try was made from and what it cost. Query, Passages and Distances: what was searched and found, null with the whole story
// (or when the search failed, for the passages). ExpectedFound: whether a phrase of the question is in a passage found; null when
// nothing was to be searched or the question gives no phrase. Notices: what the reader would have been told (ChatNotice).
// ReplyTokens are those of the reply (its input is mostly the system prompt), QueryTokens those of the writing of the query.
internal sealed record TryTrace(
    string? Query,
    IReadOnlyList<int>? Passages,
    IReadOnlyList<double>? Distances,
    bool? ExpectedFound,
    IReadOnlyList<string>? Notices,
    TokenUsage ReplyTokens,
    TokenUsage? QueryTokens,
    double Seconds);

// Stands between the chat of the application and its models and index: it passes every call on, and keeps what the try in progress
// searched, found and cost. Tries are made one at a time (Begin, then the reply, then Trace). Without a model for vectorization and an
// index (the whole story), it only counts the tokens. Every token is also added to the total of the run.
internal sealed class RetrievalRecorder(
    IChatModelFactory models, IEmbeddingModelFactory? embeddings = null, IPassageIndex? index = null, ChatUsageMeter? total = null)
    : IChatModelFactory, IEmbeddingModelFactory, IPassageIndex
{
    private ChatUsageMeter _reply = new();
    private ChatUsageMeter? _query;
    private string? _text;
    private IReadOnlyList<FoundPassage>? _found;

    private IEmbeddingModelFactory Embeddings => embeddings ?? throw new InvalidOperationException("No model for vectorization was given.");

    private IPassageIndex Index => index ?? throw new InvalidOperationException("No index was given.");

    public void Begin() => (_reply, _query, _text, _found) = (new ChatUsageMeter(), null, null, null);

    public TryTrace Trace(EvalQuestion question, IReadOnlyList<ChatNotice> notices, TimeSpan elapsed)
    {
        bool? expectedFound = index is null || question.PassageQuotes is not { } quotes
            ? null
            : _found?.Any(found => quotes.Any(quote => PassageQuotes.Appears(quote, found.Passage.Text))) ?? false;
        return new TryTrace(
            _text,
            _found?.Select(found => found.Passage.Number).ToList(),
            _found?.Select(found => Math.Round(found.Distance, 4)).ToList(),
            expectedFound,
            notices.Count == 0 ? null : [.. notices.Select(notice => notice.ToString())],
            TokenUsage.From(_reply),
            _query is null ? null : TokenUsage.From(_query),
            Math.Round(elapsed.TotalSeconds, 1));
    }

    IChatModel IChatModelFactory.Create(LlmProvider provider, bool retryPassingFailures) =>
        new Chat(this, models.Create(provider, retryPassingFailures));

    IEmbeddingModel IEmbeddingModelFactory.Create(LlmProvider provider, bool retryPassingFailures) =>
        new Vectors(this, Embeddings.Create(provider, retryPassingFailures));

    public PassageIndexProblem? Check() => Index.Check();

    public Task SavePassagesAsync(BookKey book, IReadOnlyList<StoredPassage> passages, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The evaluation tool does not change the index.");

    public Task SaveVectorsAsync(BookKey book, string embeddingModel, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The evaluation tool does not change the index.");

    public Task DeleteAsync(BookKey book, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The evaluation tool does not change the index.");

    public Task<PassageIndexState?> GetStateAsync(BookKey book, CancellationToken cancellationToken) =>
        Index.GetStateAsync(book, cancellationToken);

    public Task<IReadOnlyList<StoredPassage>> GetPassagesAsync(BookKey book, CancellationToken cancellationToken) =>
        Index.GetPassagesAsync(book, cancellationToken);

    public async Task<IReadOnlyList<FoundPassage>> SearchAsync(BookKey book, float[] query, int count, CancellationToken cancellationToken)
    {
        var found = await Index.SearchAsync(book, query, count, cancellationToken).ConfigureAwait(false);
        _found = found.Count == 0 ? null : found;
        return found;
    }

    // The counts of one call, added to the meter of its kind and to the total of the run.
    private void Count(ChatUsageMeter call, ChatUsageMeter into)
    {
        into.Add(call.InputTokens, call.CachedInputTokens, call.OutputTokens);
        total?.Add(call.InputTokens, call.CachedInputTokens, call.OutputTokens);
    }

    // The reply is streamed; the only other call of the chat writes the search query.
    private sealed class Chat(RetrievalRecorder owner, IChatModel model) : IChatModel
    {
        public async IAsyncEnumerable<string> StreamReplyAsync(
            IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var call = new ChatUsageMeter();
            try
            {
                await foreach (var piece in model.StreamReplyAsync(messages, call, cancellationToken).ConfigureAwait(false))
                {
                    yield return piece;
                }
            }
            finally
            {
                // A reply that fails half way has cost tokens too.
                owner.Count(call, owner._reply);
            }
        }

        public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
        {
            var call = new ChatUsageMeter();
            try
            {
                return await model.CompleteAsync(messages, call, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                owner.Count(call, owner._query ??= new ChatUsageMeter());
            }
        }
    }

    private sealed class Vectors(RetrievalRecorder owner, IEmbeddingModel model) : IEmbeddingModel
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, ChatUsageMeter? usage, CancellationToken cancellationToken)
        {
            owner._text = texts.Count > 0 ? texts[0] : null;
            return model.EmbedAsync(texts, usage, cancellationToken);
        }
    }
}
