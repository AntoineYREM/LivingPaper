namespace LivingPaper.Evals;

// What the runner needs from the chat: the character's answer to one question. The real one asks the chat of the application.
internal interface ICharacterReplier
{
    // Throws ChatReplyException when the reply cannot be written.
    Task<CharacterAnswer> ReplyAsync(EvalQuestion question, CancellationToken cancellationToken);
}

// Trace: what the reply was made from and what it cost, when the replier measures it.
internal sealed record CharacterAnswer(string Reply, TryTrace? Trace = null);
