using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.NativeAotLibraryProbe;

/// <summary>Inert functions whose names make the generated shared library inspectable by the bounded harness.</summary>
public static class NativeAotLibraryProbeExports
{
	/// <summary>Returns a fixture sentinel if a native inspection tool ever needs an invocation contract.</summary>
	[UnmanagedCallersOnly(EntryPoint = NativeAotLibraryProbeExportNames.NameQuery,
		CallConvs = [typeof(CallConvStdcall)])]
	public static int NameQuery()
	{
		return 1;
	}

	/// <summary>Returns a fixture sentinel and does not create, enable, or disable a plugin.</summary>
	[UnmanagedCallersOnly(EntryPoint = NativeAotLibraryProbeExportNames.LoadOnly,
		CallConvs = [typeof(CallConvStdcall)])]
	public static int LoadOnly()
	{
		return 1;
	}
}
