using System.Diagnostics;
using LivingPaper.Domain;

namespace LivingPaper.Application;

// Turns the passages kept for a book into vectors, with the model chosen for vectorization, and says where a book is with
// them. Used at the end of an analysis and when the user asks to index a book again. The vectors replace the earlier ones
// only when all of them are made. A failure never stops what called it: it is written on the book.
public sealed class BookIndexer(IPassageIndex index, LlmSettingsService settings, IEmbeddingModelFactory embeddings, AnalysisOptions options)
{
    // update: changes the book and saves it, one change at a time (the caller knows how). Returns null when the passages
    // are indexed (or there is none), else why not. Throws OperationCanceledException when cancelled.
    public async Task<IndexFailure?> IndexAsync(BookKey key, Func<Action<Book>, CancellationToken, Task> update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(update);

        // Only the number of passages and the code of a failure: never a text of the book.
        using var span = LlmTelemetry.Source.StartActivity("index-passages");
        var usage = new ChatUsageMeter();
        IndexFailure? failure;
        try
        {
            failure = await VectorizeAsync(key, update, usage, span, cancellationToken).ConfigureAwait(false);
        }
        catch (ChatProviderException exception)
        {
            failure = Classify(exception);
        }
        catch (PassageIndexException)
        {
            failure = IndexFailure.IndexUnusable;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The kind of error only: its message could hold a passage.
            span?.SetTag("index.error", exception.GetType().Name);
            failure = IndexFailure.Unexpected;
        }

        if (failure is not null)
        {
            span?.SetStatus(ActivityStatusCode.Error, failure.Value.ToString());
        }

        // The tokens were spent even when the indexing failed.
        await update(
            book =>
            {
                book.IndexDone = book.IndexTotal = 0;
                book.IndexErrorCode = failure?.ToString();
                book.EmbeddingTokens += usage.InputTokens;
            },
            cancellationToken).ConfigureAwait(false);
        return failure;
    }

    // What the page of a book says about its passages. Null when the feature is off (no model for vectorization, or an index
    // that cannot be used): the page then says nothing. queued: an indexing of the book is waiting or running.
    public async Task<PassagesView?> DescribeAsync(Book book, bool queued, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (index.Check() is not null)
        {
            return null;
        }

        try
        {
            if (await settings.GetEmbeddingProviderAsync(cancellationToken).ConfigureAwait(false) is not { } provider)
            {
                return null;
            }

            var failure = Enum.TryParse<IndexFailure>(book.IndexErrorCode, out var code) && Enum.IsDefined(code) ? code : (IndexFailure?)null;
            var state = await index.GetStateAsync(new BookKey(BookShelf.Library, book.Id.ToString()), cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                return new PassagesView(PassagesState.Missing, 0, null, failure, 0, 0);
            }

            if (queued)
            {
                return new PassagesView(PassagesState.Indexing, state.Passages, state.EmbeddingModel, null, book.IndexDone, book.IndexTotal);
            }

            return state.EmbeddingModel is null ? new PassagesView(PassagesState.NotIndexed, state.Passages, null, failure, 0, 0)
                : EmbeddingModelNames.Same(state.EmbeddingModel, provider.Model) ? new PassagesView(PassagesState.Indexed, state.Passages, state.EmbeddingModel, null, 0, 0)
                : new PassagesView(PassagesState.Outdated, state.Passages, state.EmbeddingModel, failure, 0, 0);
        }
        catch (Exception exception) when (exception is PassageIndexException or SettingsFileException)
        {
            return null;
        }
    }

    private async Task<IndexFailure?> VectorizeAsync(
        BookKey key, Func<Action<Book>, CancellationToken, Task> update, ChatUsageMeter usage, Activity? span, CancellationToken cancellationToken)
    {
        if (await settings.GetEmbeddingProviderAsync(cancellationToken).ConfigureAwait(false) is not { } provider)
        {
            return IndexFailure.NoModel;
        }

        var passages = await index.GetPassagesAsync(key, cancellationToken).ConfigureAwait(false);
        if (passages.Count == 0)
        {
            return null;
        }

        span?.SetTag("index.passages", passages.Count);
        await update(
            book =>
            {
                (book.IndexDone, book.IndexTotal) = (0, passages.Count);
                book.IndexErrorCode = null;
            },
            cancellationToken).ConfigureAwait(false);

        IEmbeddingModel model;
        try
        {
            model = embeddings.Create(provider);
        }
        catch (ArgumentException)
        {
            return IndexFailure.ModelUnreachable; // An address that cannot be used.
        }

        var vectors = new List<float[]>(passages.Count);
        foreach (var group in passages.Chunk(Math.Max(1, options.PassagesPerEmbeddingCall)))
        {
            vectors.AddRange(await model.EmbedAsync([.. group.Select(PassageHeader.TextToVectorize)], usage, cancellationToken).ConfigureAwait(false));
            var done = vectors.Count;
            await update(book => book.IndexDone = done, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await index.SaveVectorsAsync(key, provider.Model, vectors, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return IndexFailure.InvalidReply; // Vectors of different sizes from one call to another.
        }

        return null;
    }

    private static IndexFailure Classify(ChatProviderException exception) => exception.Kind switch
    {
        ChatFailureKind.Unreachable or ChatFailureKind.TimedOut => IndexFailure.ModelUnreachable,
        ChatFailureKind.Http => exception.StatusCode switch
        {
            401 or 403 => IndexFailure.KeyRefused,
            400 or 404 or 405 or 422 or 501 => IndexFailure.CannotVectorize,
            _ => IndexFailure.ProviderError,
        },
        _ => IndexFailure.InvalidReply,
    };
}
