using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Hosting.Tests.Coexistence;

/// <summary>
///     Runs <c>tests/native-host-emulator/build.ps1</c>'s output against a pair of plugin directories. One process per
///     scenario (pitfall #10 of s-host.md section 5: only one .NET runtime can ever load per process), a 60 s
///     timeout, and an explicit <c>--dotnet-root</c> derived from the running test host's own runtime directory --
///     never an installed Cheat Engine, never the ambient environment.
/// </summary>
internal static class NativeHostEmulatorRunner
{
	private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(60);

	internal static NativeHostEmulatorResult Run(
		string emulatorDirectory,
		string pluginADirectory,
		string pluginBDirectory,
		NativeHostEmulatorAlcRoute route,
		string factsPath)
	{
		string exePath = Path.Combine(emulatorDirectory, "ce-host-emulator.exe");
		if (!File.Exists(exePath))
		{
			throw new FileNotFoundException(
				$"'{exePath}' does not exist: run tests/native-host-emulator/build.ps1 first.", exePath);
		}

		string runtimeConfigPath = Path.Combine(emulatorDirectory, "ce-like.runtimeconfig.json");
		string luaPath = Path.Combine(CoexistencePluginLayout.RepoRoot, "native", "cheat-engine", "lua53-64.dll");

		ProcessStartInfo startInfo = new(exePath)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		AddArgument(startInfo, "--lua", luaPath);
		AddArgument(startInfo, "--dotnet-root", ResolveDotNetRoot());
		AddArgument(startInfo, "--runtimeconfig", runtimeConfigPath);
		AddArgument(startInfo, "--plugin-a-dir", pluginADirectory);
		AddArgument(startInfo, "--plugin-a-assembly", CoexistencePluginLayout.PluginAAssemblyName);
		AddArgument(startInfo, "--plugin-b-dir", pluginBDirectory);
		AddArgument(startInfo, "--plugin-b-assembly", CoexistencePluginLayout.PluginBAssemblyName);
		AddArgument(startInfo, "--alc", route == NativeHostEmulatorAlcRoute.Default ? "default" : "component");
		AddArgument(startInfo, "--facts", factsPath);

		using Process process = new() { StartInfo = startInfo };
		process.Start();
		string standardOutput = process.StandardOutput.ReadToEnd();
		string standardError = process.StandardError.ReadToEnd();
		bool exited = process.WaitForExit((int) ProcessTimeout.TotalMilliseconds);
		if (!exited)
		{
			process.Kill(true);
			throw new TimeoutException($"'{exePath}' did not exit within {ProcessTimeout}.");
		}

		int exitCode = process.ExitCode;
		Dictionary<string, string> facts = File.Exists(factsPath)
			? ReadFacts(factsPath)
			: new Dictionary<string, string>(StringComparer.Ordinal);

		if (facts.Count == 0 && exitCode != 0)
		{
			throw new InvalidOperationException(
				$"'{exePath}' exited {exitCode} without writing facts. Standard output:\n{standardOutput}\nStandard error:\n{standardError}");
		}

		return new NativeHostEmulatorResult(exitCode, facts);
	}

	/// <summary>
	///     Three levels up from the running test host's own <c>Microsoft.NETCore.App</c> runtime directory
	///     (<c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;/</c>), exactly as s-host.md WI-7 specifies.
	/// </summary>
	private static string ResolveDotNetRoot()
	{
		// GetRuntimeDirectory() ends with a trailing separator (".../Microsoft.NETCore.App/<version>/"), and
		// Directory.GetParent/DirectoryInfo.Parent of a trailing-separator path is a documented no-op (it trims the
		// separator without moving up a level): trim it first, so three .Parent steps really are three levels up
		// (<version> -> Microsoft.NETCore.App -> shared -> the dotnet root), exactly as s-host.md WI-7 specifies.
		string runtimeDirectory = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());
		DirectoryInfo? root = new DirectoryInfo(runtimeDirectory).Parent?.Parent?.Parent;
		if (root is null)
		{
			throw new InvalidOperationException(
				$"Could not derive a .NET root three levels above '{runtimeDirectory}'.");
		}

		return root.FullName;
	}

	private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
	{
		startInfo.ArgumentList.Add(name);
		startInfo.ArgumentList.Add(value);
	}

	private static Dictionary<string, string> ReadFacts(string path)
	{
		Dictionary<string, string> facts = new(StringComparer.Ordinal);
		foreach (string line in File.ReadLines(path))
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			int separator = line.IndexOf('=', StringComparison.Ordinal);
			if (separator <= 0)
			{
				continue;
			}

			facts[line[..separator]] = line[(separator + 1)..];
		}

		return facts;
	}
}
