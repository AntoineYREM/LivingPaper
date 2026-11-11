namespace LivingPaper.Application;

// A call to a language model failed. The message is in English and never contains the body of an HTTP error (it could
// echo secrets), the URL (it may carry credentials), the key, or any text of a book or a conversation.
// StatusCode: the HTTP status, for failures of kind Http.
public class ChatProviderException(ChatFailureKind kind, string message, int? statusCode = null) : Exception(message)
{
    public ChatFailureKind Kind { get; } = kind;

    public int? StatusCode { get; } = statusCode;

    // Trying again can help: the provider is busy (429), failing (5xx) or slow (408, time limit), or the network
    // dropped. A refusal (400, 401, 403, 404...), an unreadable answer and a content filter would answer the same again.
    public bool IsTransient => Kind switch
    {
        ChatFailureKind.Unreachable or ChatFailureKind.TimedOut or ChatFailureKind.StreamCut or ChatFailureKind.EmptyReply => true,
        ChatFailureKind.Http => StatusCode is 408 or 429 or >= 500,
        _ => false,
    };
}

// The provider's content filter refused to answer (finish_reason "content_filter"), sometimes after a few words.
public sealed class ContentFilteredException()
    : ChatProviderException(ChatFailureKind.ContentFiltered, "The content filter of the language model stopped the reply.");
