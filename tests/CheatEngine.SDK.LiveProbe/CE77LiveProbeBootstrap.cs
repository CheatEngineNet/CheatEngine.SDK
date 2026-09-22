using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Diagnostics;

using LiveProbe;

namespace CESDK;

/// <summary>
///     The intentionally hand-written CE managed bootstrap used only by the CE 7.7 live-probe plugin.
/// </summary>
/// <remarks>
///     It captures the second argument exactly as Cheat Engine supplied it. It remains an opaque integer: this assembly
///     does not call it a record size, ABI version, or anything else. The normal SDK bootstrap remains the source of
///     production behaviour.
/// </remarks>
#pragma warning disable MA0048 // The file identifies the CE 7.7 probe; the host fixes the public bootstrap type name.
#pragma warning disable MA0049 // CE's host requires CESDK.CESDK exactly.
public static unsafe class CESDK
{
	/// <summary>
	///     CE's fixed managed component entry point. The second argument is recorded raw and deliberately has no
	///     inferred semantic.
	/// </summary>
	/// <param name="initRecord">Host-owned bootstrap storage.</param>
	/// <param name="opaqueHostArgument">The unmodified second integer provided by CE.</param>
	/// <returns>One only when the production bootstrap wrote its packed record.</returns>
	public static int CEPluginInitialize(nint initRecord, int opaqueHostArgument)
	{
		try
		{
			LiveProbeState.CaptureBootstrap(initRecord, opaqueHostArgument);
			int result = PluginHost.InitializeManaged<LiveProbe.ProbePluginFactory>(initRecord, opaqueHostArgument);
			LiveProbeState.TryWriteTailCanaryAfterPackedRecord(initRecord, result);
			return result;
		}
		catch (Exception exception)
		{
			// Do not let even the probe's diagnostics escape through hostfxr's component entry point.
			HostLog.Write(HostLogLevel.Error, "CE 7.7 live-probe bootstrap failed.", exception);
			return ManagedEntryPoint.Failure;
		}
	}
}
#pragma warning restore MA0049
#pragma warning restore MA0048
