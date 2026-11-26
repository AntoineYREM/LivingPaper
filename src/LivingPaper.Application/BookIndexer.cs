using System.Diagnostics;
using LivingPaper.Domain;

namespace LivingPaper.Application;

// Turns the passages kept for a book into vectors, with the model chosen for vectorization, and says where a book is with
// them. Used at the end of an analysis and when the user asks to index a book or an example. Vectors that came with a file
// are only used once the model chosen here is seen to make the same ones; else new ones are computed when the user asks.
// New vectors replace the earlier ones only when all of them are made. A failure never stops what called it: it is written
// on the book.
public sealed class BookIndexer(IPassageIndex index, LlmSettingsService settings, IEmbeddingModelFactory embeddings, AnalysisOptions options)
{
    // Vectors of a file are those of the model when the vector it makes of the same passage points the same way, at least
    // this much (cosine similarity): the same model on another machine differs by rounding only.
    private const double SameVectorSimilarity = 0.99;

    // update: changes the book and saves it, one change at a time (the caller knows how). Returns null when the passages
    // are indexed (or there is none), else why not. Throws OperationCanceledException when cancelled.
    public Task<IndexFailure?> IndexAsync(BookKey key, Func<Action<Book>, CancellationToken, Task> update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(update);
        return RunAsync("index-passages", update, (usage, span) => VectorizeAsync(key, update, usage, span, cancellationToken), cancellationToken);
    }

