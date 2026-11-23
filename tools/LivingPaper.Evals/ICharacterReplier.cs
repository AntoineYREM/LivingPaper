namespace LivingPaper.Evals;

// What the runner needs from the chat: the character's answer to one question. The real one asks the chat of the application.
internal interface ICharacterReplier
{
    // Throws ChatReplyException when the reply cannot be written.
    Task<string> ReplyAsync(string question, CancellationToken cancellationToken);
}
