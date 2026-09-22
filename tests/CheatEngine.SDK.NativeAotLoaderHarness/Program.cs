using System.Runtime.InteropServices;
using System.Security.Cryptography;

using CheatEngine.SDK.NativeAotLibraryProbe;

namespace CheatEngine.SDK.NativeAotLoaderHarness;

/// <summary>Runs bounded file analysis or process-resident name queries for the SDK-006 NativeAOT library fixture.</summary>
internal static class Program
{
	private const string AnalyzeMode = "--analyze";
	private const string LoadMode = "--load";
	private const string AcknowledgeProcessResidentLoad = "--acknowledge-process-resident-load";
	private const string NativePluginPrefix = "CEPlugin_";
	private const string FixtureFileName = "CheatEngine.SDK.NativeAotLibraryProbe.dll";

	/// <summary>Runs the requested bounded observation.</summary>
	public static int Main(string[] arguments)
	{
		try
		{
			return Run(arguments);
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine($"harness.error={exception.Message}");
			return 1;
		}
	}

	private static int Run(string[] arguments)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("The SDK-006 NativeAOT library profile is Windows x64 only.");
		}

		if (arguments.Length == 2 && string.Equals(arguments[0], AnalyzeMode, StringComparison.Ordinal))
		{
			Inspect(arguments[1]);
			return 0;
		}

		if (arguments.Length == 2 && string.Equals(arguments[0], LoadMode, StringComparison.Ordinal) &&
			string.Equals(arguments[1], AcknowledgeProcessResidentLoad, StringComparison.Ordinal))
		{
			LoadAndQueryNames();
			return 0;
		}

		throw new ArgumentException(
			"Usage: --analyze <probe.dll> | --load --acknowledge-process-resident-load",
			nameof(arguments));
	}

	private static void Inspect(string libraryPath)
	{
		using LibraryInspection inspection = ReadAndValidateProbe(libraryPath);
		Console.WriteLine("mode=byte-only-analysis");
		Console.WriteLine($"library.path={inspection.Path}");
		Console.WriteLine($"library.sha256={inspection.Sha256}");
		Console.WriteLine("library.mapped=false");

		foreach (string exportName in NativeAotLibraryProbeExportNames.Required)
		{
			Console.WriteLine($"export.name.{exportName}=present");
		}
	}

	private static void LoadAndQueryNames()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, FixtureFileName);
		using LibraryInspection inspection = ReadAndValidateProbe(fixturePath);
		Console.WriteLine("mode=process-resident-name-query");
		Console.WriteLine($"library.path={inspection.Path}");
		Console.WriteLine($"library.sha256={inspection.Sha256}");
		Console.WriteLine("library.identity=profile-adjacent-fixture");
		Console.WriteLine("library.activation=not-attempted");

		nint module = NativeLibrary.Load(inspection.Path);
		Console.WriteLine("library.mapped=true");

		foreach (string exportName in NativeAotLibraryProbeExportNames.Required)
		{
			if (!NativeLibrary.TryGetExport(module, exportName, out nint address))
			{
				throw new InvalidOperationException($"The mapped fixture does not expose '{exportName}'.");
			}

			if (address == 0)
			{
				throw new InvalidOperationException($"The mapped fixture resolved '{exportName}' to a null address.");
			}

			Console.WriteLine($"export.query.{exportName}=present");
		}

		// NativeAOT shared-library unload is unsupported. This dedicated process exits after the observation instead.
		Console.WriteLine("library.unload=not-attempted");
	}

	private static LibraryInspection ReadAndValidateProbe(string libraryPath)
	{
		string fullPath = Path.GetFullPath(libraryPath);
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException("The NativeAOT library probe does not exist.", fullPath);
		}

		FileStream fileLock = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		byte[] bytes;
		try
		{
			if (fileLock.Length > int.MaxValue)
			{
				throw new InvalidOperationException("The NativeAOT library probe is too large to inspect safely.");
			}

			bytes = new byte[(int) fileLock.Length];
			fileLock.ReadExactly(bytes);
		}
		catch
		{
			fileLock.Dispose();
			throw;
		}

		string sha256 = Convert.ToHexString(SHA256.HashData(bytes));
		List<string> exportNames;
		try
		{
			exportNames = PortableExecutableExportReader.ReadExportNames(bytes);
			EnsureNoNativePluginExports(exportNames);
			EnsureExpectedExports(exportNames);
		}
		catch
		{
			fileLock.Dispose();
			throw;
		}

		return new LibraryInspection(fullPath, sha256, fileLock);
	}

	private static void EnsureNoNativePluginExports(IReadOnlyList<string> exportNames)
	{
		foreach (string exportName in exportNames)
		{
			if (exportName.StartsWith(NativePluginPrefix, StringComparison.Ordinal))
			{
				throw new InvalidOperationException(
					"The harness refuses a DLL that exposes a Cheat Engine native-plugin entry point.");
			}
		}
	}

	private static void EnsureExpectedExports(IReadOnlyList<string> exportNames)
	{
		foreach (string requiredName in NativeAotLibraryProbeExportNames.Required)
		{
			bool found = false;
			foreach (string exportName in exportNames)
			{
				if (!string.Equals(exportName, requiredName, StringComparison.Ordinal))
				{
					continue;
				}

				found = true;
				break;
			}

			if (!found)
			{
				throw new InvalidOperationException(
					$"The file does not expose required fixture export '{requiredName}'.");
			}
		}
	}

	private sealed class LibraryInspection(string path, string sha256, FileStream fileLock) : IDisposable
	{
		public string Path
		{
			get;
		} = path;

		public string Sha256
		{
			get;
		} = sha256;

		public void Dispose()
		{
			fileLock.Dispose();
		}
	}
}
