using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     The rules that decide which package the packaging fixture tests (<see cref="UmbrellaPackageSource.Select" />). They
///     are a pure function of two values, so these facts run in both CI legs without the fixture and without touching the
///     process environment. The files are empty stand-ins under a private temporary folder: only the path is judged.
/// </summary>
public sealed class UmbrellaPackageSourceTests : IDisposable
{
	private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("cheatengine-sdk-package-source-");

	/// <inheritdoc />
	public void Dispose()
	{
		_directory.Delete(true);
	}

	[Theory]
	[InlineData("CheatEngine.SDK.2.0.0.nupkg", null)]
	[InlineData("CheatEngine.SDK.2.0.0-alpha.0.18.nupkg", "true")]
	[InlineData("CheatEngine.SDK.2.1.0-rc.1.nupkg", "false")]
	public void Supplied_absolute_umbrella_nupkg_is_selected_as_prebuilt(string fileName, string? ci)
	{
		string path = CreateFile(fileName);

		UmbrellaPackageSelection selection = UmbrellaPackageSource.Select(path, ci);

		Assert.Equal(UmbrellaPackageOrigin.Prebuilt, selection.Origin);
		Assert.Equal(path, selection.PrebuiltPath);
	}

	[Fact]
	public void Relative_or_missing_supplied_path_is_rejected_and_the_message_names_the_variable()
	{
		string missing = Path.Combine(_directory.FullName, "CheatEngine.SDK.2.0.0.nupkg");

		InvalidOperationException relative = Assert.Throws<InvalidOperationException>(() =>
			UmbrellaPackageSource.Select(Path.Combine("artifacts", "nuget", "CheatEngine.SDK.2.0.0.nupkg"), "true"));
		InvalidOperationException absent = Assert.Throws<InvalidOperationException>(() =>
			UmbrellaPackageSource.Select(missing, "true"));

		Assert.Contains(UmbrellaPackage.PrebuiltPackageVariable, relative.Message, StringComparison.Ordinal);
		Assert.Contains("is not an absolute path", relative.Message, StringComparison.Ordinal);
		Assert.Contains(UmbrellaPackage.PrebuiltPackageVariable, absent.Message, StringComparison.Ordinal);
		Assert.Contains("names no existing file", absent.Message, StringComparison.Ordinal);
		Assert.Contains(missing, absent.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("CheatEngine.SDK.PackageAssetCarrier.1.0.0.nupkg")]
	[InlineData("CheatEngine.SDK.2.0.0.snupkg")]
	[InlineData("CheatEngine.SDK.Abi.2.0.0.nupkg")]
	[InlineData("CheatEngine.Client.0.1.0.nupkg")]
	[InlineData("CheatEngine.SDK.nupkg")]
	[InlineData("CheatEngine.SDK.2.0.nupkg")]
	[InlineData("CheatEngine.SDK.02.0.0.nupkg")]
	[InlineData("cheatengine.sdk.2.0.0.nupkg")]
	[InlineData("CheatEngine.SDK.2.0.0.zip")]
	public void Relay_carrier_or_symbol_package_is_rejected_as_not_the_umbrella(string fileName)
	{
		string path = CreateFile(fileName);

		InvalidOperationException rejection =
			Assert.Throws<InvalidOperationException>(() => UmbrellaPackageSource.Select(path, "true"));

		Assert.Contains(UmbrellaPackage.PrebuiltPackageVariable, rejection.Message, StringComparison.Ordinal);
		Assert.Contains("is not the umbrella package", rejection.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(null, "true")]
	[InlineData("", "TRUE")]
	[InlineData("   ", "True")]
	public void Missing_variable_under_ci_fails_with_an_actionable_message(string? variable, string ci)
	{
		InvalidOperationException failure =
			Assert.Throws<InvalidOperationException>(() => UmbrellaPackageSource.Select(variable, ci));

		Assert.Equal(UmbrellaPackageSource.MissingUnderContinuousIntegrationMessage, failure.Message);
		Assert.StartsWith(UmbrellaPackage.PrebuiltPackageVariable + " is not set while CI=true.", failure.Message,
			StringComparison.Ordinal);
		Assert.Contains("--filter-not-trait \"Category=Packaging\"", failure.Message, StringComparison.Ordinal);
		Assert.Contains("tests/CheatEngine.SDK.Tests/README.md#run-the-tests", failure.Message,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(null, null)]
	[InlineData("", "false")]
	[InlineData(null, "")]
	[InlineData("  ", "1")]
	public void Missing_variable_outside_ci_selects_a_self_pack(string? variable, string? ci)
	{
		UmbrellaPackageSelection selection = UmbrellaPackageSource.Select(variable, ci);

		Assert.Equal(UmbrellaPackageOrigin.SelfPacked, selection.Origin);
		Assert.Null(selection.PrebuiltPath);
	}

	private string CreateFile(string fileName)
	{
		string path = Path.Combine(_directory.FullName, fileName);
		File.WriteAllBytes(path, []);
		return path;
	}
}
