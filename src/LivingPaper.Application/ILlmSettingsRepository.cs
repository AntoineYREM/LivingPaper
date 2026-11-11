using LivingPaper.Domain;

namespace LivingPaper.Application;

// Where the user's language models and their choices are kept (data/settings.json in V0, with the keys encrypted).
public interface ILlmSettingsRepository
{
    // Empty settings when nothing was ever saved. Throws SettingsFileException when the file exists but cannot be
    // used: it is never replaced by empty settings, which would silently lose the user's models and keys.
    Task<LlmSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(LlmSettings settings, CancellationToken cancellationToken);
}
