using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Registration;

namespace CheatEngine.SDK.AotProbe;

internal static class Program
{
	public static void Main()
	{
		Console.WriteLine($"CheatEngine.SDK Native AOT publication probe: {ProbeRepresentativePaths()}, {typeof(PluginHost).Assembly.GetName().Name}");
	}

	[DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(LuaCallback<ProbeState>))]
	private static string ProbeRepresentativePaths()
	{
		if (!Address.TryParse("140001000", out Address address) || address.ToUInt64() != 0x140001000)
		{
			throw new InvalidOperationException("Address probe failed.");
		}

		LuaStatus status = KeepGenericPath(LuaStatus.Ok);
		_ = typeof(LuaCallback<ProbeState>);
		LuaRegistrationCollisionPolicy collisionPolicy = KeepGenericPath(LuaRegistrationCollisionPolicy.RejectExisting);
		_ = typeof(LuaRegistrationSet);
		_ = typeof(LuaRegistrationLease);
		if (!InstructionProfile.X64.IsValid || InstructionProfile.X86.AddressWidth != PointerSize.Bit32)
		{
			throw new InvalidOperationException("Instruction profile probe failed.");
		}

		_ = default(InstructionDisassembly);
		_ = default(InstructionTargetProfile);
		_ = typeof(AddressListMutations);
		_ = typeof(MemoryRecordMutationOutcome);
		_ = typeof(SymbolRegistrationLease);
		_ = typeof(SymbolRegistrationReleaseOutcome);
		return $"{typeof(Address).Assembly.GetName().Name}, {status}, {collisionPolicy}, {InstructionOperationStatus.Success}";
	}

	private static T KeepGenericPath<T>(T value) where T : struct => value;

	private sealed class ProbeState;
}
