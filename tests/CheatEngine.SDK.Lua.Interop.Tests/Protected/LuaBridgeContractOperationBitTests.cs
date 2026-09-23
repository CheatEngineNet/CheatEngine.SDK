using System.Runtime.CompilerServices;

using CheatEngine.SDK.Lua.Interop.Protected;

namespace CheatEngine.SDK.Lua.Interop.Tests.Protected;

/// <summary>
///     Q11 (audit A05-21): a bridge whose supported-operation bitmap lacks any single operation the generated
///     <c>LuaProtectedOperation</c> enum requires is incompatible, whatever else it offers. DLL-free: the contract is
///     built in memory.
/// </summary>
public sealed class LuaBridgeContractOperationBitTests
{
	/// <summary>Every value of the generated operation enum, as its opcode.</summary>
	public static TheoryData<int> RequiredOperations
	{
		get
		{
			TheoryData<int> data = [];
			foreach (LuaProtectedOperation operation in Enum.GetValues<LuaProtectedOperation>())
			{
				data.Add((int) operation);
			}

			return data;
		}
	}

	[Theory]
	[MemberData(nameof(RequiredOperations))]
	[Trait("Qualification", "Q11")]
	public void Contract_missing_any_single_required_operation_bit_is_incompatible(int opcode)
	{
		LuaBridgeContract contract = CompatibleContract();
		Assert.True(contract.IsCompatible());

		contract.SupportedOperations &= ~(1UL << opcode);
		Assert.False(contract.IsCompatible());

		// Extra bits never compensate for a missing required one.
		contract.SupportedOperations |= ~LuaProtectedOperationContract.RequiredBitmap;
		Assert.False(contract.IsCompatible());
	}

	[Fact]
	public void The_theory_covers_exactly_the_required_bitmap()
	{
		ulong bitmap = 0;
		foreach (LuaProtectedOperation operation in Enum.GetValues<LuaProtectedOperation>())
		{
			bitmap |= 1UL << (int) operation;
		}

		Assert.Equal(LuaProtectedOperationContract.RequiredBitmap, bitmap);
		Assert.Equal(LuaProtectedOperationContract.Count, Enum.GetValues<LuaProtectedOperation>().Length);
	}

	private static LuaBridgeContract CompatibleContract()
	{
		return new LuaBridgeContract
		{
			Magic = LuaBridgeContract.ExpectedMagic,
			ContractSize = (uint) Unsafe.SizeOf<LuaBridgeContract>(),
			SupportedOperations = LuaProtectedOperationContract.RequiredBitmap,
			ExportTableSize = (uint) Unsafe.SizeOf<LuaProtectedExports>(),
			AbiMajor = LuaBridgeContract.ExpectedMajor,
			AbiMinor = LuaBridgeContract.MinimumMinor,
			PointerSize = (byte) IntPtr.Size,
			LuaIntegerSize = sizeof(long),
			SizeTSize = (byte) Unsafe.SizeOf<nuint>()
		};
	}
}
