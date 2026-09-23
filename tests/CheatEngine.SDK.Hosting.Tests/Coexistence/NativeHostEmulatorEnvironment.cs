namespace CheatEngine.SDK.Hosting.Tests.Coexistence;

/// <summary>
///     Locates the directory <c>tests/native-host-emulator/build.ps1</c> produced. Same opt-in shape as
///     <c>CheatEngine.SDK.Abi.Tests.Fixture.NativeAbiFixtureFacts</c>: unset locally, the emulator-dependent tests
///     assert the documented opt-out and return (never <see cref="Assert.Skip(string)" />); required mode without a
///     directory is an actionable error.
/// </summary>
internal static class NativeHostEmulatorEnvironment
{
	/// <summary>Name of the CI-provided absolute path to the built native host emulator directory.</summary>
	internal const string DirectoryEnvironmentVariable = "CESDK_NATIVE_HOST_EMULATOR_DIR";

	/// <summary>Name of the opt-in gate that makes the native host emulator mandatory.</summary>
	internal const string RequiredEnvironmentVariable = "CESDK_NATIVE_HOST_EMULATOR_REQUIRED";

	/// <summary>Reads both environment variables and applies <see cref="ResolveDirectory" />.</summary>
	internal static string? FromEnvironment()
	{
		return ResolveDirectory(
			Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable),
			Environment.GetEnvironmentVariable(RequiredEnvironmentVariable));
	}

	/// <summary>Applies the opt-in rule: a supplied directory wins; required mode without one is an error.</summary>
	internal static string? ResolveDirectory(string? directory, string? requiredMode)
	{
		if (!string.IsNullOrWhiteSpace(directory))
		{
			return directory;
		}

		if (string.Equals(requiredMode, "true", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"'{RequiredEnvironmentVariable}=true' requires '{DirectoryEnvironmentVariable}' to name a built native host emulator directory.");
		}

		return null;
	}
}
