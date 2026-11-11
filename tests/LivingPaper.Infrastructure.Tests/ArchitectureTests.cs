using System.Reflection;
using Shouldly;

namespace LivingPaper.Infrastructure.Tests;

// Guards the hexagonal architecture: Infrastructure (external adapters such as the LLM client or the EPUB/PDF
// reader) may depend on Application and Domain only. Adapters never depend on each other or on the web layer.
public class ArchitectureTests
{
    private static readonly string[] Allowed = ["LivingPaper.Application", "LivingPaper.Domain"];

    [Fact]
    public void Infrastructure_DependsOnlyOnApplicationAndDomain()
    {
        Assembly.Load("LivingPaper.Infrastructure").GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith("LivingPaper.", StringComparison.Ordinal))
            .Where(name => !Allowed.Contains(name))
            .ShouldBeEmpty();
    }
}
