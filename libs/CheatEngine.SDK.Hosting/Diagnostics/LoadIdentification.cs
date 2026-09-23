using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Loading;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Hosting.Diagnostics;

/// <summary>
///     Builds and emits the opt-in <c>CheatEngineSdkIdentification</c> line: one bounded, path-free entry written at
///     most once per enable attempt, before any Lua binding or plugin construction is attempted (A21-34, A24-11,
///     A24-19, A20-Q46-3).
/// </summary>
/// <remarks>
///     <para>
///         <b>Unqualified.</b> Every field here is a fact this SDK copy can read about itself and the host process; none
///         of it is confirmed by a host-based qualification run. It is diagnostic only, never a precondition for any
///         SDK behaviour.
///     </para>
///     <para>
///         <b>Non-reentrant.</b> Building the line calls no Lua API, constructs no plugin, and never writes to
///         <see cref="HostLog" /> while it runs; only the caller writes the finished line, once, after building
///         finishes.
///     </para>
/// </remarks>
internal static unsafe partial class LoadIdentification
{
	private const string LinePrefix = "CheatEngineSdkIdentification: ";
	private const int MaxValueLength = 128;
	private const int MaxEntryLength = 1024;
	private const string Unavailable = "unavailable";
	private const string Unknown = "unknown";
	private const uint ModuleHandleUnchangedRefCountFlag = 0x4; // GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT
	private const long MaxHashedFileBytes = 16 * 1024 * 1024;

	[GeneratedRegex("(?<![0-9a-fA-F])[0-9a-fA-F]{40}(?![0-9a-fA-F])", RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 1000)]
	private static partial Regex CommitPattern();

