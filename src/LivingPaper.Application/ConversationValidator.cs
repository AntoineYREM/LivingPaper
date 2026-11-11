namespace LivingPaper.Application;

// Why a conversation sent by the browser is refused. Only for the answer (HTTP 400) and the tests: never shown as is.
public enum ConversationProblem
{
    None,
    NoMessage,
    TooManyMessages,
    UnknownRole,
    EmptyOrTooLongMessage,
    LastMessageNotFromTheUser,
}

public static class ConversationValidator
{
    // The roles are those of the people in the conversation, never "system": a visitor must not be able to give the model
    // instructions of its own authority.
    public static ConversationProblem Check(IReadOnlyList<ChatMessage>? messages)
    {
        if (messages is null or { Count: 0 })
        {
            return ConversationProblem.NoMessage;
        }

        if (messages.Count > ConversationLimits.MaxMessages)
        {
            return ConversationProblem.TooManyMessages;
        }

        if (messages.Any(message => message is null || message.Role is not (ChatRole.User or ChatRole.Assistant)))
        {
            return ConversationProblem.UnknownRole;
        }

        if (messages.Any(message => string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > ConversationLimits.MaxMessageLength))
        {
            return ConversationProblem.EmptyOrTooLongMessage;
        }

        return messages[^1].Role == ChatRole.User ? ConversationProblem.None : ConversationProblem.LastMessageNotFromTheUser;
    }
}
