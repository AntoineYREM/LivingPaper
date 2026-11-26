using System.Diagnostics;
using System.Globalization;
using LivingPaper.Domain;

namespace LivingPaper.Application;

// Passages: those that answer the message, the closest first; null when the search failed, and the chat then uses the story.
public sealed record Retrieval(IReadOnlyList<FoundPassage>? Passages, IReadOnlyList<ChatNotice> Notices);

// Finds the passages of a book that answer the last message of a conversation. Whatever fails, the reply must still be
// written: a failure is told as a notice, never thrown. Only a reader who leaves stops it.
public sealed class PassageRetriever(
    IPassageIndex index, LlmSettingsService settings, IEmbeddingModelFactory embeddings, IChatModelFactory models, IPromptProvider prompts, RagOptions options)
{
    // The longest query that is vectorized.
    public const int QueryLetters = 4000;

    // A rewritten query is one line: a model that writes pages is cut.
    private const int RewrittenLetters = 500;

    // The last message and the three before it; each is cut, so a long one does not hide the others.
    private const int RecentMessages = 4;
    private const int MessageLetters = 1000;

    // Null when the chat goes as without passages: no model for vectorization, or a book that is not indexed with it. Its
    // page then says so; the chat says nothing. shelf: a book of the library or an example.
    public async Task<Retrieval?> FindAsync(
        Book book, BookShelf shelf, Character character, IReadOnlyList<ChatMessage> conversation, LlmProvider chat,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(chat);
        if (index.Check() is not null || conversation.Count == 0)
        {
            return null;
        }

        var key = new BookKey(shelf, book.Id.ToString());
        LlmProvider provider;
        try
        {
            if (await settings.GetEmbeddingProviderAsync(cancellationToken).ConfigureAwait(false) is not { } chosen
                || await index.GetStateAsync(key, cancellationToken).ConfigureAwait(false) is not { EmbeddingModel: { } model, Unchecked: false }
                || !EmbeddingModelNames.Same(model, chosen.Model))
            {
                return null;
            }

            provider = chosen;
        }
        catch (SettingsFileException)
        {
            return null;
        }
        catch (PassageIndexException)
        {
            return new Retrieval(null, [ChatNotice.SearchFailed]);
        }

        // The numbers and the distances; the text of the query only when content is captured, never that of a passage.
        using var span = LlmTelemetry.Source.StartActivity("passage-search");
        span?.SetTag("book.id", book.Id.ToString());
        span?.SetTag("langfuse.observation.type", "span");

        var notices = new List<ChatNotice>();
        var mode = options.Mode;
        string? query = null;
        if (mode == QueryMode.Rewrite)
        {
            query = await RewriteAsync(book, character, conversation, chat, span, cancellationToken).ConfigureAwait(false);
            if (query is null)
            {
                notices.Add(ChatNotice.RewriteFailed);
                mode = QueryMode.Recent;
            }
        }

        query ??= mode == QueryMode.Recent
            ? string.Join('\n', conversation.TakeLast(RecentMessages).Select(message => CharacterLimits.Clip(message.Content, MessageLetters)))
            : conversation[^1].Content;
        query = CharacterLimits.Clip(query, QueryLetters);
        span?.SetTag("langfuse.observation.metadata.mode", mode.ToString().ToLowerInvariant());
        if (options.CaptureContent)
        {
            span?.SetTag("langfuse.observation.input", query);
        }

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(options.SearchTimeout);

            // One try: the reply must not wait through retries, it can be written from the story.
            var vectors = await embeddings.Create(provider, retryPassingFailures: false).EmbedAsync([query], usage: null, limit.Token).ConfigureAwait(false);
            var found = await index.SearchAsync(key, vectors[0], Math.Clamp(options.Passages, 1, RagOptions.MaxPassages), limit.Token).ConfigureAwait(false);
            if (found.Count > 0)
            {
                span?.SetTag("langfuse.observation.metadata.passages", string.Join(',', found.Select(result => result.Passage.Number)));
                span?.SetTag(
                    "langfuse.observation.metadata.distances", string.Join(',', found.Select(result => result.Distance.ToString("F3", CultureInfo.InvariantCulture))));
                return new Retrieval(found, notices);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The kind of error only: its message could hold a text.
            span?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        }

        notices.Add(ChatNotice.SearchFailed);
        return new Retrieval(null, notices);
    }

    // The query written by the chat model, on one line; null when it could not be written.
    private async Task<string?> RewriteAsync(
        Book book, Character character, IReadOnlyList<ChatMessage> conversation, LlmProvider chat, Activity? span, CancellationToken cancellationToken)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(options.RewriteTimeout);
            var system = await prompts.RenderAsync(
                PromptKeys.SearchQuery,
                new Dictionary<string, string> { [PromptVariables.BookTitle] = book.Title, [PromptVariables.CharacterName] = character.Name },
                limit.Token).ConfigureAwait(false);
            var transcript = string.Join(
                '\n',
                conversation.TakeLast(RecentMessages).Select(message =>
                    $"{(message.Role == ChatRole.User ? "Reader" : character.Name)}: {CharacterLimits.Clip(message.Content, MessageLetters)}"));
            var reply = await models.Create(chat, retryPassingFailures: false)
                .CompleteAsync([new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, transcript)], usage: null, limit.Token)
                .ConfigureAwait(false);
            var line = string.Join(' ', reply.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim('"', ' ', '«', '»', '“', '”');
            return line.Length == 0 ? null : CharacterLimits.Clip(line, RewrittenLetters);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            span?.SetTag("rag.rewrite.error", exception.GetType().Name);
            return null;
        }
    }
}