	[GeneratedRegex("^[0-9a-f]{64}:[0-9a-f]{64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex BridgeFingerprintPattern();

	/// <summary>
	///     Builds and, when accepted by <see cref="HostLog.IsIdentifyOnEnableRequested" />, writes the identification
	///     line for the enable attempt in progress. Safe to call even when <paramref name="exports" /> is null or the
	///     record it points at cannot be trusted yet: only <c>SizeOfExportedFunctions</c> is read, never a function
	///     pointer inside it.
	/// </summary>
	/// <param name="exports">The host's raw exports record, copied later by the caller; read-only here.</param>
	/// <param name="pluginId">The id the host assigned for this enable.</param>
	/// <param name="factoryType">The registered plugin factory type.</param>
	internal static void EmitIfRequested(ManagedExportedFunctions* exports, uint pluginId, Type factoryType)
	{
		if (!HostLog.IsIdentifyOnEnableRequested())
		{
			return;
		}

		string line = Build(exports, pluginId, factoryType);
		HostLog.Information(line);
	}

	/// <summary>Builds the line unconditionally. Exposed for direct testing of content and bounds.</summary>
	internal static string Build(ManagedExportedFunctions* exports, uint pluginId, Type factoryType)
	{
		StringBuilder builder = new(MaxEntryLength);
		builder.Append(LinePrefix);

		AppendSdkVersion(builder);
		AppendHostingIdentity(builder);
		AppendField(builder, "plugin.id", pluginId.ToString(CultureInfo.InvariantCulture));
		AppendField(builder, "plugin.assembly", DescribeAssembly(factoryType.Assembly));
		AppendField(builder, "host.argument",
			PluginHost.LastInitRecordArgument.ToString(CultureInfo.InvariantCulture));
		AppendField(builder, "exports.size", DescribeExportsSize(exports));
		AppendBridgeFields(builder);
		AppendLuaFields(builder);
		AppendCeFields(builder);
		AppendField(builder, "runtime", RuntimeInformation.FrameworkDescription);
		AppendField(builder, "arch", RuntimeInformation.ProcessArchitecture.ToString(), last: true);

		if (builder.Length > MaxEntryLength)
		{
			builder.Length = MaxEntryLength;
		}

		return builder.ToString();
	}

	private static void AppendSdkVersion(StringBuilder builder)
	{
		string hostingVersion = ReadInformationalVersion(typeof(PluginHost).Assembly);
		string luaVersion = ReadInformationalVersion(typeof(LuaRuntime).Assembly);
		string luaInteropVersion = ReadInformationalVersion(typeof(LuaApi).Assembly);
		string abiVersion = ReadInformationalVersion(typeof(AbiConstants).Assembly);

		bool consistent = string.Equals(hostingVersion, luaVersion, StringComparison.Ordinal)
						  && string.Equals(hostingVersion, luaInteropVersion, StringComparison.Ordinal)
						  && string.Equals(hostingVersion, abiVersion, StringComparison.Ordinal)
						  && !string.Equals(hostingVersion, Unknown, StringComparison.Ordinal);

		AppendField(builder, "sdk.version", hostingVersion);
		AppendField(builder, "sdk.commit", ParseCommit(hostingVersion));
		AppendField(builder, "sdk.consistent", consistent ? "true" : "false");
	}

	private static string ReadInformationalVersion(Assembly assembly)
	{
		try
		{
			string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
				?.InformationalVersion;
			return string.IsNullOrEmpty(version) ? Unknown : version;
		}
		catch (Exception)
		{
			return Unknown;
		}
	}

	private static string ParseCommit(string informationalVersion)
	{
		if (string.Equals(informationalVersion, Unknown, StringComparison.Ordinal))
		{
			return Unknown;
		}

		Match match = CommitPattern().Match(informationalVersion);
		return match.Success ? match.Value.ToLowerInvariant() : Unknown;
	}

	private static void AppendHostingIdentity(StringBuilder builder)
	{
		string mvid;
		try
		{
			mvid = typeof(PluginHost).Assembly.ManifestModule.ModuleVersionId.ToString();
		}
		catch (Exception)
		{
			mvid = Unknown;
		}

		AppendField(builder, "hosting.mvid", mvid);
		AppendField(builder, "hosting.alc", DescribeLoadContext());
	}

	private static string DescribeLoadContext()
	{
		try
		{
			AssemblyLoadContext? context = AssemblyLoadContext.GetLoadContext(typeof(PluginHost).Assembly);
			if (context is null)
			{
				return Unknown;
			}

			string kind = context.GetType().Name;
			string? name = context.Name;
			return string.IsNullOrEmpty(name) ? kind : kind + "<file:" + LastPathSegment(name) + ">";
		}
		catch (Exception)
		{
			return Unknown;
		}
	}

	private static string DescribeAssembly(Assembly assembly)
	{
		try
		{
			AssemblyName name = assembly.GetName();
			return (name.Name ?? Unknown) + " " + (name.Version?.ToString() ?? Unknown);
		}
		catch (Exception)
		{
			return Unknown;
		}
	}

	private static string DescribeExportsSize(ManagedExportedFunctions* exports)
	{
		return exports is null ? "0" : exports->SizeOfExportedFunctions.ToString(CultureInfo.InvariantCulture);
	}

	private static void AppendBridgeFields(StringBuilder builder)
	{
		ReadBridgeFacts(out string fingerprint, out string sha256);

		// The fingerprint is a fixed "<64 hex>:<64 hex>" shape (129 characters) validated by BridgeFingerprintPattern,
		// or the 11-character "unavailable" fallback: never free-form, so it is exempt from the 128-character
		// per-value bound that guards against unbounded native/plugin-supplied text elsewhere in this line.
		AppendField(builder, "bridge.fingerprint", fingerprint, boundValue: false);
		AppendField(builder, "bridge.sha256", sha256);
	}

	// Loads the bridge by its assembly-relative search path (the same one the Abi/Lua.Interop layer uses at
	// runtime), reads the fixed-size fingerprint export, hashes the file it was loaded from, and frees the handle.
	// Never keeps a reference: identification does not need the bridge to remain resolvable afterward.
	private static void ReadBridgeFacts(out string fingerprint, out string sha256)
	{
		fingerprint = Unavailable;
		sha256 = Unavailable;
		try
		{
			if (!NativeLibrary.TryLoad("cheatengine-sdk-lua-bridge", typeof(PluginHost).Assembly,
					DllImportSearchPath.AssemblyDirectory, out nint handle))
			{
				return;
			}

			try
			{
				if (NativeLibrary.TryGetExport(handle, "cheatengine_sdk_lua_bridge_source_fingerprint",
						out nint export))
				{
					string? text = ReadBoundedAnsiString((byte*) export, 160);
					if (text is not null && BridgeFingerprintPattern().IsMatch(text))
					{
						fingerprint = text;
					}
				}

				string? path = TryGetModuleFilePath(handle);
				if (path is not null && TryHashFile(path, out string hash))
				{
					sha256 = hash;
				}
			}
			finally
			{
				NativeLibrary.Free(handle);
			}
		}
		catch (Exception)
		{
			fingerprint = Unavailable;
			sha256 = Unavailable;
		}
	}

	private static void AppendLuaFields(StringBuilder builder)
	{
		string fileName = Unavailable;
		string sha256 = Unavailable;
		try
		{
			if (TryLocateLuaModule(out nint handle))
			{
				string? path = TryGetModuleFilePath(handle);
				if (path is not null)
				{
					string name = Path.GetFileName(path);
					if (name.Length > 0)
					{
						fileName = name;
					}

					if (TryHashFile(path, out string hash))
					{
						sha256 = hash;
					}
				}
			}
		}
		catch (Exception)
		{
			fileName = Unavailable;
			sha256 = Unavailable;
		}

		AppendField(builder, "lua.module", fileName);
		AppendField(builder, "lua.sha256", sha256);
	}

	// Uses the same test seam as the production bind (LuaModuleLocator.Resolver) so identification observes exactly
	// the module a test double stands in for. In production, when no seam is installed, looks up the module without
	// taking a loader reference: identification only reads facts, it never needs to keep the module alive.
	private static bool TryLocateLuaModule(out nint moduleHandle)
	{
		delegate*<nint> resolver = LuaModuleLocator.Resolver;
		if (resolver is not null)
		{
			moduleHandle = resolver();
			return moduleHandle != 0;
		}

		moduleHandle = 0;
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		const string moduleName = LuaModule.CheatEngine64ModuleName;
		fixed (char* name = moduleName)
		{
			nint handle = 0;
			if (GetModuleHandleExW(ModuleHandleUnchangedRefCountFlag, name, &handle) == 0)
			{
				return false;
			}

			moduleHandle = handle;
			return handle != 0;
		}
	}

	private static string? TryGetModuleFilePath(nint moduleHandle)
	{
		if (!OperatingSystem.IsWindows() || moduleHandle == 0)
		{
			return null;
		}

		Span<char> buffer = stackalloc char[1024];
		int written;
		fixed (char* p = buffer)
		{
			written = GetModuleFileNameW(moduleHandle, p, buffer.Length);
		}

		return written <= 0 || written >= buffer.Length ? null : new string(buffer[..written]);
	}

	private static bool TryHashFile(string path, out string sha256Hex)
	{
		sha256Hex = Unavailable;
		try
		{
			using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
				FileShare.ReadWrite | FileShare.Delete);
			if (stream.Length is < 0 or > MaxHashedFileBytes)
			{
				return false;
			}

			sha256Hex = Convert.ToHexStringLower(SHA256.HashData(stream));
			return true;
		}
		catch (Exception)
		{
			sha256Hex = Unavailable;
			return false;
		}
	}

	private static string? ReadBoundedAnsiString(byte* address, int maxBytes)
	{
		if (address is null)
		{
			return null;
		}

		try
		{
			int length = 0;
			while (length < maxBytes && address[length] != 0)
			{
				length++;
			}

			return Encoding.ASCII.GetString(address, length);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static void AppendCeFields(StringBuilder builder)
	{
		string file = Unavailable;
		string fileVersion = Unavailable;
		try
		{
			string? processPath = Environment.ProcessPath;
			if (!string.IsNullOrEmpty(processPath))
			{
				file = Path.GetFileName(processPath);
				FileVersionInfo info = FileVersionInfo.GetVersionInfo(processPath);
				fileVersion = info.FileVersion ?? Unavailable;
			}
		}
		catch (Exception)
		{
			file = Unavailable;
			fileVersion = Unavailable;
		}

		AppendField(builder, "ce.file", file);
		AppendField(builder, "ce.fileVersion", fileVersion);
	}

	private static string LastPathSegment(string value)
	{
		int separator = value.LastIndexOfAny(['\\', '/']);
		string tail = separator >= 0 ? value[(separator + 1)..] : value;
		return tail.Length == 0 ? Unknown : tail;
	}

	private static void AppendField(StringBuilder builder, string key, string value, bool last = false,
		bool boundValue = true)
	{
		builder.Append(key).Append('=').Append(Sanitize(value, boundValue));
		if (!last)
		{
			builder.Append("; ");
		}
	}

	private static string Sanitize(string value, bool boundValue)
	{
		string bounded = boundValue && value.Length > MaxValueLength ? value[..MaxValueLength] : value;
		if (bounded.IndexOfAny(['\r', '\n', ';']) < 0)
		{
			return bounded;
		}

		Span<char> scratch = bounded.Length <= 256 ? stackalloc char[bounded.Length] : new char[bounded.Length];
		for (int i = 0; i < bounded.Length; i++)
		{
			char c = bounded[i];
			scratch[i] = c is '\r' or '\n' ? ' ' : c is ';' ? ',' : c;
		}

		return new string(scratch);
	}

	// HMODULE GetModuleHandleExW(DWORD dwFlags, LPCWSTR lpModuleName, HMODULE* phModule); flag 0x4 does not take a
	// loader reference (GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT): identification only reads facts about a module
	// something else keeps alive.
	[LibraryImport("kernel32", EntryPoint = "GetModuleHandleExW")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[SupportedOSPlatform("windows")]
	private static partial int GetModuleHandleExW(uint dwFlags, char* lpModuleName, nint* phModule);

	// DWORD GetModuleFileNameW(HMODULE hModule, LPWSTR lpFilename, DWORD nSize); returns 0 on failure, or a length
	// equal to nSize when the buffer was too small (the result is then not NUL-terminated and must be rejected).
	[LibraryImport("kernel32", EntryPoint = "GetModuleFileNameW")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[SupportedOSPlatform("windows")]
	private static partial int GetModuleFileNameW(nint hModule, char* lpFilename, int nSize);
}
