using System.Globalization;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Evals;

// Adds a language model from the terminal, with the same steps and the same checks as the Settings page: name, address and key, then
// the models the provider offers (which proves the key), or a typed model name tested with a real call when the provider has no list.
// The model is saved in the list of the site. Nothing is printed that was typed in the key field.
internal sealed class ProviderAdder(ITerminal terminal, LlmSettingsService settings)
{
    private const int ModelsShown = 40;

    // True when a model was saved. A refusal is explained and the caller shows its menu again.
    public async Task<bool> AddAsync(CancellationToken cancellationToken)
    {
        var name = terminal.ReadLine("Name (for example OpenAI): ");
        var baseUrl = terminal.ReadLine("Base URL (for example https://api.openai.com/v1): ");
        var key = terminal.ReadSecret("API key (empty for a local server, not shown): ");
        if (name is null || baseUrl is null || key is null)
        {
            return false;
        }

        var fetched = await settings.FetchModelsAsync(new LlmProviderDraft(name, baseUrl, key, null), cancellationToken).ConfigureAwait(false);
        if (fetched.Pending is not { } pending)
        {
            Explain(fetched.Errors, fetched.Failure, fetched.HttpStatus);
            return false;
        }

        var model = AskModel(pending);
        if (model is null)
        {
            return false;
        }

        var added = await settings.AddFetchedAsync(pending.Id, model, cancellationToken).ConfigureAwait(false);
        if (added is null)
        {
            terminal.WriteLine("The draft expired: start again.");
            return false;
        }

        if (added.Succeeded)
        {
            terminal.WriteLine($"Added {added.Added!.Name}. It is now in the list of the Settings page too.");
            return true;
        }

        Explain(added.Errors, added.Connection?.Failure, added.Connection?.HttpStatus);
        return false;
    }

    private string? AskModel(PendingProviderView pending)
    {
        if (!pending.CanChooseFromList)
        {
            return terminal.ReadLine("This provider has no list of models. Model name: ");
        }

        terminal.WriteLine("Models offered:");
        for (var index = 0; index < Math.Min(ModelsShown, pending.Models.Count); index++)
        {
            terminal.WriteLine($"  {index + 1}) {pending.Models[index]}");
        }

        if (pending.Models.Count > ModelsShown)
        {
            terminal.WriteLine($"  ... and {pending.Models.Count - ModelsShown} more: type the exact name.");
        }

        var answer = terminal.ReadLine("Model (number or name): ")?.Trim();
        return answer is not null
            && int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number >= 1 && number <= Math.Min(ModelsShown, pending.Models.Count)
                ? pending.Models[number - 1]
                : answer;
    }

    // The codes only, never what was typed: a field error or a failure is a stable key, not the text of the user.
    private void Explain(IReadOnlyList<FieldError> errors, ConnectionFailure? failure, int? httpStatus)
    {
        foreach (var error in errors)
        {
            terminal.WriteLine($"Not accepted: {(error.Field.Length == 0 ? "the list" : error.Field)} ({error.Code}).");
        }

        if (failure is not null)
        {
            terminal.WriteLine($"The provider could not be used: {failure}{(httpStatus is null ? string.Empty : $" (HTTP {httpStatus})")}.");
        }
    }
}
