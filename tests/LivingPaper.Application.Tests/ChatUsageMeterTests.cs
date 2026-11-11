using LivingPaper.Application;
using Shouldly;

namespace LivingPaper.Application.Tests;

public class ChatUsageMeterTests
{
    [Fact]
    public void NewMeter_StartsAtZero()
    {
        var meter = new ChatUsageMeter();

        meter.InputTokens.ShouldBe(0);
        meter.CachedInputTokens.ShouldBe(0);
        meter.OutputTokens.ShouldBe(0);
    }

    [Fact]
    public void Add_AddsUpTheTokensOfSeveralCalls()
    {
        var meter = new ChatUsageMeter();

        meter.Add(input: 100, cachedInput: 20, output: 50);
        meter.Add(input: 10, cachedInput: 0, output: 5);

        meter.InputTokens.ShouldBe(110);
        meter.CachedInputTokens.ShouldBe(20);
        meter.OutputTokens.ShouldBe(55);
    }

    [Fact]
    public async Task Add_IsSafeWhenSeveralCallsReportAtTheSameTime()
    {
        var meter = new ChatUsageMeter();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => meter.Add(1, 1, 1), TestContext.Current.CancellationToken)));

        meter.InputTokens.ShouldBe(200);
        meter.CachedInputTokens.ShouldBe(200);
        meter.OutputTokens.ShouldBe(200);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Add_RejectsNegativeCounts(int input, int cachedInput, int output)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ChatUsageMeter().Add(input, cachedInput, output));
    }
}
