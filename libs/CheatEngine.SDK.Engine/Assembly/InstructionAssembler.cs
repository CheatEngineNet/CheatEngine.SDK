using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>Assembles one instruction through CE's protected Lua <c>assemble</c> global.</summary>
/// <remarks>
///     <para>
///         CE's <c>assemble(line, address?, preference?, skipRangeCheck?)</c> returns a Lua byte table on success. It
///         returns <c>nil</c> alone when it rejects the instruction, or <c>nil</c> and a message when the attempt raised
///         inside CE (<c>LuaHandler.pas:2914-2961</c> at ec45d5f, ObservedSource; <c>celua.txt:236-238</c>). Both are
///         <see cref="InstructionOperationStatus.InstructionRejected" />: an unknown opcode or a missing symbol are both
///         rejections, and the SDK never reads or parses the message (audit A15-20, A15-21). This API always supplies the
///         address: it is the explicit origin CE receives when it evaluates a relative operand. It does not claim
///         relocation support beyond that CE operation or reconfigure CE's ambient target architecture.
///     </para>
///     <para>
///         The destination is caller-owned. The table is validated completely before the first byte is copied, so a
///         short destination or malformed later element cannot publish a prefix; a destination that is too small is
///         reported with the required length and no byte written (A15-19). Neither the table nor Lua text is retained
///         after the stack is restored. This API does not invoke a classic native assembler slot; the classic
///         buffer-based slots are not applicable to the managed-hostfxr profile and stay unprojected.
///     </para>
///     <para>
///         The target PID is checked before and after CE's call. A change observed after CE produced the bytes is
///         <see cref="InstructionOperationStatus.TargetChanged" />, no byte is copied, and no rollback is promised: the
///         checks are observations, not a lock (A15-06).
///     </para>
/// </remarks>
public static class InstructionAssembler
{
	private static readonly LuaRef SAssemble = new();

	/// <summary>Assembles one instruction into caller-owned storage using an explicit target address as its origin.</summary>
	/// <param name="targetProfile">
	///     The CE-observed selected PID and instruction profile used to validate
	///     <paramref name="address" />.
	/// </param>
	/// <param name="instruction">The instruction source sent to CE as UTF-8 without normalization.</param>
	/// <param name="address">
	///     The target origin supplied to CE; relative operands are interpreted by CE relative to this
	///     address.
	/// </param>
	/// <param name="destination">Caller-owned storage for the complete assembled byte sequence.</param>
	/// <param name="written">The number of copied bytes on success; zero for every other outcome.</param>
	/// <param name="requiredLength">The exact byte-table length when CE returned a valid table; zero otherwise.</param>
	/// <returns>A profile, capacity, instruction, availability, protected-call, or result-shape outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="instruction" /> is <see langword="null" />.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     This overload passes exactly two arguments to <c>assemble</c> (the text and the address), so CE applies its
	///     defaults for the preference and the range check. Use the overload with <see cref="AssemblePreference" /> to
	///     pass and record them.
	/// </remarks>
	[RequiresPluginEnabled]
	public static InstructionOperationStatus TryAssemble(InstructionTargetProfile targetProfile, string instruction,
		Address address,
		Span<byte> destination, out int written, out int requiredLength)
	{
		ArgumentNullException.ThrowIfNull(instruction);
		return TryAssembleCore(targetProfile, instruction, address, null, false, destination, out written,
			out requiredLength);
	}

