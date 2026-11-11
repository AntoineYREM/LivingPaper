using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The language model client factory is wired into the application (PAPER-6), ready for the settings page (PAPER-5).
public class ChatModelFactoryRegistrationTests
{
    [Fact]
    public void ChatModelFactory_IsRegistered_AndBuildsAClientForAProvider()
    {
        using var factory = new LivingPaperFactory();

        var chatModels = factory.Services.GetRequiredService<IChatModelFactory>();
        var model = chatModels.Create(new LlmProvider("id", "Local", "http://localhost:11434/v1", string.Empty, "llama3"));

        model.ShouldNotBeNull();
    }

    [Fact]
    public void ChatModelFactory_IsASingleton_SoTheConnectionsAreShared()
    {
        using var factory = new LivingPaperFactory();

        factory.Services.GetRequiredService<IChatModelFactory>().ShouldBeSameAs(factory.Services.GetRequiredService<IChatModelFactory>());
    }

    [Fact]
    public void ChatModelFactory_RefusesAnAddressThatIsNotHttpOrHttps()
    {
        using var factory = new LivingPaperFactory();
        var chatModels = factory.Services.GetRequiredService<IChatModelFactory>();

        Should.Throw<ArgumentException>(() => chatModels.Create(new LlmProvider("id", "Bad", "file:///etc/passwd", "key", "m")));
    }
}
