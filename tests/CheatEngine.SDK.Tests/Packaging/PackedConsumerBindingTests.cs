using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     Runs package-only consumers against the freshly packed local feed, proving generated Lua bindings execute at
///     runtime and the package boundary contains precisely the assets that direct consumers require.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
[Trait("Category", UmbrellaPackage.PackagingCategory)]
public sealed class PackedConsumerBindingTests(PackagedUmbrellaFixture fixture)
{
	private static readonly string[] RequiredAotWarningIds = ["IL2026", "IL3050", "IL3058"];

	private static readonly string[] ExpectedLibNet10Assets =
	[
		"lib/net10.0/CheatEngine.SDK.dll",
		"lib/net10.0/CheatEngine.SDK.xml",
		"lib/net10.0/CheatEngine.SDK.Abi.dll",
		"lib/net10.0/CheatEngine.SDK.Abi.xml",
		"lib/net10.0/CheatEngine.SDK.Annotations.dll",
		"lib/net10.0/CheatEngine.SDK.Annotations.xml",
		"lib/net10.0/CheatEngine.SDK.Engine.dll",
		"lib/net10.0/CheatEngine.SDK.Engine.xml",
		"lib/net10.0/CheatEngine.SDK.Hosting.dll",
		"lib/net10.0/CheatEngine.SDK.Hosting.xml",
		"lib/net10.0/CheatEngine.SDK.Lua.dll",
		"lib/net10.0/CheatEngine.SDK.Lua.xml",
		"lib/net10.0/CheatEngine.SDK.Lua.Interop.dll",
		"lib/net10.0/CheatEngine.SDK.Lua.Interop.xml"
	];

	private static readonly string[] ExpectedBuildAssets =
	[
		"build/CheatEngine.SDK.props",
		"build/CheatEngine.SDK.targets",
		"build/native/cheatengine-sdk-lua-bridge.dll"
	];

	[Fact]
	public void Packed_runtime_consumer_executes_generated_bindings_and_preserves_lua_failure_origin()
	{
		Assert.True(fixture.PackedRuntimeConsumerRunSucceeded,
			$"The packed Lua runtime consumer did not exit successfully:{Environment.NewLine}" +
			fixture.PackedRuntimeConsumerRunOutput);

		Assert.Contains("SDK-022-RUNTIME-GLOBAL-MARSHALLER", fixture.PackedRuntimeConsumerRunOutput,
			StringComparison.Ordinal);
		Assert.Contains("SDK-022-RUNTIME-CALLBACK-MARSHALLER", fixture.PackedRuntimeConsumerRunOutput,
			StringComparison.Ordinal);
		Assert.Contains("SDK-022-RUNTIME-COLLISION-LEASE", fixture.PackedRuntimeConsumerRunOutput,
			StringComparison.Ordinal);
		Assert.Contains("SDK-022-RUNTIME-LUA-RUNTIME-ERROR", fixture.PackedRuntimeConsumerRunOutput,
			StringComparison.Ordinal);
		Assert.Contains("SDK-022-RUNTIME-PROOF", fixture.PackedRuntimeConsumerRunOutput, StringComparison.Ordinal);
	}

	[Fact]
	public void Duplicate_lua_function_consumer_is_rejected_with_CESDK2005()
	{
		Assert.False(fixture.DuplicateLuaFunctionConsumerBuildSucceeded,
			"The package-only consumer with duplicate Lua function names unexpectedly compiled.");
		Assert.Contains("CESDK2005", fixture.DuplicateLuaFunctionConsumerBuildOutput, StringComparison.Ordinal);
	}

	[Fact]
	public void Packaged_AOT_consumer_publishes_and_runs_only_as_a_standalone_executable()
	{
		Assert.True(fixture.PackedAotConsumerPublishSucceeded,
			$"The package-only AOT consumer did not publish successfully:{Environment.NewLine}" +
			fixture.PackedAotConsumerPublishOutput);
		Assert.True(fixture.PackedAotConsumerRunSucceeded,
			$"The published package-only AOT consumer did not exit successfully:{Environment.NewLine}" +
			fixture.PackedAotConsumerRunOutput);
		Assert.Contains("SDK-022-AOT-GENERATED-BINDING", fixture.PackedAotConsumerRunOutput,
			StringComparison.Ordinal);
		Assert.Contains("SDK-022-AOT-STANDALONE", fixture.PackedAotConsumerRunOutput, StringComparison.Ordinal);
		Assert.Contains("SDK-022-AOT-NO-CE-HOST", fixture.PackedAotConsumerRunOutput, StringComparison.Ordinal);
	}

	[Fact]
	public void Packaged_AOT_consumer_evaluates_its_publication_contract_and_does_not_suppress_trim_or_AOT_warnings()
	{
		Assert.Equal("true", fixture.PackedAotConsumerAllowUnsafeBlocks, true);
		Assert.Equal("true", fixture.PackedAotConsumerProperties["PublishAot"], true);
		Assert.Equal("true", fixture.PackedAotConsumerProperties["PublishTrimmed"], true);
		Assert.Equal("true", fixture.PackedAotConsumerProperties["SelfContained"], true);
		Assert.Equal("win-x64", fixture.PackedAotConsumerProperties["RuntimeIdentifier"], true);
		Assert.Equal("true", fixture.PackedAotConsumerProperties["VerifyReferenceAotCompatibility"], true);

		foreach (string warningId in RequiredAotWarningIds)
		{
			Assert.Contains(warningId, fixture.PackedAotConsumerWarningsAsErrors, StringComparison.Ordinal);
			Assert.DoesNotContain(warningId, fixture.PackedAotConsumerProperties["NoWarn"],
				StringComparison.OrdinalIgnoreCase);
		}
	}

	[Fact]
	public void Legacy_Aob_consumer_still_compiles_against_the_packed_package()
	{
		Assert.True(fixture.LegacyAobConsumerBuildSucceeded,
			"The packed default consumer did not compile the legacy AobScanner.TryScan overload probe.");
	}

	[Fact]
	public void Package_boundary_has_exact_library_and_build_assets()
	{
		Assert.Equal(
			ExpectedLibNet10Assets.OrderBy(static entry => entry, StringComparer.Ordinal),
			fixture.PackageEntries
				.Where(static entry => entry.StartsWith("lib/net10.0/", StringComparison.Ordinal))
				.OrderBy(static entry => entry, StringComparer.Ordinal));
		Assert.Equal(
			ExpectedBuildAssets.OrderBy(static entry => entry, StringComparer.Ordinal),
			fixture.PackageEntries
				.Where(static entry => entry.StartsWith("build/", StringComparison.Ordinal))
				.OrderBy(static entry => entry, StringComparer.Ordinal));
		Assert.DoesNotContain(fixture.PackageEntries,
			static entry => entry.Contains("lua53-64.dll", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(fixture.PackageEntries,
			static entry => entry.StartsWith("runtimes/", StringComparison.Ordinal));
	}
}
