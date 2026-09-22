using System.Reflection;
using System.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Infrastructure;

/// <summary>The exact checked-in C11 bridge source embedded as deterministic parity-test data.</summary>
internal static class ProductionNativeBridge
{
	private const string ResourceName = "CheatEngine.SDK.LuaBridgeContract.Tests.ProductionNativeBridge.c";

	public static string Read()
	{
		Assembly assembly = typeof(ProductionNativeBridge).Assembly;
		using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
		Assert.NotNull(stream);
		using StreamReader reader = new(stream, Encoding.UTF8, true);
		return reader.ReadToEnd();
	}
}
