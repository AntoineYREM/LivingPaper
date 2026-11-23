namespace LivingPaper.Evals;

// Something the user can fix (a wrong file, a model that does not exist, a missing option). Its message is shown as it is, so it never
// contains a key, an address or a passage of a conversation.
internal sealed class EvalException(string message) : Exception(message);
