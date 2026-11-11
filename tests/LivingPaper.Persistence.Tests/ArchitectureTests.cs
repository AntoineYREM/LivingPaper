using System.Reflection;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

// Guards the hexagonal architecture: Persistence (the JSON file storage adapter) may depend on Application and
// Domain only. Adapters never depend on each other or on the web layer.
public class ArchitectureTests
{
    private static readonly string[] Allowed = ["LivingPaper.Application", "LivingPaper.Domain"];

    [Fact]
    public void Persistence_DependsOnlyOnApplicationAndDomain()
    {
        Assembly.Load("LivingPaper.Persistence").GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith("LivingPaper.", StringComparison.Ordinal))
            .Where(name => !Allowed.Contains(name))
            .ShouldBeEmpty();
    }
}
