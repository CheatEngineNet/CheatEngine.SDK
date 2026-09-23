using System.Security.Cryptography;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     Completes the PE audit at the package boundary: the direct package-reference build and publish targets must copy
///     the exact audited bridge, rather than another native asset that merely happens to share its file name.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
[Trait("Category", UmbrellaPackage.PackagingCategory)]
public sealed class NativeBridgePackagingAuditTests(PackagedUmbrellaFixture fixture)
{
	private const string BridgeRelativePath =
		"native/cheatengine-sdk-lua-bridge/runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll";

	private const string PackedBridgeEntry = "build/native/cheatengine-sdk-lua-bridge.dll";

	[Fact]
	public void Direct_consumer_build_and_publish_copy_the_exact_audited_bridge_asset()
	{
		string auditedHash = CalculateSha256(RepositoryLayout.PathOf(BridgeRelativePath));

		Assert.True(File.Exists(fixture.DefaultNativeBridgePath),
			"The direct consumer build did not receive the bridge.");
		Assert.True(File.Exists(fixture.DefaultPublishedNativeBridgePath),
			"The direct consumer publish output did not receive the bridge.");
		Assert.Equal(auditedHash, CalculateSha256(fixture.DefaultNativeBridgePath));
		Assert.Equal(auditedHash, CalculateSha256(fixture.DefaultPublishedNativeBridgePath));
	}

	/// <summary>
	///     The workspace comparison above holds only when the package was packed from this workspace. This one holds for
	///     every origin, including the exact CI package: what a direct consumer deploys is the bridge the package carries.
	/// </summary>
	[Fact]
	public void Direct_consumer_bridges_are_byte_identical_to_the_packed_build_native_entry()
	{
		string packedHash = Convert.ToHexString(SHA256.HashData(
			NupkgInspector.ReadEntryBytes(fixture.PackagePath, PackedBridgeEntry))).ToLowerInvariant();

		Assert.Equal(packedHash, CalculateSha256(fixture.DefaultNativeBridgePath));
		Assert.Equal(packedHash, CalculateSha256(fixture.DefaultPublishedNativeBridgePath));
	}

	private static string CalculateSha256(string path)
	{
		return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
	}
}
