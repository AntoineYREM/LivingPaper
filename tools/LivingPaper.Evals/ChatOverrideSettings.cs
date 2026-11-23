using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Evals;

// The settings of the site, with one difference: the model of the chat is the one the tool chose. It lets the real chat of the application
// (CharacterChatService, which reads its model from the settings) play with any model of the list, without changing the site's own choice.
internal sealed class ChatOverrideSettings(ILlmSettingsRepository site, string chatProviderId) : ILlmSettingsRepository
{
    public async Task<LlmSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await site.LoadAsync(cancellationToken).ConfigureAwait(false);
        settings.SelectForChat(chatProviderId);
        return settings;
    }

    // The tool never writes the settings of the site through this door.
    public Task SaveAsync(LlmSettings settings, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The evaluation tool does not change the settings of the site.");
}
