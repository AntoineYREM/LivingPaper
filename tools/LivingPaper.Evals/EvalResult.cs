namespace LivingPaper.Evals;

// The outcome of one question, one time: a score and its reason, or a failure (reply:<code> when the character could not answer,
// judge:<code> when the judge could not grade). Input and Reply are the text of the question and of the answer, kept in the run file
// (never in a log) so a low score can be understood and a person can grade the same answers. TraceId and SpanId say where the try is
// in Langfuse (null without it). JudgeAnswer is what the judge wrote when it was not a grade (judge:invalid-verdict), to understand why.
// Trace is what the reply was made from and what it cost (null in older files, and for a reply that failed).
internal sealed record EvalResult(
    string QuestionId,
    string Category,
    int Repeat,
    int? Score,
    string? Reason,
    string? Failure,
    string? Input = null,
    string? Reply = null,
    string? TraceId = null,
    string? SpanId = null,
    string? JudgeAnswer = null,
    TryTrace? Trace = null);