	/// <summary>
	///     Assembles one instruction into caller-owned storage with an explicit origin, jump-encoding preference and
	///     range-check option, and echoes all of them in <paramref name="assembly" />.
	/// </summary>
	/// <param name="targetProfile">
	///     The CE-observed selected PID and instruction profile used to validate
	///     <paramref name="address" />.
	/// </param>
	/// <param name="instruction">The instruction source sent to CE as UTF-8 without normalization.</param>
	/// <param name="address">The target origin supplied to CE.</param>
	/// <param name="preference">The jump-encoding preference passed to CE unchanged.</param>
	/// <param name="skipRangeCheck">
	///     The range-check option passed to CE. When <see langword="true" />, CE skips its own check that a relative
	///     operand is reachable from <paramref name="address" /> and emits bytes even when they cannot encode the intended
	///     target; the caller then owns that risk.
	/// </param>
	/// <param name="destination">Caller-owned storage for the complete assembled byte sequence.</param>
	/// <param name="assembly">
	///     The echo of the call: target, profile, origin, preference and range-check option on every outcome, plus the
	///     written and required lengths.
	/// </param>
	/// <returns>A profile, capacity, instruction, availability, protected-call, or result-shape outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="instruction" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="preference" /> is not a defined <see cref="AssemblePreference" /> value; nothing is sent to CE.
	/// </exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     This overload always passes four arguments to <c>assemble</c>: the text, the address as a Lua integer, the
	///     preference as its CE integer and the option as a Lua boolean.
	/// </remarks>
	[RequiresPluginEnabled]
	public static InstructionOperationStatus TryAssemble(InstructionTargetProfile targetProfile, string instruction,
		Address address, AssemblePreference preference, bool skipRangeCheck, Span<byte> destination,
		out InstructionAssembly assembly)
	{
		ArgumentNullException.ThrowIfNull(instruction);
		if ((byte) preference > (byte) AssemblePreference.Far)
		{
			throw new ArgumentOutOfRangeException(nameof(preference), preference,
				"The assembler preference must be None, Short, Long or Far.");
		}

		InstructionOperationStatus status = TryAssembleCore(targetProfile, instruction, address, preference,
			skipRangeCheck, destination, out int written, out int requiredLength);
		assembly = new InstructionAssembly(targetProfile.Target, targetProfile.Profile, address, preference,
			skipRangeCheck, written, requiredLength);
		return status;
	}

	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification = "The protected call, full table validation, target recheck, and copy share one stack frame.")]
	private static InstructionOperationStatus TryAssembleCore(InstructionTargetProfile targetProfile,
		string instruction, Address address, AssemblePreference? preference, bool skipRangeCheck,
		Span<byte> destination, out int written, out int requiredLength)
	{
		written = 0;
		requiredLength = 0;

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

			LuaGlobalPushOutcome global = LuaGlobalFunctions.TryPushWithOutcome(state, SAssemble, "assemble"u8);
			if (global.Status == LuaGlobalPushStatus.Unavailable)
			{
				return InstructionOperationStatus.GlobalUnavailable;
			}

			if (!global.IsSuccess)
			{
				return InstructionOperationStatus.LuaFailure;
			}

			int resultStart = state.Top - 1;
			StringMarshaller.Push(state, instruction);
			Address.Push(state, address);
			int argumentCount = 2;
			if (preference is AssemblePreference explicitPreference)
			{
				// The four-argument form: CE's TassemblerPreference integer, then the range-check boolean.
				state.PushInteger((long) explicitPreference);
				state.PushBoolean(skipRangeCheck);
				argumentCount = 4;
			}

			if (!state.TryCall(argumentCount, LuaState.MultipleResults).IsOk)
			{
				return InstructionOperationStatus.LuaFailure;
			}

			targetStatus = InstructionProfiles.TryVerifyCurrent(state, targetProfile.Target);
			if (targetStatus != InstructionOperationStatus.Success)
			{
				return targetStatus;
			}

			int resultIndex = resultStart + 1;
			if (state.Top < resultIndex)
			{
				return InstructionOperationStatus.InvalidResult;
			}

			// nil alone (CE rejected the line) and nil plus a message (CE raised internally) are both a rejection. The
			// second value is never read, so no outcome depends on message text.
			if (state.IsNil(resultIndex))
			{
				return InstructionOperationStatus.InstructionRejected;
			}

			if (!state.IsTable(resultIndex))
			{
				return InstructionOperationStatus.InvalidResult;
			}

			int tableIndex = state.AbsoluteIndex(resultIndex);
			UIntPtr rawLength = state.RawLength(tableIndex);
			if (rawLength > int.MaxValue)
			{
				return InstructionOperationStatus.InvalidResult;
			}

			requiredLength = (int) rawLength;
			if (requiredLength > destination.Length)
			{
				return InstructionOperationStatus.DestinationTooSmall;
			}

			if (!ValidateByteTable(state, tableIndex, requiredLength))
			{
				requiredLength = 0;
				return InstructionOperationStatus.InvalidResult;
			}

			targetStatus = InstructionProfiles.TryVerifyCurrent(state, targetProfile.Target);
			if (targetStatus != InstructionOperationStatus.Success)
			{
				requiredLength = 0;
				return targetStatus;
			}

			for (int index = 0; index < requiredLength; index++)
			{
				_ = state.RawGetSequenceItem(tableIndex, index);
				_ = state.TryReadInteger(-1, out long value);
				destination[index] = (byte) value;
				state.Pop(1);
			}

			written = requiredLength;
			return InstructionOperationStatus.Success;
		}
		catch (LuaException)
		{
			written = 0;
			requiredLength = 0;
			return InstructionOperationStatus.LuaFailure;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	private static bool ValidateByteTable(LuaState state, int tableIndex, int length)
	{
		for (int index = 0; index < length; index++)
		{
			_ = state.RawGetSequenceItem(tableIndex, index);
			bool valid = state.TryReadInteger(-1, out long value) && value is >= byte.MinValue and <= byte.MaxValue;
			state.Pop(1);
			if (!valid)
			{
				return false;
			}
		}

		return true;
	}
}
