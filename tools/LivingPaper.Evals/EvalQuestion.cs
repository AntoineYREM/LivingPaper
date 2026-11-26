namespace LivingPaper.Evals;

// One question put to the character. Category says what it tests; ExpectedOutput describes what is expected, in words: the attitude
// for a question about the role (a guide for the people who read the results), the right answer for a factual question (the
// accuracy judge reads it).
internal sealed record EvalQuestion(string Id, string Category, string Input, string ExpectedOutput)
{
    // Phrases of the book that hold the answer: the search found it when one of them is in a passage found. Null when not told.
    public IReadOnlyList<string>? PassageQuotes { get; init; }

    // What was said before the question, for a follow-up; null for a question that starts a conversation.
    public IReadOnlyList<ConversationTurn>? Conversation { get; init; }
}

internal sealed record ConversationTurn(bool FromCharacter, string Content);

// The content of a questions file (evals/<book>/questions.json): the book and the character the questions are for.
internal sealed record QuestionSet(string Dataset, string BookId, string Character, IReadOnlyList<EvalQuestion> Items);
