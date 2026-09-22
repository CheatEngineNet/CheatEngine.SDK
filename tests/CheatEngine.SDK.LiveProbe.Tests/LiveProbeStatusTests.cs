using System.Text.Json;

using LiveProbe;

namespace CheatEngine.SDK.LiveProbe.Tests;

/// <summary>
///     The status records the qualification driver reads (<c>ce77_live_probe_status()</c> and
///     <c>ce77_live_probe_status_json()</c>) and the two Checkpoint-B command hooks, with injected host facts and gates.
/// </summary>
public sealed class LiveProbeStatusTests
{
	[Fact]
	public void Status_reports_the_exports_size_and_the_raw_second_bootstrap_integer_without_interpretation()
	{
		const int RawSecondInteger = -1_234_567;
		LiveProbeState.CaptureBootstrap(0x1000, RawSecondInteger);
		LiveProbeHostFacts host = Facts(RawSecondInteger, 48, 7, 3);

		string text = LiveProbeState.GetStatus(host);
		using JsonDocument json = JsonDocument.Parse(LiveProbeState.GetStatusJson(host));

		Assert.Contains("opaqueSecondInt=-1234567 (raw; no size/version meaning assigned)", text,
			StringComparison.Ordinal);
		Assert.Contains("pluginHostLastInitRecordArgument=-1234567", text, StringComparison.Ordinal);
		Assert.Contains("pluginId=7, epoch=3, reportedExportsSize=48", text, StringComparison.Ordinal);

		JsonElement root = json.RootElement;
		Assert.Equal(LiveProbeStatusReport.Schema, root.GetProperty("schema").GetString());
		JsonElement bootstrap = root.GetProperty("bootstrap");
		Assert.Equal(RawSecondInteger, bootstrap.GetProperty("opaqueSecondInt").GetInt32());
		Assert.Equal(RawSecondInteger, bootstrap.GetProperty("pluginHostLastInitRecordArgument").GetInt32());
		Assert.Equal(LiveProbeStatusReport.NoInterpretation, bootstrap.GetProperty("interpretation").GetString());
		Assert.True(bootstrap.GetProperty("calls").GetInt32() >= 1);
		JsonElement context = root.GetProperty("context");
		Assert.True(context.GetProperty("present").GetBoolean());
		Assert.Equal(48, context.GetProperty("reportedExportsSize").GetInt32());
		Assert.Equal(7u, context.GetProperty("pluginId").GetUInt32());
		Assert.Equal(3, context.GetProperty("epoch").GetInt32());
		Assert.Equal("Enabled", context.GetProperty("phase").GetString());

		// The bootstrap record never names the raw integer a size, a length or a version.
		foreach (JsonProperty property in bootstrap.EnumerateObject())
		{
			Assert.DoesNotContain("size", property.Name, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("length", property.Name, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("version", property.Name, StringComparison.OrdinalIgnoreCase);
		}

		Assert.Equal(16, root.GetProperty("versionQuery").GetProperty("lastRecordSize").GetInt32());
	}

	[Fact]
	public void Status_without_an_enabled_context_reports_none_instead_of_zero_values()
	{
		LiveProbeHostFacts host = Facts(0, 0, 0, 0) with
		{
			HasContext = false,
			Phase = "Registered"
		};

		string text = LiveProbeState.GetStatus(host);
		using JsonDocument json = JsonDocument.Parse(LiveProbeState.GetStatusJson(host));

		Assert.Contains("context=none", text, StringComparison.Ordinal);
		JsonElement context = json.RootElement.GetProperty("context");
		Assert.False(context.GetProperty("present").GetBoolean());
		Assert.False(context.TryGetProperty("reportedExportsSize", out _));
		Assert.False(context.TryGetProperty("pluginId", out _));
		Assert.Equal("Registered", context.GetProperty("phase").GetString());
	}

	[Fact]
	public void Status_json_reports_the_fault_switch_decision_and_the_assembly_identities()
	{
		LiveProbeHostFacts host = Facts(0, 48, 1, 1);

		using JsonDocument json = JsonDocument.Parse(LiveProbeState.GetStatusJson(host));

		JsonElement fault = json.RootElement.GetProperty("faultInjection");
		Assert.Equal(LiveProbeFaultInjection.Current.Stage.ToString(), fault.GetProperty("stage").GetString());
		Assert.Equal(JsonValueKind.Array, fault.GetProperty("injected").ValueKind);
		JsonElement identity = json.RootElement.GetProperty("identity");
		Assert.Equal("plugin.dll", identity.GetProperty("pluginAssemblyLocation").GetString());
		Assert.Equal("hosting.dll", identity.GetProperty("hostingAssemblyLocation").GetString());
		Assert.Equal("Default (collectible=False)", identity.GetProperty("hostingLoadContext").GetString());
	}

	[Fact]
	public void Captured_host_facts_without_a_plugin_host_report_no_context()
	{
		LiveProbeHostFacts host = LiveProbeHostFacts.Capture();

		Assert.False(host.HasContext);
		Assert.False(string.IsNullOrEmpty(host.PluginAssemblyMvid));
		Assert.False(string.IsNullOrEmpty(host.HostingAssemblyMvid));
	}

	[Fact]
	public void Throw_hook_returns_the_denial_and_throws_nothing_without_authorization()
	{
		string result = LiveProbeState.ThrowManagedExceptionIfAuthorized(
			static () => AuthorizationDecision.Denied("CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE is absent."),
			static () => throw new InvalidOperationException("A denied authorization must not read CE's PID."));

		Assert.Equal("Live probe denied: CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE is absent.", result);
	}

	[Fact]
	public void Throw_hook_throws_the_marked_exception_when_authorized_for_the_opened_target()
	{
		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
			LiveProbeState.ThrowManagedExceptionIfAuthorized(Allowed, static () => 401));

		Assert.Equal(LiveProbeState.ManagedExceptionMarker, exception.Message);
		using JsonDocument json = JsonDocument.Parse(LiveProbeState.GetStatusJson(Facts(0, 48, 1, 1)));
		Assert.True(json.RootElement.GetProperty("managedExceptionThrows").GetInt32() >= 1);
	}

	[Theory]
	[InlineData(0.5)]
	[InlineData(61)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	public void Pump_hook_refuses_a_duration_outside_one_to_sixty_seconds_before_any_host_call(double seconds)
	{
		bool pumped = false;

		string result = LiveProbeState.PumpMessages(
			static () => throw new InvalidOperationException("An invalid duration must not evaluate authorization."),
			static () => throw new InvalidOperationException("An invalid duration must not read CE's PID."),
			seconds,
			_ =>
			{
				pumped = true;
				return "pumped";
			});

		Assert.False(pumped);
		Assert.StartsWith("Pump refused:", result, StringComparison.Ordinal);
	}

	[Fact]
	public void Pump_hook_is_inert_without_authorization_and_pumps_only_for_the_opened_target()
	{
		double pumpedFor = 0;

		string denied = LiveProbeState.PumpMessages(static () => AuthorizationDecision.Denied("No manifest."),
			static () => 401, 5, seconds =>
			{
				pumpedFor = seconds;
				return "pumped";
			});
		string wrongTarget = LiveProbeState.PumpMessages(Allowed, static () => 402, 5, seconds =>
		{
			pumpedFor = seconds;
			return "pumped";
		});
		string allowed = LiveProbeState.PumpMessages(Allowed, static () => 401, 5, seconds =>
		{
			pumpedFor = seconds;
			return "pumped";
		});

		Assert.Equal("Live probe denied: No manifest.", denied);
		Assert.Equal("Live probe denied: CE reports opened process 402, not manifest process 401.", wrongTarget);
		Assert.Equal("pumped", allowed);
		Assert.Equal(5, pumpedFor);
	}

	private static LiveProbeHostFacts Facts(int lastInitRecordArgument, int reportedExportsSize, uint pluginId,
		int epoch)
	{
		return new LiveProbeHostFacts(true, pluginId, epoch, reportedExportsSize, true, true, "Enabled",
			lastInitRecordArgument, 16, "plugin.dll", "00000000-0000-0000-0000-000000000001", "hosting.dll",
			"00000000-0000-0000-0000-000000000002", "Default (collectible=False)");
	}

	private static AuthorizationDecision Allowed()
	{
		return AuthorizationDecision.Allowed("C:\\ce.exe", "HOST", 401, "C:\\disposable-target.exe", "TARGET",
			DateTimeOffset.MaxValue);
	}
}
