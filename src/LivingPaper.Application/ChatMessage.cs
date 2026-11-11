namespace LivingPaper.Application;

// One line of the conversation sent to the language model.
public sealed record ChatMessage(ChatRole Role, string Content);
