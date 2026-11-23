using System.Globalization;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Evals;

// Chooses a language model among those of the Settings page (the same list, data/settings.json). With a name (--chat, --judge or the
// EVAL_* variables) it asks nothing; without one it shows the list and lets the user pick a model or add one. It only reads the
// list: what the site chose for its own chat and analysis is never changed.
internal sealed class ProviderChooser(ITerminal terminal, ILlmSettingsRepository repository, ProviderAdder adder)
{
    // role: "chat" or "judge", as the option and the variable (EVAL_CHAT_PROVIDER) are named.
    public async Task<LlmProvider> ChooseAsync(string role, string? requestedName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            return await FindByNameAsync(requestedName, cancellationToken).ConfigureAwait(false);
        }

        if (!terminal.IsInteractive)
        {
            throw new EvalException(
                $"There is no terminal to ask which language model to use for the {role}: give --{role} <name> or set EVAL_{role.ToUpperInvariant()}_PROVIDER (a name from the Settings page).");
        }

        while (true)
        {
            var settings = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            var providers = settings.Providers;
            terminal.WriteLine();
            terminal.WriteLine($"Language model for the {role}:");
            for (var index = 0; index < providers.Count; index++)
            {
                var unreadable = settings.UnreadableKeyProviderIds.Contains(providers[index].Id) ? " - key unreadable, add it again" : string.Empty;
                terminal.WriteLine($"  {index + 1}) {providers[index].Name} ({providers[index].Model}){unreadable}");
            }

            terminal.WriteLine($"  {providers.Count + 1}) Add a language model");
            if (providers.Count == 0)
            {
                // The usual cause: the tool reads another data folder than the site. DATA_DIRECTORY points it at the right one.
                terminal.WriteLine("  (No language model is saved in the data folder above. If your site keeps its models elsewhere, set DATA_DIRECTORY to its data folder.)");
            }

            var answer = terminal.ReadLine("Your choice: ");
            if (answer is null)
            {
                throw new EvalException("No answer was given: the choice of the language model was cancelled.");
            }

            if (!int.TryParse(answer.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 || number > providers.Count + 1)
            {
                terminal.WriteLine($"Type a number from 1 to {providers.Count + 1}.");
            }
            else if (number == providers.Count + 1)
            {
                await adder.AddAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (settings.UnreadableKeyProviderIds.Contains(providers[number - 1].Id))
            {
                terminal.WriteLine("The key of this model cannot be read: choose another one, or add it again.");
            }
            else
            {
                return providers[number - 1];
            }
        }
    }

    private async Task<LlmProvider> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        var settings = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var provider = settings.Providers.FirstOrDefault(candidate => string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            var names = settings.Providers.Count == 0 ? "none saved yet" : string.Join(", ", settings.Providers.Select(candidate => candidate.Name));
            throw new EvalException($"No language model has that name. Available: {names}.");
        }

        if (settings.UnreadableKeyProviderIds.Contains(provider.Id))
        {
            throw new EvalException($"The key of {provider.Name} cannot be read (the encryption keys of the application changed). Add the model again.");
        }

        return provider;
    }
}
