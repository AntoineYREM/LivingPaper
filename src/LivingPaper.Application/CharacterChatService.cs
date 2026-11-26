using System.Runtime.CompilerServices;
using LivingPaper.Domain;

namespace LivingPaper.Application;

// A character of a book plays itself in a conversation (PAPER-10). The model chosen for the chat in the settings plays the
// character from its sheet; the prompt is the one of data/prompts.json (chat.system), read again at every message.
// Nothing is stored: the browser sends the whole conversation with each message and keeps it only while the page is open.
// With a book indexed for the model chosen for vectorization, the character gets the short plot and the passages that answer
// the message (PassageRetriever) in place of the whole story (PAPER-86).
// The text of the messages is never logged, and never leaves this class except to the model and back to the browser.
public sealed class CharacterChatService(
    LlmSettingsService settings, IChatModelFactory models, IPromptProvider prompts, PassageRetriever? retriever = null)
{
    // False when no model is chosen for the chat, or its key can no longer be read: the page then explains and offers no chat.
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await settings.GetAsync(cancellationToken).ConfigureAwait(false)).IsChatEnabled;
        }
        catch (SettingsFileException)
        {
            return false;
        }
    }

    // The character's reply, piece by piece, as the model writes it. The conversation must have been checked with
    // ConversationValidator: only its last ConversationLimits.MaxSentMessages messages go to the model.
    // Throws ChatReplyException, at the start or in the middle of the reply, with a code and nothing else: never the message of
    // the provider (it could repeat the key or the address). Throws OperationCanceledException when stopped.
    public IAsyncEnumerable<string> ReplyAsync(
        Book book, Character character, IReadOnlyList<ChatMessage> conversation, CancellationToken cancellationToken) =>
        ReplyAsync(book, character, conversation, onNotice: null, cancellationToken);

    // onNotice: told, before the first piece, when the passages of the book could not be used as intended.
    public async IAsyncEnumerable<string> ReplyAsync(
        Book book,
        Character character,
        IReadOnlyList<ChatMessage> conversation,
        Action<ChatNotice>? onNotice,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(conversation);

        var provider = await ChosenProviderAsync(cancellationToken).ConfigureAwait(false);
        var retrieval = retriever is null
            ? null
            : await retriever.FindAsync(book, character, conversation, provider, cancellationToken).ConfigureAwait(false);
        foreach (var notice in retrieval?.Notices ?? [])
        {
            onNotice?.Invoke(notice);
        }

        var system = await RenderSystemPromptAsync(book, character, retrieval?.Passages, cancellationToken).ConfigureAwait(false);
        var request = new List<ChatMessage> { new(ChatRole.System, system) };
        request.AddRange(conversation.TakeLast(ConversationLimits.MaxSentMessages));

        IChatModel model;
        try
        {
            model = models.Create(provider);
        }
        catch (ArgumentException)
        {
            throw new ChatReplyException(ChatFailure.ModelUnreachable);
        }

        await using var pieces = model.StreamReplyAsync(request, usage: null, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            string piece;
            try
            {
                if (!await pieces.MoveNextAsync().ConfigureAwait(false))
                {
                    yield break;
                }

                piece = pieces.Current;
            }
            catch (ContentFilteredException)
            {
                throw new ChatReplyException(ChatFailure.ContentFiltered);
            }
            catch (ChatProviderException exception)
            {
                throw Classify(exception);
            }

            yield return piece;
        }
    }

    private async Task<LlmProvider> ChosenProviderAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await settings.GetChatProviderAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ChatReplyException(ChatFailure.NoModel);
        }
        catch (SettingsFileException)
        {
            throw new ChatReplyException(ChatFailure.NoModel);
        }
    }

    // passages: those found for this message, which take the place of the story; null for the story.
    private async Task<string> RenderSystemPromptAsync(
        Book book, Character character, IReadOnlyList<FoundPassage>? passages, CancellationToken cancellationToken)
    {
        try
        {
            // The story section only exists for a book that has a story: an older book chats as before, with no empty section.
            var story = passages is not null
                ? await prompts.RenderAsync(
                    PromptKeys.ChatPassages,
                    new Dictionary<string, string>
                    {
                        [PromptVariables.BookTitle] = book.Title,
                        [PromptVariables.CharacterName] = character.Name,
                        [PromptVariables.PlotSummary] = book.PlotSummary ?? string.Empty,
                        // In the order of the book, each under the scene it belongs to and who is there, as it was vectorized.
                        [PromptVariables.Passages] = string.Join(
                            "\n\n", passages.OrderBy(found => found.Passage.Number).Select(found => PassageHeader.TextToVectorize(found.Passage))),
                    },
                    cancellationToken).ConfigureAwait(false)
                : string.IsNullOrWhiteSpace(book.StorySummary)
                ? string.Empty
                : await prompts.RenderAsync(
                    PromptKeys.ChatStory,
                    new Dictionary<string, string>
                    {
                        [PromptVariables.BookTitle] = book.Title,
                        [PromptVariables.CharacterName] = character.Name,
                        [PromptVariables.StorySummary] = book.StorySummary,
                    },
                    cancellationToken).ConfigureAwait(false);
            return await prompts.RenderAsync(
                PromptKeys.ChatSystem,
                new Dictionary<string, string>
                {
                    [PromptVariables.BookTitle] = book.Title,
                    [PromptVariables.CharacterName] = character.Name,
                    [PromptVariables.Profile] = character.ChatProfile,
                    [PromptVariables.Story] = story,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (PromptFileException)
        {
            throw new ChatReplyException(ChatFailure.PromptFile);
        }
    }

    private static ChatReplyException Classify(ChatProviderException exception) => exception.Kind switch
    {
        ChatFailureKind.Http when exception.StatusCode is 400 or 401 or 403 or 404 => new ChatReplyException(ChatFailure.ModelRefused),
        ChatFailureKind.InvalidResponse or ChatFailureKind.EmptyReply => new ChatReplyException(ChatFailure.InvalidReply),
        _ => new ChatReplyException(ChatFailure.ModelUnreachable),
    };
}
