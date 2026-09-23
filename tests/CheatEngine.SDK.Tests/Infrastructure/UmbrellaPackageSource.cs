using System.Text.RegularExpressions;

namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>
///     Decides which package the packaging fixture tests. It is a pure function of the two environment values, read by the
///     fixture only, so the rules are unit-tested in parallel without touching the process environment:
///     <list type="bullet">
///         <item>
///             <see cref="UmbrellaPackage.PrebuiltPackageVariable" /> set: it must be the absolute path of an existing
///             <c>CheatEngine.SDK.&lt;SemVer&gt;.nupkg</c> file, which the fixture copies instead of packing.
///         </item>
///         <item>
///             Unset while <c>CI</c> is <c>true</c> (GitHub Actions sets it on every run): an error, because a CI run must
///             test the file it ships, and the Debug leg must not run these tests at all.
///         </item>
///         <item>Unset outside CI: the fixture packs the working tree, as a developer expects.</item>
///     </list>
/// </summary>
internal static partial class UmbrellaPackageSource
{
	private const string NupkgExtension = ".nupkg";

	/// <summary>The message of a CI run that did not hand the fixture its package.</summary>
	public static string MissingUnderContinuousIntegrationMessage =>
		$"{UmbrellaPackage.PrebuiltPackageVariable} is not set while CI=true. The Release leg must pack before testing " +
		"and pass the absolute path of the packed file in that variable, so these tests run on the exact package that is " +
		"uploaded, attested and published; the Debug leg must exclude these tests with " +
		$"--filter-not-trait \"Category={UmbrellaPackage.PackagingCategory}\". " +
		"See tests/CheatEngine.SDK.Tests/README.md#run-the-tests.";

	/// <summary>Chooses between the supplied package and a self-pack, or throws when CI supplied nothing usable.</summary>
	/// <param name="variableValue">The value of <see cref="UmbrellaPackage.PrebuiltPackageVariable" />.</param>
	/// <param name="continuousIntegrationValue">The value of the <c>CI</c> environment variable.</param>
	/// <exception cref="InvalidOperationException">The supplied value is not an umbrella package, or CI supplied none.</exception>
	public static UmbrellaPackageSelection Select(string? variableValue, string? continuousIntegrationValue)
	{
		if (string.IsNullOrWhiteSpace(variableValue))
		{
			if (string.Equals(continuousIntegrationValue?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(MissingUnderContinuousIntegrationMessage);
			}

			return new UmbrellaPackageSelection(UmbrellaPackageOrigin.SelfPacked, null);
		}

		if (!Path.IsPathFullyQualified(variableValue))
		{
			throw Rejected(variableValue, "is not an absolute path");
		}

		if (!File.Exists(variableValue))
		{
			throw Rejected(variableValue, "names no existing file");
		}

		if (!IsUmbrellaPackageFileName(Path.GetFileName(variableValue)))
		{
			throw Rejected(variableValue,
				$"is not the umbrella package: expected a file named {UmbrellaPackage.Id}.<SemVer version>{NupkgExtension} " +
				"(a relay package such as CheatEngine.SDK.PackageAssetCarrier.1.0.0.nupkg or a .snupkg symbol package is " +
				"not the package under test)");
		}

		return new UmbrellaPackageSelection(UmbrellaPackageOrigin.Prebuilt, variableValue);
	}

	/// <summary>
	///     Whether <paramref name="fileName" /> is <c>CheatEngine.SDK.&lt;SemVer&gt;.nupkg</c>: the umbrella, not the relay
	///     carrier package the fixture packs into the same feed, and not a symbol package.
	/// </summary>
	public static bool IsUmbrellaPackageFileName(string fileName)
	{
		ArgumentNullException.ThrowIfNull(fileName);
		string prefix = UmbrellaPackage.Id + ".";
		return fileName.StartsWith(prefix, StringComparison.Ordinal)
		       && fileName.EndsWith(NupkgExtension, StringComparison.Ordinal)
		       && fileName.Length > prefix.Length + NupkgExtension.Length
		       && SemanticVersion().IsMatch(fileName[prefix.Length..^NupkgExtension.Length]);
	}

	private static InvalidOperationException Rejected(string value, string reason)
	{
		return new InvalidOperationException(
			$"{UmbrellaPackage.PrebuiltPackageVariable}='{value}' {reason}. Set it to the absolute path of the " +
			$"{UmbrellaPackage.Id}.<version>{NupkgExtension} file that 'dotnet pack src/CheatEngine.SDK' produced, or unset " +
			"it outside CI to let the fixture pack the working tree.");
	}

	/// <summary>A SemVer 2.0 version without build metadata, as NuGet writes it into a package file name.</summary>
	[GeneratedRegex(
		"^(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)" +
		"(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?$",
		RegexOptions.CultureInvariant, 1000)]
	private static partial Regex SemanticVersion();
}
