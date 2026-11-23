using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The .env file of local development, read by `dotnet run` (which does not read it by itself).
public class DotEnvFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "livingpaper-dotenv-" + Guid.NewGuid().ToString("N"));

    public DotEnvFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Write(string content)
    {
        var path = Path.Combine(_folder, ".env");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Parse_ReadsValues_SkippingCommentsAndBlankLines()
    {
        var path = Write("# comment\n\nA=1\r\nB = two words \nC=\"quoted\"\nnot a pair\n=novalue\n");

        var values = DotEnvFile.Parse(path).ToDictionary(pair => pair.Key, pair => pair.Value);

        values.ShouldBe(new Dictionary<string, string> { ["A"] = "1", ["B"] = "two words", ["C"] = "quoted" });
    }

    [Fact]
    public void Load_SetsOnlyVariablesThatAreNotAlreadySet()
    {
        var path = Write("LP_TEST_NEW=file\nLP_TEST_OLD=file\n");
        Environment.SetEnvironmentVariable("LP_TEST_NEW", null);
        Environment.SetEnvironmentVariable("LP_TEST_OLD", "real");
        try
        {
            DotEnvFile.Load(path);

            Environment.GetEnvironmentVariable("LP_TEST_NEW").ShouldBe("file");
            Environment.GetEnvironmentVariable("LP_TEST_OLD").ShouldBe("real");
        }
        finally
        {
            Environment.SetEnvironmentVariable("LP_TEST_NEW", null);
            Environment.SetEnvironmentVariable("LP_TEST_OLD", null);
        }
    }

    [Fact]
    public void Find_WalksUpToTheParentFolders()
    {
        var path = Write("A=1");
        var deep = Directory.CreateDirectory(Path.Combine(_folder, "src", "Web")).FullName;

        DotEnvFile.Find(deep).ShouldBe(path);
    }
}
