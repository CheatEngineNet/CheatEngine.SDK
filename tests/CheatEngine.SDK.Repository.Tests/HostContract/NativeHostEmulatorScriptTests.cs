using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.HostContract;

/// <summary>
///     Repository-level checks on <c>tests/native-host-emulator/build.ps1</c> and its native sources: it is a genuine
///     build script (same category as the already-accepted <c>tests/native-abi-fixture/build.ps1</c>), never a
///     wrapper for hidden Cheat Engine access, and it checks every native command it runs.
/// </summary>
public sealed partial class NativeHostEmulatorScriptTests
{
	private const string BuildScriptRelativePath = "tests/native-host-emulator/build.ps1";

	[Fact]
	public void Build_script_checks_the_exit_code_after_every_native_command()
	{
		string[] lines = File.ReadAllLines(RepositoryFile(BuildScriptRelativePath));

		int nativeCommandCount = 0;
		int checkedCount = 0;
		for (int index = 0; index < lines.Length; index++)
		{
			if (!NativeCommandInvocation().IsMatch(lines[index]))
			{
				continue;
			}

			nativeCommandCount++;
			if (index + 1 < lines.Length && lines[index + 1].Contains("$LASTEXITCODE", StringComparison.Ordinal))
			{
				checkedCount++;
			}
		}

		Assert.True(nativeCommandCount > 0, "The build script should invoke at least one native command (cl.exe).");
		Assert.Equal(nativeCommandCount, checkedCount);
	}

	[Fact]
	public void Emulator_scripts_never_reference_a_cheat_engine_installation()
	{
		// Code only: the README legitimately explains, in prose, that this program never touches an installed Cheat
		// Engine, which itself names the install file it must not reference.
		string[] codeExtensions = [".ps1", ".cpp", ".h", ".json"];
		List<string> offendingFiles = [];
		foreach (string relativePath in RepositoryRoot.EnumerateSourceFiles("tests/native-host-emulator/*"))
		{
			if (Array.IndexOf(codeExtensions, Path.GetExtension(relativePath)) < 0)
			{
				continue;
			}

			string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path,
				relativePath.Replace('/', Path.DirectorySeparatorChar)));
			if (CheatEngineInstallationReference().IsMatch(text))
			{
				offendingFiles.Add(relativePath);
			}
		}

		Assert.True(offendingFiles.Count == 0,
			$"tests/native-host-emulator/** must never reference an installed Cheat Engine: {string.Join(", ", offendingFiles)}");
	}

	[Fact]
	public void Build_script_writes_only_under_its_output_directory()
	{
		string[] lines = File.ReadAllLines(RepositoryFile(BuildScriptRelativePath));

		foreach (string line in lines)
		{
			if (WriteCommand().IsMatch(line))
			{
				Assert.True(OutputDirectoryDerivedVariable().IsMatch(line),
					$"This write command does not reference an output-directory-derived variable: '{line.Trim()}'");
			}

			string codePortion = CommentStart().Split(line)[0];
			Assert.False(DriveLetterPath().IsMatch(codePortion),
				$"Hardcoded drive-letter path outside a comment: '{line.Trim()}'");
		}
	}

	private static string RepositoryFile(string relativePath)
	{
		return Path.Combine(RepositoryRoot.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	[GeneratedRegex(@"&\s*\$compiler\.Source\b", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex NativeCommandInvocation();

	[GeneratedRegex(@"Program Files\\Cheat Engine|cheatengine-x86_64",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
	private static partial Regex CheatEngineInstallationReference();

	[GeneratedRegex(@"^\s*(Copy-Item|Set-Content).*$",
		RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture, 1000)]
	private static partial Regex WriteCommand();

	[GeneratedRegex(
		@"resolvedOutputDirectory|nethostDllDestination|runtimeConfigDestination|manifestPath|\$exe\b|\$objectFile\b",
		RegexOptions.CultureInvariant, 1000)]
	private static partial Regex OutputDirectoryDerivedVariable();

	[GeneratedRegex(@"[A-Za-z]:\\", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex DriveLetterPath();

	[GeneratedRegex(@"(?<!['""])#", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex CommentStart();
}
