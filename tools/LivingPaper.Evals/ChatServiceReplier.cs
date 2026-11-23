using System.Text;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Evals;

// The character answers through the real chat of the application: same prompts, same profile and story of the book, same rules. Each
// question starts a new conversation (one user message), so questions do not influence each other.
internal sealed class ChatServiceReplier(CharacterChatService chat, Book book, Character character) : ICharacterReplier
{
    public async Task<string> ReplyAsync(string question, CancellationToken cancellationToken)
    {
        var reply = new StringBuilder();
        await foreach (var piece in chat.ReplyAsync(book, character, [new ChatMessage(ChatRole.User, question)], cancellationToken).ConfigureAwait(false))
        {
            reply.Append(piece);
        }

        return reply.ToString();
    }
}
