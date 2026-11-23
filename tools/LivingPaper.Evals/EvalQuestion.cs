namespace LivingPaper.Evals;

// One question put to the character. Category says what it tests; ExpectedOutput describes the attitude expected, in words: it is a
// guide for the people who read the results, the judge does not see it.
internal sealed record EvalQuestion(string Id, string Category, string Input, string ExpectedOutput);

// The content of a questions file (evals/<book>/questions.json): the book and the character the questions are for.
internal sealed record QuestionSet(string Dataset, string BookId, string Character, IReadOnlyList<EvalQuestion> Items);
