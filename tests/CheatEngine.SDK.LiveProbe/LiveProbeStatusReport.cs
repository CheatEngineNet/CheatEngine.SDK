using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiveProbe;

/// <summary>
///     Serializes the status record as one JSON object (schema <see cref="Schema" />). The qualification driver stores the
///     text verbatim in its event log; the runner parses it. Nothing here interprets the second bootstrap integer.
/// </summary>
internal static class LiveProbeStatusReport
{
	internal const string Schema = "ce77-live-probe-status-v1";

	/// <summary>The value of <c>bootstrap.interpretation</c>: the raw integer is never given a meaning here.</summary>
	internal const string NoInterpretation = "none";

	internal static string ToJson(in LiveProbeStatusSnapshot snapshot)
	{
		using MemoryStream stream = new();
		using (Utf8JsonWriter writer = new(stream))
		{
			writer.WriteStartObject();
			writer.WriteString("schema", Schema);
			WriteBootstrap(writer, snapshot);

			writer.WriteStartObject("versionQuery");
			writer.WriteNumber("lastRecordSize", snapshot.Host.LastVersionRecordSize);
			writer.WriteEndObject();

			WriteContext(writer, snapshot.Host);
			WriteIdentity(writer, snapshot.Host);
			WriteGates(writer, snapshot);
			WriteFaultInjection(writer, snapshot);
			writer.WriteNumber("managedExceptionThrows", snapshot.ManagedExceptionThrows);

			writer.WriteStartObject("observations");
			writer.WriteString("synchronize", snapshot.Synchronize);
			writer.WriteString("luaThreads", snapshot.LuaThreads);
			writer.WriteString("reset", snapshot.Reset);
			writer.WriteString("callback", snapshot.Callback);
			writer.WriteEndObject();

			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static void WriteBootstrap(Utf8JsonWriter writer, in LiveProbeStatusSnapshot snapshot)
	{
		writer.WriteStartObject("bootstrap");
		writer.WriteNumber("calls", snapshot.BootstrapCalls);
		writer.WriteNumber("opaqueSecondInt", snapshot.OpaqueSecondInt);
		writer.WriteNumber("pluginHostLastInitRecordArgument", snapshot.Host.LastInitRecordArgument);
		writer.WriteString("interpretation", NoInterpretation);
		writer.WriteBoolean("tailCanaryWritten", snapshot.TailCanaryWritten);
		writer.WriteNumber("tailWrites", snapshot.TailWrites);
		if (snapshot.TailCanaryWritten)
		{
			writer.WriteString("tailBeforeHex", snapshot.TailReadBeforeWrite.ToString("X8", CultureInfo.InvariantCulture));
		}
		else
		{
			writer.WriteNull("tailBeforeHex");
		}

		if (snapshot.TailFailure is null)
		{
			writer.WriteNull("tailFailure");
		}
		else
		{
			writer.WriteString("tailFailure", snapshot.TailFailure);
		}

		writer.WriteEndObject();
	}

	private static void WriteContext(Utf8JsonWriter writer, in LiveProbeHostFacts host)
	{
		writer.WriteStartObject("context");
		writer.WriteBoolean("present", host.HasContext);
		if (host.HasContext)
		{
			writer.WriteNumber("pluginId", host.PluginId);
			writer.WriteNumber("epoch", host.Epoch);
			writer.WriteNumber("reportedExportsSize", host.ReportedExportsSize);
			writer.WriteBoolean("hasProcessMessages", host.HasProcessMessages);
			writer.WriteBoolean("hasCheckSynchronize", host.HasCheckSynchronize);
		}

		writer.WriteString("phase", host.Phase);
		writer.WriteEndObject();
	}

	private static void WriteIdentity(Utf8JsonWriter writer, in LiveProbeHostFacts host)
	{
		writer.WriteStartObject("identity");
		writer.WriteString("pluginAssemblyLocation", host.PluginAssemblyLocation);
		writer.WriteString("pluginAssemblyMvid", host.PluginAssemblyMvid);
		writer.WriteString("hostingAssemblyLocation", host.HostingAssemblyLocation);
		writer.WriteString("hostingAssemblyMvid", host.HostingAssemblyMvid);
		writer.WriteString("hostingLoadContext", host.HostingLoadContext);
		writer.WriteEndObject();
	}

	private static void WriteGates(Utf8JsonWriter writer, in LiveProbeStatusSnapshot snapshot)
	{
		writer.WriteStartObject("gates");
		writer.WriteBoolean("bootstrapAllowed", snapshot.BootstrapGateAllowed);
		writer.WriteString("bootstrapReason", snapshot.BootstrapGateReason);
		writer.WriteBoolean("runtimeAllowed", snapshot.RuntimeGateAllowed);
		writer.WriteString("runtimeReason", snapshot.RuntimeGateReason);
		writer.WriteEndObject();
	}

	private static void WriteFaultInjection(Utf8JsonWriter writer, in LiveProbeStatusSnapshot snapshot)
	{
		writer.WriteStartObject("faultInjection");
		writer.WriteString("stage", snapshot.Fault.Stage.ToString());
		writer.WriteString("reason", snapshot.Fault.Reason);
		writer.WriteBoolean("fileFound", snapshot.Fault.FileFound);
		writer.WriteStartArray("injected");
		foreach (string injected in snapshot.InjectedFaults)
		{
			writer.WriteStringValue(injected);
		}

		writer.WriteEndArray();
		writer.WriteEndObject();
	}
}
