using System.Globalization;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Hosting.Tests.Diagnostics;

/// <summary>
///     The opt-in, bounded <c>CheatEngineSdkIdentification</c> diagnostic (A21-34, A24-11, A24-19, A20-Q46-3): silent
///     unless requested, emitted at most once per enable attempt before the exports record is copied, path-free and
///     built without touching Lua or plugin code.
/// </summary>
public sealed unsafe partial class LoadIdentificationTests
{
	private const string Prefix = "CheatEngineSdkIdentification: ";

	private static readonly string[] SExpectedKeys =
	[
		"sdk.version", "sdk.commit", "sdk.consistent", "hosting.mvid", "hosting.alc", "plugin.id", "plugin.assembly",
		"host.argument", "exports.size", "bridge.fingerprint", "bridge.sha256", "lua.module", "lua.sha256", "ce.file",
		"ce.fileVersion", "runtime", "arch"
	];

	[GeneratedRegex(@"lua\.module=(?<module>[^;]*); lua\.sha256=(?<sha>[0-9a-f]{64}|unavailable)",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 1000)]
	private static partial Regex LuaModuleFieldsPattern();

	[GeneratedRegex("^[A-Za-z]:", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex DriveRootPattern();

	[Fact]
	public void Identification_is_not_emitted_unless_opted_in()
	{
		CapturingLogSink sink = HostingTest.Reset();
		using HostSimulator host = new();
		HostingTest.Bootstrap(host);
		ManagedExportedFunctions exports = FakeExports.Create();

		host.CallEnable(&exports, 1);

		Assert.Equal(0, CountIdentificationEntries(sink));
	}

	[Fact]
	public void Identification_is_emitted_once_per_enable_attempt_when_opted_in_programmatically()
	{
		CapturingLogSink sink = HostingTest.Reset();
		HostLog.IdentifyOnEnable = true;
		using HostSimulator host = new();
		HostingTest.Bootstrap(host);
		ManagedExportedFunctions exports = FakeExports.Create();

		host.CallEnable(&exports, 1);

		Assert.Equal(1, CountIdentificationEntries(sink));
	}

	[Fact]
	public void Identification_is_emitted_when_the_environment_opt_in_is_set()
	{
		CapturingLogSink sink = HostingTest.Reset();
		HostLog.IdentifyOnEnableEnvironmentReader = static () => "1";
		using HostSimulator host = new();
		HostingTest.Bootstrap(host);
		ManagedExportedFunctions exports = FakeExports.Create();

		host.CallEnable(&exports, 1);

		// The seam opted in; the real process environment was never touched.
		Assert.False(HostLog.IdentifyOnEnable);
		Assert.Null(Environment.GetEnvironmentVariable(HostLog.IdentifyOnEnableEnvironmentVariable));
		Assert.Equal(1, CountIdentificationEntries(sink));
	}

	[Trait("Qualification", "Q46")]
	[Fact]
	public void Identification_contains_the_sdk_version_and_a_40_hex_commit_or_unknown()
	{
		string line = EnableWithIdentification();

		Assert.Matches(
			@"sdk\.version=[^;]*; sdk\.commit=(?:[0-9a-f]{40}|unknown); sdk\.consistent=(?:true|false)(?:; |$)",
			line);
	}

	[Trait("Qualification", "Q46")]
	[Fact]
	public void Identification_contains_the_bridge_fingerprint_or_unavailable()
	{
		string line = EnableWithIdentification();

		Assert.Matches(@"bridge\.fingerprint=(?:unavailable|[0-9a-f]{64}:[0-9a-f]{64})(?:; |$)", line);
	}

	[Trait("Qualification", "Q46")]
	[Trait("Category", "NativeLua")]
	[Fact]
	public void Identification_contains_the_lua_module_file_name_and_sha256_without_a_directory()
	{
		HostingTest.RequireNativeLua();
		string line = EnableWithIdentification(true);

		Match match = LuaModuleFieldsPattern().Match(line);
		Assert.True(match.Success, "lua.module/lua.sha256 not found in: " + line);
		string moduleName = match.Groups["module"].Value;
		Assert.DoesNotContain('\\', moduleName);
		Assert.DoesNotContain('/', moduleName);
		Assert.Equal(Path.GetFileName(NativeLuaLibrary.LibraryPath!), moduleName);
		Assert.Matches("^[0-9a-f]{64}$", match.Groups["sha"].Value);
	}

	[Trait("Qualification", "Q46")]
	[Trait("Category", "NativeLua")]
	[Fact]
	public void Identification_never_contains_a_directory_separator_drive_root_or_user_name()
	{
		HostingTest.RequireNativeLua();
		string line = EnableWithIdentification(true);

		foreach ((string _, string value) in ParsePairs(line))
		{
			Assert.DoesNotContain('\\', value);
			Assert.DoesNotContain('/', value);
			Assert.False(DriveRootPattern().IsMatch(value), "drive root in value: " + value);
			if (!string.IsNullOrEmpty(Environment.UserName))
			{
				Assert.DoesNotContain(Environment.UserName, value, StringComparison.OrdinalIgnoreCase);
			}
		}
	}

	[Trait("Qualification", "Q46")]
	[Fact]
	public void Identification_is_bounded_and_keeps_a_fixed_key_order()
	{
		string line = EnableWithIdentification();

		Assert.True(line.Length <= 1024, "entry exceeds 1024 characters: " + line.Length);

		string[] actualKeys = ExtractKeys(line);
		Assert.Equal(SExpectedKeys, actualKeys);

		foreach ((string key, string value) in ParsePairs(line))
		{
			// bridge.fingerprint is the single fixed-shape "<64 hex>:<64 hex>" exception (129 characters), validated
			// by its own pattern instead of the general per-value bound.
			if (string.Equals(key, "bridge.fingerprint", StringComparison.Ordinal))
			{
				Assert.Matches("^(?:unavailable|[0-9a-f]{64}:[0-9a-f]{64})$", value);
				continue;
			}

			Assert.True(value.Length <= 128, "value exceeds 128 characters: " + value);
		}
	}

	[Fact]
	public void Identification_calls_no_Lua_and_no_plugin_code()
	{
		HostingTest.Reset();
		HostLog.IdentifyOnEnable = true;
		ManagedExportedFunctions exports = FakeExports.Create();

		LoadIdentification.EmitIfRequested(&exports, 1, typeof(RecordingPluginFactory));

		Assert.Equal(0, FakeExports.GetLuaStateCalls);
		Assert.Equal(0, RecordingPlugin.ConstructorCalls);
		Assert.Null(RecordingPlugin.LastConstructed);
	}

	[Fact]
	public void Identification_is_emitted_even_when_the_Lua_bind_fails()
	{
		CapturingLogSink sink = HostingTest.Reset();
		HostLog.IdentifyOnEnable = true;
		HostingTest.UseNoModule();
		using HostSimulator host = new();
		HostingTest.Bootstrap(host);
		ManagedExportedFunctions exports = FakeExports.Create();

		Bool32 result = host.CallEnable(&exports, 1);

		Assert.False(result.IsTrue);
		Assert.Equal(1, CountIdentificationEntries(sink));
	}

	[Theory]
	[InlineData(int.MinValue)]
	[InlineData(-1)]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(36)]
	[InlineData(int.MaxValue)]
	public void Identification_records_the_raw_host_argument_without_interpreting_it(int hostArgument)
	{
		CapturingLogSink sink = HostingTest.Reset();
		HostLog.IdentifyOnEnable = true;
		using HostSimulator host = new();
		Assert.Equal(1, host.Initialize<RecordingPluginFactory>(hostArgument));
		ManagedExportedFunctions exports = FakeExports.Create();

		host.CallEnable(&exports, 1);

		string line = RequireIdentificationLine(sink);
		string expected = "host.argument=" + hostArgument.ToString(CultureInfo.InvariantCulture);
		Assert.Contains(expected, line, StringComparison.Ordinal);
	}

	private static string EnableWithIdentification(bool useNativeLuaModule = false)
	{
		CapturingLogSink sink = HostingTest.Reset();
		HostLog.IdentifyOnEnable = true;
		if (useNativeLuaModule)
		{
			LuaModuleLocator.Resolver = &ResolveNativeLuaModule;
		}

		using HostSimulator host = new();
		HostingTest.Bootstrap(host);
		ManagedExportedFunctions exports = FakeExports.Create();
		host.CallEnable(&exports, 1);
		return RequireIdentificationLine(sink);
	}

	private static nint ResolveNativeLuaModule()
	{
		return NativeLuaLibrary.Handle;
	}

	private static string RequireIdentificationLine(CapturingLogSink sink)
	{
		foreach ((HostLogLevel level, string message, Exception? _) in sink.Entries)
		{
			if (level == HostLogLevel.Information && message.StartsWith(Prefix, StringComparison.Ordinal))
			{
				return message;
			}
		}

		Assert.Fail("No CheatEngineSdkIdentification entry was captured.");
		return string.Empty; // unreachable
	}

	private static int CountIdentificationEntries(CapturingLogSink sink)
	{
		int count = 0;
		foreach ((HostLogLevel level, string message, Exception? _) in sink.Entries)
		{
			if (level == HostLogLevel.Information && message.StartsWith(Prefix, StringComparison.Ordinal))
			{
				count++;
			}
		}

		return count;
	}

	private static string[] ExtractKeys(string line)
	{
		(string Key, string Value)[] pairs = ParsePairs(line);
		string[] keys = new string[pairs.Length];
		for (int i = 0; i < pairs.Length; i++)
		{
			keys[i] = pairs[i].Key;
		}

		return keys;
	}

	private static (string Key, string Value)[] ParsePairs(string line)
	{
		string body = line[Prefix.Length..];
		string[] segments = body.Split("; ");
		(string, string)[] result = new (string, string)[segments.Length];
		for (int i = 0; i < segments.Length; i++)
		{
			int equals = segments[i].IndexOf('=');
			result[i] = equals >= 0
				? (segments[i][..equals], segments[i][(equals + 1)..])
				: (segments[i], string.Empty);
		}

		return result;
	}
}
