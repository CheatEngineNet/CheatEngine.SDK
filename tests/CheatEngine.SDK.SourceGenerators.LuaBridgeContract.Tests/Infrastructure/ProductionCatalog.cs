using System.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Infrastructure;

/// <summary>The exact repository catalogue embedded in the test assembly as deterministic test data.</summary>
internal static class ProductionCatalog
{
    private const string ResourceName = "CheatEngine.SDK.LuaBridgeContract.Tests.ProductionCatalog.json";

    public static string Read()
    {
        var assembly = typeof(ProductionCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }
}
