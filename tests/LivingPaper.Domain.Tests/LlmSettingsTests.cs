using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Domain.Tests;

public class LlmSettingsTests
{
    private static LlmProvider Provider(string id, string name = "Provider", string key = "key") =>
        new(id, name, "https://example.com/v1", key, "model");

    [Fact]
    public void NewSettings_HaveNoProvider_AndNeitherAnalysisNorChatIsEnabled()
    {
        var settings = new LlmSettings();

        settings.Providers.ShouldBeEmpty();
        settings.AnalysisProviderId.ShouldBeNull();
        settings.ChatProviderId.ShouldBeNull();
        settings.IsAnalysisEnabled.ShouldBeFalse();
        settings.IsChatEnabled.ShouldBeFalse();
    }

    [Fact]
    public void TheFirstProviderAdded_IsSelectedForTheAnalysisAndTheChat()
    {
        var settings = new LlmSettings();
        var first = Provider("a");

        settings.Add(first);

        settings.Providers.ShouldBe([first]);
        settings.AnalysisProviderId.ShouldBe("a");
        settings.ChatProviderId.ShouldBe("a");
        settings.AnalysisProvider.ShouldBe(first);
        settings.ChatProvider.ShouldBe(first);
        settings.IsAnalysisEnabled.ShouldBeTrue();
        settings.IsChatEnabled.ShouldBeTrue();
    }

    [Fact]
    public void AnotherProviderAdded_DoesNotChangeTheSelection()
    {
        var settings = new LlmSettings();
        settings.Add(Provider("a"));

        settings.Add(Provider("b", "Second"));

        settings.Providers.Select(provider => provider.Id).ShouldBe(["a", "b"]);
        settings.AnalysisProviderId.ShouldBe("a");
        settings.ChatProviderId.ShouldBe("a");
    }

    [Fact]
    public void AddingTheFirstProviderAgain_AfterTheOnlyOneWasRemoved_SelectsItAgain()
    {
        var settings = new LlmSettings();
        settings.Add(Provider("a"));
        settings.Remove("a");

        settings.Add(Provider("b", "Second"));

        settings.AnalysisProviderId.ShouldBe("b");
        settings.ChatProviderId.ShouldBe("b");
    }

    [Fact]
    public void AddingAProviderWithAnIdAlreadyUsed_Throws()
    {
        var settings = new LlmSettings();
        settings.Add(Provider("a"));

        Should.Throw<InvalidOperationException>(() => settings.Add(Provider("a", "Other name")));
    }

    [Fact]
    public void AddingMoreThanTheMaximumNumberOfProviders_Throws()
    {
        var settings = new LlmSettings();
        for (var index = 0; index < LlmProviderLimits.MaxProviders; index++)
        {
            settings.Add(Provider($"id{index}", $"Name {index}"));
        }

        Should.Throw<InvalidOperationException>(() => settings.Add(Provider("one-too-many", "One too many")));
    }

    // ---- Removing ----

    [Fact]
    public void RemovingTheSelectedProvider_UnselectsIt_AndDisablesAnalysisAndChat()
    {
        var settings = new LlmSettings();
        settings.Add(Provider("a"));
        settings.Add(Provider("b", "Second"));

        settings.Remove("a").ShouldBeTrue();

        settings.Providers.Select(provider => provider.Id).ShouldBe(["b"]);
        settings.AnalysisProviderId.ShouldBeNull(); // Nothing is chosen in its place.
        settings.ChatProviderId.ShouldBeNull();
        settings.IsAnalysisEnabled.ShouldBeFalse();
        settings.IsChatEnabled.ShouldBeFalse();
    }

    [Fact]
    public void RemovingAProviderSelectedForOnlyOneUse_KeepsTheOtherUse()
    {
        var settings = new LlmSettings();
        settings.Add(Provider("a"));
        settings.Add(Provider("b", "Second"));
        settings.SelectForChat("b");

        settings.Remove("b");

        settings.ChatProviderId.ShouldBeNull();
        settings.AnalysisProviderId.ShouldBe("a");
        settings.IsAnalysisEnabled.ShouldBeTrue();
        settings.IsChatEnabled.ShouldBeFalse();
    }

    [Fact]
    public void RemovingAProviderThatIsNotSelected_LeavesTheSelectionAlone()
    {
        var settings = new LlmSettings();
        settings.Add(Provider("a"));
        settings.Add(Provider("b", "Second"));

        settings.Remove("b").ShouldBeTrue();

        settings.AnalysisProviderId.ShouldBe("a");
        settings.ChatProviderId.ShouldBe("a");
    }

