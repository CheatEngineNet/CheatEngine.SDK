using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>Disassembles one profile-qualified target address and parses CE's display line inside the SDK.</summary>
/// <remarks>
///     <para>
///         The pinned CE 7.7.0.10621 <c>LuaHandler.pas</c> implementation registers <c>disassemble</c> and
///         <c>splitDisassembledString</c>. The first accepts one target address and returns one display string; the
///         second converts that string into address, bytes, opcode, and extra UTF-8 fields. This type deliberately
///         keeps those two Lua calls together so an SDK consumer does not parse UI-shaped text to repair a native
///         contract.
///     </para>
///     <para>
///         The text is bounded before decoding: a raw line longer than <c>maximumUtf8Bytes</c> is not
///         published, and the four raw split fields are measured collectively against the same limit before any is
///         decoded. The raw line is copied before the second Lua global is resolved, so a returned value never
///         depends on Lua-owned storage after another Lua operation. No native classic disassembler slot is invoked
///         or projected by this API, because its historical ABI evidence is not sufficient for a callable CE-host
///         contract.
///     </para>
/// </remarks>
public static class InstructionDisassembler
{
	private static readonly LuaRef SDisassemble = new();
	private static readonly LuaRef SSplitDisassembledString = new();

	/// <summary>Disassembles and parses one target instruction with an explicit bound for CE's raw UTF-8 line.</summary>
	/// <param name="targetProfile">The CE-observed selected PID and instruction profile used to validate the address.</param>
	/// <param name="address">The target address supplied to CE's <c>disassemble</c> global.</param>
	/// <param name="maximumUtf8Bytes">The largest raw display-line byte length accepted before any managed text is decoded.</param>
	/// <param name="instruction">
	///     The copied parsed result only when the returned status is
	///     <see cref="InstructionOperationStatus.Success" />.
	/// </param>
	/// <param name="requiredUtf8Bytes">The raw display-line UTF-8 byte length when CE supplied a string; otherwise zero.</param>
	/// <returns>A profile, capacity, availability, protected-call, target, or result-shape outcome.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumUtf8Bytes" /> is negative.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification = "Both protected Lua calls and all stack-borrowed text remain in one lifetime frame.")]
	public static InstructionOperationStatus TryDisassemble(InstructionTargetProfile targetProfile, Address address,
		int maximumUtf8Bytes, out InstructionDisassembly instruction, out int requiredUtf8Bytes)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(maximumUtf8Bytes);
		instruction = default;
		requiredUtf8Bytes = 0;

		InstructionOperationStatus profileStatus = targetProfile.Validate(address);
		if (profileStatus != InstructionOperationStatus.Success)
		{
			return profileStatus;
		}

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			InstructionOperationStatus targetStatus = InstructionProfiles.TryVerifyCurrent(state, targetProfile.Target);
			if (targetStatus != InstructionOperationStatus.Success)
			{
				return targetStatus;
			}

			InstructionOperationStatus status = PushGlobal(state, SDisassemble, "disassemble"u8);
			if (status != InstructionOperationStatus.Success)
			{
				return status;
			}

			Address.Push(state, address);
			if (!state.TryCall(1, 1).IsOk)
			{
				return InstructionOperationStatus.LuaFailure;
			}

			if (!state.TryReadUtf8(-1, out ReadOnlySpan<byte> line))
			{
				return InstructionOperationStatus.InvalidResult;
			}

			requiredUtf8Bytes = line.Length;
			if (line.Length > maximumUtf8Bytes)
			{
				return InstructionOperationStatus.OutputTooLong;
			}

			byte[] lineCopy = new byte[line.Length];
			line.CopyTo(lineCopy);

			targetStatus = InstructionProfiles.TryVerifyCurrent(state, targetProfile.Target);
			if (targetStatus != InstructionOperationStatus.Success)
			{
				requiredUtf8Bytes = 0;
				return targetStatus;
			}

			status = PushGlobal(state, SSplitDisassembledString, "splitDisassembledString"u8);
			if (status != InstructionOperationStatus.Success)
			{
				return status;
			}

			state.PushString(lineCopy);
			if (!state.TryCall(1, 4).IsOk)
			{
				return InstructionOperationStatus.LuaFailure;
			}

			if (!state.TryReadUtf8(-4, out ReadOnlySpan<byte> addressUtf8) ||
			    !state.TryReadUtf8(-3, out ReadOnlySpan<byte> bytesUtf8) ||
			    !state.TryReadUtf8(-2, out ReadOnlySpan<byte> opcodeUtf8) ||
			    !state.TryReadUtf8(-1, out ReadOnlySpan<byte> extraUtf8))
			{
				return InstructionOperationStatus.InvalidResult;
			}

			int copiedLength = checked(addressUtf8.Length + bytesUtf8.Length + opcodeUtf8.Length + extraUtf8.Length);
			if (copiedLength > maximumUtf8Bytes)
			{
				return InstructionOperationStatus.OutputTooLong;
			}

			targetStatus = InstructionProfiles.TryVerifyCurrent(state, targetProfile.Target);
			if (targetStatus != InstructionOperationStatus.Success)
			{
				requiredUtf8Bytes = 0;
				return targetStatus;
			}

			instruction = new InstructionDisassembly(address, Encoding.UTF8.GetString(addressUtf8),
				Encoding.UTF8.GetString(bytesUtf8), Encoding.UTF8.GetString(opcodeUtf8),
				Encoding.UTF8.GetString(extraUtf8),
				copiedLength);
			return InstructionOperationStatus.Success;
		}
		catch (LuaException)
		{
			instruction = default;
			requiredUtf8Bytes = 0;
			return InstructionOperationStatus.LuaFailure;
		}
		catch (OverflowException)
		{
			instruction = default;
			requiredUtf8Bytes = 0;
			return InstructionOperationStatus.InvalidResult;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	private static InstructionOperationStatus PushGlobal(LuaState state, LuaRef cache, ReadOnlySpan<byte> name)
	{
		return LuaGlobalFunctions.TryPushWithStatus(state, cache, name) switch
		{
			LuaGlobalPushStatus.Success => InstructionOperationStatus.Success,
			LuaGlobalPushStatus.Unavailable => InstructionOperationStatus.GlobalUnavailable,
			_ => InstructionOperationStatus.LuaFailure
		};
	}
}
