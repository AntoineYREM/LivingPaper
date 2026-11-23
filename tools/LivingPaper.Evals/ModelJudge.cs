using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Evals;

// The judge: a language model that grades one reply from 1 to 5 with the prompt of evals/judge.json. It sees the question and the
// reply, not the prompt the character had. A model that cannot be reached, a content filter or an answer that is not a grade are
// failures of the judge, never a made-up score.
internal sealed class ModelJudge(IChatModelFactory factory, LlmProvider provider, JudgePrompt prompt, string characterName, string bookTitle) : IJudge
{
    public async Task<JudgeVerdict> GradeAsync(string question, string reply, CancellationToken cancellationToken)
    {
        var message = new ChatMessage(ChatRole.User, prompt.Render(characterName, bookTitle, question, reply));

        string answer;
        try
        {
            answer = await factory.Create(provider).CompleteAsync([message], usage: null, cancellationToken).ConfigureAwait(false);
        }
        catch (ContentFilteredException)
        {
            throw new JudgeException("content-filtered");
        }
        catch (Exception exception) when (exception is ChatProviderException or ArgumentException)
        {
            throw new JudgeException("model-error");
        }

        return JudgeVerdict.TryParse(answer) ?? throw new JudgeException("invalid-verdict");
    }
}
