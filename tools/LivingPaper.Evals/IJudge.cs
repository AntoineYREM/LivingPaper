namespace LivingPaper.Evals;

// What the runner needs from the judge: a grade for one reply.
internal interface IJudge
{
    // Throws JudgeException when the reply cannot be graded.
    Task<JudgeVerdict> GradeAsync(EvalQuestion question, string reply, CancellationToken cancellationToken);
}
