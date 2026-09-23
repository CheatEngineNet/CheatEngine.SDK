using System.Text.Json;

namespace CheatEngine.SDK.Abi.Tests.Support;

/// <summary>
///     Read access to the committed <c>tests/CheatEngine.SDK.Repository.Tests/Abi/TestData/classic-slot-registry.json</c>, embedded in this test assembly. The
///     registry's authority is the pinned host source <c>plugin.pas</c>; the SDK types must agree with it, never the
///     other way round.
/// </summary>
internal static class ClassicSlotRegistry
{
	private const string ResourceName = "CheatEngine.SDK.Abi.Tests.ClassicSlotRegistry.json";

	private static readonly Lazy<JsonElement> SRoot = new(Load);

	/// <summary>The registry document.</summary>
	public static JsonElement Root => SRoot.Value;

	/// <summary>The 159 slot records, in slot order.</summary>
	public static JsonElement[] Slots => [.. Root.GetProperty("slots").EnumerateArray()];

	/// <summary>The nine callback categories, in plugin-type order.</summary>
	public static JsonElement[] CallbackCategories => [.. Root.GetProperty("callbackCategories").EnumerateArray()];

	/// <summary>The minimum declared size of <paramref name="slot" />: <c>8 * (slot + 1)</c> for a pointer slot.</summary>
	public static int MinDeclaredSize(int slot)
	{
		return Slots[slot].GetProperty("minDeclaredSize").GetInt32();
	}

	private static JsonElement Load()
	{
		using Stream stream = typeof(ClassicSlotRegistry).Assembly.GetManifestResourceStream(ResourceName)
							  ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
		using JsonDocument document = JsonDocument.Parse(stream);
		return document.RootElement.Clone();
	}
}
