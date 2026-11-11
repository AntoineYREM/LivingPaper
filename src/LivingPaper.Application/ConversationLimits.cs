namespace LivingPaper.Application;

// The limits of a conversation with a character. The browser sends the whole conversation with each message (nothing is
// stored on the server), so the server checks it every time: it is input from the network like any other.
public static class ConversationLimits
{
    // The most messages in a conversation, the user's and the character's together.
    public const int MaxMessages = 100;

    // Only the last ones go to the model: an old part of a long conversation matters less than the time and the cost of
    // sending it again and again.
    public const int MaxSentMessages = 20;

    public const int MaxMessageLength = 4000;
}
