namespace LivingPaper.Application;

// Why a reply of a character could not be written. A stable key: the page turns each one into a sentence in the user's
// language. Never the message of the provider, which could repeat a key or an address.
public enum ChatFailure
{
    // No language model is chosen for the chat (or its key can no longer be read).
    NoModel,

    // The model cannot be reached, or kept failing after the retries.
    ModelUnreachable,

    // The provider refused for good: a wrong key, an unknown model (HTTP 400, 401, 403, 404).
    ModelRefused,

    // The model's content filter stopped the reply.
    ContentFiltered,

    // The reply came in a form the application cannot read, or ended before its end.
    InvalidReply,

    // The prompts file cannot be used.
    PromptFile,

    // A bug.
    Unexpected,
}

// A reply could not be written. Thrown at the start of the reply or in the middle of it, for the code that reads the reply.
public sealed class ChatReplyException(ChatFailure failure) : Exception($"The reply could not be written: {failure}.")
{
    public ChatFailure Failure { get; } = failure;
}