    // Checks the vectors that came with the file of a book, with one passage: nothing else is computed. Nothing happens
    // when there is nothing to check with the model chosen.
    public async Task<IndexFailure?> CheckAsync(BookKey key, Func<Action<Book>, CancellationToken, Task> update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(update);
        LlmProvider provider;
        try
        {
            if (await settings.GetEmbeddingProviderAsync(cancellationToken).ConfigureAwait(false) is not { } chosen
                || await index.GetStateAsync(key, cancellationToken).ConfigureAwait(false) is not { } state
                || !CanBeChecked(state, chosen))
            {
                return null;
            }

            provider = chosen;
        }
        catch (Exception exception) when (exception is PassageIndexException or SettingsFileException)
        {
            return null; // The page of the book says that the feature is off.
        }

        return await RunAsync(
            "check-passages",
            update,
            async (usage, _) =>
            {
                if (Create(provider) is not { } model)
                {
                    return IndexFailure.ModelUnreachable;
                }

                if (!await SameVectorsAsync(model, key, usage, cancellationToken).ConfigureAwait(false))
                {
                    return IndexFailure.VectorsDiffer;
                }

                await index.MarkCheckedAsync(key, cancellationToken).ConfigureAwait(false);
                return null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    // What the page of a book says about its passages. Null when the feature is off (no model for vectorization, or an index
    // that cannot be used): the page then says nothing. queued: an indexing of the book is waiting or running.
    public Task<PassagesView?> DescribeAsync(Book book, bool queued, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        return WithModelAsync(
            async provider =>
            {
                var state = await index.GetStateAsync(new BookKey(BookShelf.Library, book.Id.ToString()), cancellationToken).ConfigureAwait(false);
                return state is null
                    ? new PassagesView(PassagesState.Missing, 0, null, Failure(book), 0, 0)
                    : View(book, state, provider, queued);
            },
            cancellationToken);
    }

    // The same for an example shipped with "passages" passages, which are in no index until the user asks. progress: what
    // is known of its indexing, kept apart since an example is never changed.
    public Task<PassagesView?> DescribeExampleAsync(Book progress, int passages, bool queued, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return WithModelAsync(
            async provider =>
            {
                var state = await index.GetStateAsync(new BookKey(BookShelf.Examples, progress.Id.ToString()), cancellationToken).ConfigureAwait(false);
                if (state is not null)
                {
                    return View(progress, state, provider, queued);
                }

                return queued
                    ? new PassagesView(PassagesState.Indexing, passages, null, null, 0, passages)
                    : new PassagesView(PassagesState.NotIndexed, passages, null, Failure(progress), 0, 0);
            },
            cancellationToken);
    }

    private async Task<PassagesView?> WithModelAsync(Func<LlmProvider, Task<PassagesView>> describe, CancellationToken cancellationToken)
    {
        if (index.Check() is not null)
        {
            return null;
        }

        try
        {
            return await settings.GetEmbeddingProviderAsync(cancellationToken).ConfigureAwait(false) is { } provider
                ? await describe(provider).ConfigureAwait(false)
                : null;
        }
        catch (Exception exception) when (exception is PassageIndexException or SettingsFileException)
        {
            return null;
        }
    }

    private static PassagesView View(Book book, PassageIndexState state, LlmProvider provider, bool queued)
    {
        if (queued)
        {
            // Vectors of a file with nothing counted yet: they are being checked.
            return state.Unchecked && book.IndexTotal == 0
                ? new PassagesView(PassagesState.Checking, state.Passages, state.EmbeddingModel, null, 0, 0)
                : new PassagesView(PassagesState.Indexing, state.Passages, state.EmbeddingModel, null, book.IndexDone, book.IndexTotal);
        }

        var failure = Failure(book);
        if (state.EmbeddingModel is null)
        {
            return new PassagesView(PassagesState.NotIndexed, state.Passages, null, failure, 0, 0);
        }

        if (!EmbeddingModelNames.Same(state.EmbeddingModel, provider.Model))
        {
            // What a check found about another model says nothing any more.
            return new PassagesView(
                PassagesState.Outdated, state.Passages, state.EmbeddingModel, failure == IndexFailure.VectorsDiffer ? null : failure, 0, 0);
        }

        return state.Unchecked
            ? new PassagesView(PassagesState.Unchecked, state.Passages, state.EmbeddingModel, failure, 0, 0)
            : new PassagesView(PassagesState.Indexed, state.Passages, state.EmbeddingModel, null, 0, 0);
    }

    private static IndexFailure? Failure(Book book) =>
        Enum.TryParse<IndexFailure>(book.IndexErrorCode, out var code) && Enum.IsDefined(code) ? code : null;

    private static bool CanBeChecked(PassageIndexState state, LlmProvider provider) =>
        state is { Unchecked: true, EmbeddingModel: { } model } && EmbeddingModelNames.Same(model, provider.Model);

    private static async Task<IndexFailure?> RunAsync(
        string name,
        Func<Action<Book>, CancellationToken, Task> update,
        Func<ChatUsageMeter, Activity?, Task<IndexFailure?>> work,
        CancellationToken cancellationToken)
    {
        // Only the number of passages and the code of a failure: never a text of the book.
        using var span = LlmTelemetry.Source.StartActivity(name);
        var usage = new ChatUsageMeter();
        IndexFailure? failure;
        try
        {
            failure = await work(usage, span).ConfigureAwait(false);
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

        // The tokens were spent even when the work failed.
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
        if (Create(provider) is not { } model)
        {
            return IndexFailure.ModelUnreachable;
        }

        if (await index.GetStateAsync(key, cancellationToken).ConfigureAwait(false) is { } state && CanBeChecked(state, provider)
            && await SameVectorsAsync(model, key, usage, cancellationToken).ConfigureAwait(false))
        {
            await index.MarkCheckedAsync(key, cancellationToken).ConfigureAwait(false);
            return null;
        }

        await update(
            book =>
            {
                (book.IndexDone, book.IndexTotal) = (0, passages.Count);
                book.IndexErrorCode = null;
            },
            cancellationToken).ConfigureAwait(false);

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

    // Null when the address of the model cannot be used.
    private IEmbeddingModel? Create(LlmProvider provider)
    {
        try
        {
            return embeddings.Create(provider);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private async Task<bool> SameVectorsAsync(IEmbeddingModel model, BookKey key, ChatUsageMeter usage, CancellationToken cancellationToken)
    {
        var passages = await index.GetPassagesAsync(key, cancellationToken).ConfigureAwait(false);
        var kept = await index.GetVectorsAsync(key, cancellationToken).ConfigureAwait(false);
        if (passages.Count == 0 || kept.Count == 0)
        {
            return false;
        }

        var made = await model.EmbedAsync([PassageHeader.TextToVectorize(passages[0])], usage, cancellationToken).ConfigureAwait(false);
        return made[0].Length == kept[0].Length && Similarity(made[0], kept[0]) >= SameVectorSimilarity;
    }

    // The cosine of the angle between two vectors of the same size; 0 when one has no direction.
    private static double Similarity(float[] left, float[] right)
    {
        double product = 0, leftSquares = 0, rightSquares = 0;
        for (var at = 0; at < left.Length; at++)
        {
            product += (double)left[at] * right[at];
            leftSquares += (double)left[at] * left[at];
            rightSquares += (double)right[at] * right[at];
        }

        return leftSquares > 0 && rightSquares > 0 ? product / Math.Sqrt(leftSquares * rightSquares) : 0;
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
