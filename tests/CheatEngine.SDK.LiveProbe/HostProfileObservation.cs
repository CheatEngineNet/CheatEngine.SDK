using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Lua.Interop.Loading;

namespace LiveProbe;

// This is evidence capture, not a runtime capability detector. The command that calls it is authorization-gated and
// emits a self-contained JSON record for the operator to retain outside the repository.
internal static class HostProfileObservation
{
	private const string BridgeFileName = "cheatengine-sdk-lua-bridge.dll";
	private const string ObservedOutcome = "observed";

	internal static string Capture(AuthorizationDecision authorization)
	{
		using MemoryStream stream = new();
		using (Utf8JsonWriter writer = new(stream))
		{
			writer.WriteStartObject();
			writer.WriteString("schema", "ce77-live-host-profile-v1");
			writer.WriteString("capturedAtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
			writer.WriteString("catalogRevision", "ce-7.7.0.10621-x64-source-index");

			writer.WriteStartObject("authorization");
			writer.WriteBoolean("allowed", authorization.IsAllowed);
			writer.WriteString("outcome", authorization.Reason);
			writer.WriteString("expiresUtc", authorization.ExpiresUtc.ToString("O", CultureInfo.InvariantCulture));
			writer.WriteEndObject();

			WriteFileIdentity(writer, "host", authorization.HostPath, authorization.HostSha256);
			WriteFileIdentity(writer, "lua", FindLoadedModulePath(LuaModule.CheatEngine64ModuleName), null);
			// The bridge module the process actually loaded (through the plugin's dependency resolution), never a file
			// guessed next to AppContext.BaseDirectory: under Cheat Engine's hostfxr runtime that is not the plugin folder.
			WriteFileIdentity(writer, "bridge", FindLoadedModulePath(BridgeFileName), null);
			WriteFileIdentity(writer, "plugin", typeof(HostProfileObservation).Assembly.Location, null);

			writer.WriteStartObject("target");
			writer.WriteNumber("processId", authorization.TargetProcessId);
			WriteFileIdentityFields(writer, authorization.TargetPath, authorization.TargetSha256);
			writer.WriteEndObject();
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static void WriteFileIdentity(Utf8JsonWriter writer, string name, string? path, string? knownHash)
	{
		writer.WriteStartObject(name);
		WriteFileIdentityFields(writer, path, knownHash);
		writer.WriteEndObject();
	}

	private static void WriteFileIdentityFields(Utf8JsonWriter writer, string? path, string? knownHash)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			writer.WriteString("outcome", "not-observed");
			return;
		}

		writer.WriteString("path", path);
		string outcome = InspectFileForIdentity(path, static filePath => File.OpenRead(filePath));
		writer.WriteString("outcome", outcome);
		if (!string.Equals(outcome, ObservedOutcome, StringComparison.Ordinal))
		{
			return;
		}

		writer.WriteString("sha256", knownHash ?? HashFile(path));
		writer.WriteString("fileVersion", ObserveFileVersion(path,
			static filePath => FileVersionInfo.GetVersionInfo(filePath).FileVersion));

		try
		{
			using FileStream file = File.OpenRead(path);
			using PEReader reader = new(file);
			writer.WriteString("machine", reader.PEHeaders.CoffHeader.Machine.ToString());
		}
		catch (Exception exception) when (exception is BadImageFormatException or IOException
											  or UnauthorizedAccessException)
		{
			writer.WriteString("machine", "unavailable: " + exception.GetType().Name);
		}
	}

	// The opener is an internal test seam only. Production passes File.OpenRead, which already underpins the later hash
	// and PE reads; this preliminary open avoids turning an access or sharing failure into a missing-file claim.
	internal static string InspectFileForIdentity(string? path, Func<string, Stream> openRead)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "not-observed";
		}

		try
		{
			using Stream file = openRead(path);
			return ObservedOutcome;
		}
		catch (FileNotFoundException)
		{
			return "file-not-found";
		}
		catch (DirectoryNotFoundException)
		{
			return "file-not-found";
		}
		catch (UnauthorizedAccessException)
		{
			return "unavailable: UnauthorizedAccessException";
		}
		catch (IOException exception)
		{
			return "unavailable: " + exception.GetType().Name;
		}
	}

	// File metadata is read after the preliminary open, so it needs its own failure classification when the file is
	// deleted, locked, or access is revoked in between. The reader is an internal test seam only.
	internal static string ObserveFileVersion(string path, Func<string, string?> getFileVersion)
	{
		try
		{
			return getFileVersion(path) ?? "not-present";
		}
		catch (Exception exception) when (exception is ArgumentException or Win32Exception
											  or IOException or UnauthorizedAccessException)
		{
			return "unavailable: " + exception.GetType().Name;
		}
	}

	private static string? FindLoadedModulePath(string moduleName)
	{
		try
		{
			using Process process = Process.GetCurrentProcess();
			foreach (ProcessModule module in process.Modules)
			{
				if (string.Equals(Path.GetFileName(module.FileName), moduleName, StringComparison.OrdinalIgnoreCase))
				{
					return module.FileName;
				}
			}
		}
		catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException
											  or Win32Exception)
		{
			HostLog.Write(HostLogLevel.Warning,
				"CE 7.7 host-profile probe could not enumerate loaded modules.", exception);
		}

		return null;
	}

	private static string HashFile(string path)
	{
		try
		{
			using FileStream file = File.OpenRead(path);
			return Convert.ToHexString(SHA256.HashData(file));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return "unavailable: " + exception.GetType().Name;
		}
	}
}
