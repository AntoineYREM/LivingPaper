using Shouldly;

namespace LivingPaper.Evals.Tests;

public class PriceListTests
{
    private const string Valid = """
        {
          "schemaVersion": 1,
          "currency": "USD",
          "updated": "2026-11-23",
          "models": {
            "model-a": { "input": 2, "cachedInput": 0.5, "output": 10 },
            "model-b": { "input": 1, "output": 4 },
            "local": { "input": 0, "output": 0 }
          }
        }
        """;

    private static async Task<PriceList> ReadAsync(string json)
    {
        using var folder = new TempFolder();
        return await PriceList.ReadAsync(folder.Write("prices.json", json), CancellationToken.None);
    }

    [Fact]
    public async Task CostOf_CountsCachedTokensAtTheirOwnPrice()
    {
        var prices = await ReadAsync(Valid);

        // 1000 tokens in, 400 of them cached: 600 at 2, 400 at 0.5; 100 out at 10 (all per million tokens).
        prices.CostOf("model-a", new TokenUsage(1000, 400, 100))!.Value.ShouldBe((600 * 2.0 + 400 * 0.5 + 100 * 10.0) / 1_000_000, 1e-12);
    }

    [Fact]
    public async Task CostOf_BillsCachedTokensAsNormalWhenThereIsNoCachePrice()
    {
        var prices = await ReadAsync(Valid);

        prices.CostOf("model-b", new TokenUsage(1000, 400, 100))!.Value.ShouldBe((1000 * 1.0 + 100 * 4.0) / 1_000_000, 1e-12);
    }

    [Fact]
    public async Task CostOf_IsZeroForALocalModelAndUnknownForOneWithNoPrice()
    {
        var prices = await ReadAsync(Valid);

        prices.CostOf("local", new TokenUsage(5000, 0, 500)).ShouldBe(0.0);
        prices.CostOf("nobody", new TokenUsage(5000, 0, 500)).ShouldBeNull();
        prices.Currency.ShouldBe("USD");
    }

    [Fact]
    public async Task ReadAsync_ReadsTheRealFileOfTheRepository()
    {
        var prices = await PriceList.ReadAsync(RepoFiles.Find("evals/prices.json")!, CancellationToken.None);

        prices.CostOf("anthropic/claude-haiku-5.5", new TokenUsage(1_000_000, 0, 0))!.Value.ShouldBe(0.10, 1e-9);
        prices.CostOf("gemma3:4b", new TokenUsage(1_000_000, 0, 1_000_000)).ShouldBe(0.0);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "schemaVersion": 2, "currency": "USD", "models": {} }""")]
    [InlineData("""{ "schemaVersion": 1, "currency": "", "models": {} }""")]
    [InlineData("""{ "schemaVersion": 1, "currency": "USD", "models": { "m": { "input": -1, "output": 1 } } }""")]
    [InlineData("""{ "schemaVersion": 1, "currency": "USD", "models": { "m": { "input": 1, "cachedInput": -1, "output": 1 } } }""")]
    [InlineData("""{ "schemaVersion": 1, "currency": "USD" }""")]
    public async Task ReadAsync_RefusesAFileThatBreaksTheRules(string json) =>
        await Should.ThrowAsync<EvalException>(() => ReadAsync(json));

    [Fact]
    public async Task ReadAsync_RefusesAMissingFile() =>
        await Should.ThrowAsync<EvalException>(() => PriceList.ReadAsync(Path.Combine(Path.GetTempPath(), "no-such-prices.json"), CancellationToken.None));
}
