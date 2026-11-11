using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Domain.Tests;

public class LlmProviderTests
{
    private const string Key = "sk-this-is-a-secret-key";

    private static LlmProvider NewProvider() => new("id-1", "OpenAI", "https://user:password@api.openai.com/v1", Key, "gpt-5-nano");

    [Fact]
    public void ToString_NeverContainsTheApiKey()
    {
        NewProvider().ToString().ShouldNotContain(Key);
    }

    [Fact]
    public void ToString_NeverContainsTheBaseUrl_WhichMayCarryCredentials()
    {
        var text = NewProvider().ToString();

        text.ShouldNotContain("password");
        text.ShouldNotContain("api.openai.com");
    }

    [Fact]
    public void ToString_IdentifiesTheProvider()
    {
        var text = NewProvider().ToString();

        text.ShouldContain("OpenAI");
        text.ShouldContain("gpt-5-nano");
    }

    [Fact]
    public void Equality_ComparesAllTheFields()
    {
        NewProvider().ShouldBe(NewProvider());
        NewProvider().ShouldNotBe(NewProvider() with { Model = "other" });
    }
}
