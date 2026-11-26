using System.Diagnostics;
using System.Text;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Evals;

// The character answers through the real chat of the application: same prompts, same profile, same story or passages of the book, same
// rules. Each question is a conversation of its own, so questions do not influence each other. With a recorder (the one the chat was
// given its models through), each answer comes with what it was made from and what it cost.
internal sealed class ChatServiceReplier(CharacterChatService chat, Book book, Character character, RetrievalRecorder? recorder = null)
    : ICharacterReplier
{
    public async Task<CharacterAnswer> ReplyAsync(EvalQuestion question, CancellationToken cancellationToken)
    {
        List<ChatMessage> conversation =
        [
            .. (question.Conversation ?? []).Select(turn => new ChatMessage(turn.FromCharacter ? ChatRole.Assistant : ChatRole.User, turn.Content)),
            new ChatMessage(ChatRole.User, question.Input),
        ];
        var notices = new List<ChatNotice>();
        recorder?.Begin();
        var watch = Stopwatch.StartNew();
        var reply = new StringBuilder();
        await foreach (var piece in chat.ReplyAsync(book, character, conversation, notices.Add, cancellationToken).ConfigureAwait(false))
        {
            reply.Append(piece);
        }

        return new CharacterAnswer(reply.ToString(), recorder?.Trace(question, notices, watch.Elapsed));
    }
}
