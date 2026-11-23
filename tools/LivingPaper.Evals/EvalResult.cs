namespace LivingPaper.Evals;

// The outcome of one question, one time: a score and its reason, or a failure (reply:<code> when the character could not answer,
// judge:<code> when the judge could not grade). The text of the reply is not kept.
internal sealed record EvalResult(string QuestionId, string Category, int Repeat, int? Score, string? Reason, string? Failure);
