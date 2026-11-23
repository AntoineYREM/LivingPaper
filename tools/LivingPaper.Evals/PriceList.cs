using System.Text.Json;

namespace LivingPaper.Evals;

// USD (or another currency) per million tokens, for the input, the cached input and the output of a model.
internal sealed record ModelPrice(double Input, double? CachedInput, double Output);

// The prices of the models (evals/prices.json), kept in the repository because they decide what a run costs and nobody should have to
// look them up again. They change: the file says when it was updated, and a model that is not in it simply has no cost shown, never a
// guessed one. A model that runs on your own computer is listed at 0.
internal sealed class PriceList(string currency, IReadOnlyDictionary<string, ModelPrice> models)
{
    public const long MaxFileBytes = 262_144;
    public const int MaxModels = 1000;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public string Currency { get; } = currency;

    public static async Task<PriceList> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            throw new EvalException($"The price file {name} does not exist.");
        }

        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new EvalException($"The price file {name} is too large.");
        }

        FileContent? file;
        try
        {
            await using var stream = File.OpenRead(path);
            file = await JsonSerializer.DeserializeAsync<FileContent>(stream, Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw new EvalException($"The price file {name} is not valid JSON.");
        }

        if (file is null || file.SchemaVersion != 1 || string.IsNullOrWhiteSpace(file.Currency) || file.Models is null || file.Models.Count > MaxModels)
        {
            throw new EvalException($"The price file {name} needs \"schemaVersion\": 1, a currency and a list of models (at most {MaxModels}).");
        }

        var models = new Dictionary<string, ModelPrice>(StringComparer.Ordinal);
        foreach (var (model, price) in file.Models)
        {
            if (price is null || price.Input < 0 || price.Output < 0 || price.CachedInput < 0)
            {
                throw new EvalException($"The price of a model in {name} must be a number that is not negative.");
            }

            models[model] = new ModelPrice(price.Input, price.CachedInput, price.Output);
        }

        return new PriceList(file.Currency.Trim(), models);
    }

    // The tokens of a run at the price of the model. The input tokens are all the tokens of the prompt, those found in the provider's
    // cache included (as the providers report them): the cached ones are billed at the cache price, the others at the normal one.
    // Null when the model has no price.
    public double? CostOf(string model, TokenUsage tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (!models.TryGetValue(model, out var price))
        {
            return null;
        }

        var cached = Math.Clamp(tokens.CachedInput, 0, tokens.Input);
        var cachedPrice = price.CachedInput ?? price.Input;
        return (((tokens.Input - cached) * price.Input) + (cached * cachedPrice) + (tokens.Output * price.Output)) / 1_000_000d;
    }

    private sealed record FileContent(int SchemaVersion, string? Currency, string? Updated, Dictionary<string, PriceContent?>? Models);

    private sealed record PriceContent(double Input, double? CachedInput, double Output);
}