    [Fact]
    public void RemovingAnUnknownProvider_ReturnsFalse_AndChangesNothing()
    {
        var settings = new LlmSettings();
        settings.Add(Provider("a"));

        settings.Remove("zzz").ShouldBeFalse();

        settings.Providers.Count.ShouldBe(1);
        settings.AnalysisProviderId.ShouldBe("a");
    }

    // ---- Selecting ----

    [Fact]
    public void TheAnalysisAndTheChat_CanUseDifferentProviders()
    {
        var settings = new LlmSettings();
        settings.Add(Provider("a"));
        settings.Add(Provider("b", "Second"));

        settings.SelectForChat("b");

        settings.AnalysisProviderId.ShouldBe("a");
        settings.ChatProviderId.ShouldBe("b");
        settings.ChatProvider!.Id.ShouldBe("b");
    }

    [Fact]
    public void SelectingNothing_DisablesThatUse()
    {
        var settings = new LlmSettings();
        settings.Add(Provider("a"));

        settings.SelectForAnalysis(null);

        settings.AnalysisProviderId.ShouldBeNull();
        settings.IsAnalysisEnabled.ShouldBeFalse();
        settings.IsChatEnabled.ShouldBeTrue();
    }

    [Fact]
    public void SelectingAnUnknownProvider_Throws_AndKeepsThePreviousSelection()
    {
        var settings = new LlmSettings();
        settings.Add(Provider("a"));

        Should.Throw<ArgumentException>(() => settings.SelectForAnalysis("zzz"));
        Should.Throw<ArgumentException>(() => settings.SelectForChat("zzz"));

        settings.AnalysisProviderId.ShouldBe("a");
        settings.ChatProviderId.ShouldBe("a");
    }

    // ---- A key that cannot be read ----

    [Fact]
    public void AProviderWhoseKeyCannotBeRead_IsNotUsable_EvenWhenSelected()
    {
        var settings = LlmSettings.Restore([Provider("a", key: string.Empty), Provider("b", "Second")], "a", "b", unreadableKeyProviderIds: ["a"]);

        settings.AnalysisProviderId.ShouldBe("a");
        settings.IsAnalysisEnabled.ShouldBeFalse();
        settings.IsChatEnabled.ShouldBeTrue();
        settings.UnreadableKeyProviderIds.ShouldBe(["a"]);
    }

    // ---- Restoring from a file ----

    [Fact]
    public void Restore_BuildsTheSettingsWithoutSelectingAnythingByItself()
    {
        var settings = LlmSettings.Restore([Provider("a"), Provider("b", "Second")], null, "b");

        settings.Providers.Count.ShouldBe(2);
        settings.AnalysisProviderId.ShouldBeNull();
        settings.ChatProviderId.ShouldBe("b");
    }

    [Fact]
    public void Restore_IgnoresASelectionOfAProviderThatDoesNotExist()
    {
        var settings = LlmSettings.Restore([Provider("a")], "gone", "gone");

        settings.AnalysisProviderId.ShouldBeNull();
        settings.ChatProviderId.ShouldBeNull();
    }

    [Fact]
    public void Restore_RejectsTwoProvidersWithTheSameIdOrTheSameName()
    {
        Should.Throw<ArgumentException>(() => LlmSettings.Restore([Provider("a"), Provider("a", "Second")], null, null));
        Should.Throw<ArgumentException>(() => LlmSettings.Restore([Provider("a", "Same"), Provider("b", "same")], null, null));
    }

    [Fact]
    public void Restore_RejectsMoreThanTheMaximumNumberOfProviders()
    {
        var providers = Enumerable.Range(0, LlmProviderLimits.MaxProviders + 1).Select(index => Provider($"id{index}", $"Name {index}")).ToList();

        Should.Throw<ArgumentException>(() => LlmSettings.Restore(providers, null, null));
    }

    [Fact]
    public void TheProviderList_CannotBeChangedFromOutside()
    {
        var settings = new LlmSettings();
        settings.Add(Provider("a"));

        settings.Providers.ShouldNotBeAssignableTo<List<LlmProvider>>();
        settings.Providers.ShouldBeAssignableTo<IReadOnlyList<LlmProvider>>();
    }
}
