using System;

namespace CheatEngine.SDK.Engine.Enums;

/// <summary>
///     The Cheat Engine names of the enum members of this assembly (<c>vtDword</c>, <c>soExactValue</c>, ...), as
///     UTF-8, and the reverse lookup. Allocation-free in both directions.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why it exists.</b> The numeric values are what Cheat Engine's functions take and return, so the enums carry
///         them and nothing else. The names are needed in two places: properties that Cheat Engine publishes through LCL
///         RTTI read and write enum values as their identifier text (<c>MemoryRecord.VarType</c> is <c>"vtDword"</c> where
///         <c>MemoryRecord.Type</c> is <c>2</c>; <c>Stringlist.Duplicates</c> is <c>"dupIgnore"</c>), and diagnostics that
///         should show a plugin author the name they know from Cheat Engine's Lua scripts. Every name here was taken
///         from <c>defines.lua</c> of Cheat Engine 7.7.0.10621 together with its value; the pairing is pinned by the
///         tests.
///     </para>
///     <para>
///         <b>Policy.</b> The C# member is the idiomatic name (<see cref="VariableType.Dword" />), the CE name lives in
///         the member's XML documentation and here; nothing is stored in attributes, so no reflection and no allocation
///         is involved. <c>ToCEName</c> returns an empty span for a value that is not a defined member (or a flag
///         combination); <c>TryParseCEName</c> compares bytes exactly (Cheat Engine's names are case-sensitive
///         identifiers) and accepts the documented alias <c>vtUnicodeString</c>. Only UTF-8 overloads exist: Lua strings
///         are bytes, and a <c>"..."u8</c> literal is the natural argument.
///     </para>
/// </remarks>
public static class CEEnumNames
{
	/// <summary>The <c>vt*</c> name of a <see cref="VariableType" />.</summary>
	/// <param name="value">The member.</param>
	/// <returns>The name, or an empty span for an undefined value.</returns>
	public static ReadOnlySpan<byte> ToCEName(this VariableType value)
	{
		return value switch
		{
			VariableType.Byte => "vtByte"u8,
			VariableType.Word => "vtWord"u8,
			VariableType.Dword => "vtDword"u8,
			VariableType.Qword => "vtQword"u8,
			VariableType.Single => "vtSingle"u8,
			VariableType.Double => "vtDouble"u8,
			VariableType.String => "vtString"u8,
			VariableType.WideString => "vtWideString"u8,
			VariableType.ByteArray => "vtByteArray"u8,
			VariableType.Binary => "vtBinary"u8,
			VariableType.All => "vtAll"u8,
			VariableType.AutoAssembler => "vtAutoAssembler"u8,
			VariableType.Pointer => "vtPointer"u8,
			VariableType.Custom => "vtCustom"u8,
			VariableType.Grouped => "vtGrouped"u8,
			_ => default
		};
	}

	/// <summary>Parses a <c>vt*</c> name (<c>vtUnicodeString</c> is accepted as <see cref="VariableType.WideString" />).</summary>
	/// <param name="ceName">The name, UTF-8, exact case.</param>
	/// <param name="value">The member; <see cref="VariableType.Byte" /> on failure.</param>
	/// <returns><see langword="false" /> when the name is not a member.</returns>
	public static bool TryParseCEName(ReadOnlySpan<byte> ceName, out VariableType value)
	{
		if (ceName.SequenceEqual("vtUnicodeString"u8))
		{
			value = VariableType.WideString;
			return true;
		}

		for (VariableType candidate = VariableType.Byte; candidate <= VariableType.Grouped; candidate++)
		{
			if (ceName.SequenceEqual(candidate.ToCEName()))
			{
				value = candidate;
				return true;
			}
		}

		value = default;
		return false;
	}

	/// <summary>The <c>so*</c> name of a <see cref="ScanOption" />.</summary>
	/// <param name="value">The member.</param>
	/// <returns>The name, or an empty span for an undefined value.</returns>
	public static ReadOnlySpan<byte> ToCEName(this ScanOption value)
	{
		return value switch
		{
			ScanOption.UnknownValue => "soUnknownValue"u8,
			ScanOption.ExactValue => "soExactValue"u8,
			ScanOption.ValueBetween => "soValueBetween"u8,
			ScanOption.BiggerThan => "soBiggerThan"u8,
			ScanOption.SmallerThan => "soSmallerThan"u8,
			ScanOption.IncreasedValue => "soIncreasedValue"u8,
			ScanOption.IncreasedValueBy => "soIncreasedValueBy"u8,
			ScanOption.DecreasedValue => "soDecreasedValue"u8,
			ScanOption.DecreasedValueBy => "soDecreasedValueBy"u8,
			ScanOption.Changed => "soChanged"u8,
			ScanOption.Unchanged => "soUnchanged"u8,
			_ => default
		};
	}

	/// <summary>Parses a <c>so*</c> name.</summary>
	/// <param name="ceName">The name, UTF-8, exact case.</param>
	/// <param name="value">The member; <see cref="ScanOption.UnknownValue" /> on failure.</param>
	/// <returns><see langword="false" /> when the name is not a member.</returns>
	public static bool TryParseCEName(ReadOnlySpan<byte> ceName, out ScanOption value)
	{
		for (ScanOption candidate = ScanOption.UnknownValue; candidate <= ScanOption.Unchanged; candidate++)
		{
			if (ceName.SequenceEqual(candidate.ToCEName()))
			{
				value = candidate;
				return true;
			}
		}

		value = default;
		return false;
	}

	/// <summary>The <c>rt*</c> name of a <see cref="RoundingType" />.</summary>
	/// <param name="value">The member.</param>
	/// <returns>The name, or an empty span for an undefined value.</returns>
	public static ReadOnlySpan<byte> ToCEName(this RoundingType value)
	{
		return value switch
		{
			RoundingType.Rounded => "rtRounded"u8,
			RoundingType.ExtremeRounded => "rtExtremerounded"u8,
			RoundingType.Truncated => "rtTruncated"u8,
			_ => default
		};
	}

	/// <summary>Parses an <c>rt*</c> name.</summary>
	/// <param name="ceName">The name, UTF-8, exact case.</param>
	/// <param name="value">The member; <see cref="RoundingType.Rounded" /> on failure.</param>
	/// <returns><see langword="false" /> when the name is not a member.</returns>
	public static bool TryParseCEName(ReadOnlySpan<byte> ceName, out RoundingType value)
	{
		for (RoundingType candidate = RoundingType.Rounded; candidate <= RoundingType.Truncated; candidate++)
		{
			if (ceName.SequenceEqual(candidate.ToCEName()))
			{
				value = candidate;
				return true;
			}
		}

		value = default;
		return false;
	}

	/// <summary>The <c>fsm*</c> name of a <see cref="FastScanMethod" />.</summary>
	/// <param name="value">The member.</param>
	/// <returns>The name, or an empty span for an undefined value.</returns>
	public static ReadOnlySpan<byte> ToCEName(this FastScanMethod value)
	{
		return value switch
		{
			FastScanMethod.NotAligned => "fsmNotAligned"u8,
			FastScanMethod.Aligned => "fsmAligned"u8,
			FastScanMethod.LastDigits => "fsmLastDigits"u8,
			_ => default
		};
	}

	/// <summary>Parses an <c>fsm*</c> name.</summary>
	/// <param name="ceName">The name, UTF-8, exact case.</param>
	/// <param name="value">The member; <see cref="FastScanMethod.NotAligned" /> on failure.</param>
	/// <returns><see langword="false" /> when the name is not a member.</returns>
	public static bool TryParseCEName(ReadOnlySpan<byte> ceName, out FastScanMethod value)
	{
		for (FastScanMethod candidate = FastScanMethod.NotAligned; candidate <= FastScanMethod.LastDigits; candidate++)
		{
			if (ceName.SequenceEqual(candidate.ToCEName()))
			{
				value = candidate;
				return true;
			}
		}

		value = default;
		return false;
	}

	/// <summary>The <c>bpm*</c> name of a <see cref="BreakpointMethod" />.</summary>
	/// <param name="value">The member.</param>
	/// <returns>The name, or an empty span for an undefined value.</returns>
	public static ReadOnlySpan<byte> ToCEName(this BreakpointMethod value)
	{
		return value switch
		{
			BreakpointMethod.Int3 => "bpmInt3"u8,
			BreakpointMethod.DebugRegister => "bpmDebugRegister"u8,
			BreakpointMethod.Exception => "bpmException"u8,
			_ => default
		};
	}

	/// <summary>Parses a <c>bpm*</c> name.</summary>
	/// <param name="ceName">The name, UTF-8, exact case.</param>
	/// <param name="value">The member; <see cref="BreakpointMethod.Int3" /> on failure.</param>
	/// <returns><see langword="false" /> when the name is not a member.</returns>
	public static bool TryParseCEName(ReadOnlySpan<byte> ceName, out BreakpointMethod value)
	{
		for (BreakpointMethod candidate = BreakpointMethod.Int3; candidate <= BreakpointMethod.Exception; candidate++)
		{
			if (ceName.SequenceEqual(candidate.ToCEName()))
			{
				value = candidate;
				return true;
			}
		}

		value = default;
		return false;
	}

	/// <summary>The <c>bpt*</c> name of a <see cref="BreakpointTrigger" />.</summary>
	/// <param name="value">The member.</param>
	/// <returns>The name, or an empty span for an undefined value.</returns>
	public static ReadOnlySpan<byte> ToCEName(this BreakpointTrigger value)
	{
		return value switch
		{
			BreakpointTrigger.Execute => "bptExecute"u8,
			BreakpointTrigger.Access => "bptAccess"u8,
			BreakpointTrigger.Write => "bptWrite"u8,
			_ => default
		};
	}

	/// <summary>Parses a <c>bpt*</c> name.</summary>
	/// <param name="ceName">The name, UTF-8, exact case.</param>
	/// <param name="value">The member; <see cref="BreakpointTrigger.Execute" /> on failure.</param>
	/// <returns><see langword="false" /> when the name is not a member.</returns>
	public static bool TryParseCEName(ReadOnlySpan<byte> ceName, out BreakpointTrigger value)
	{
		for (BreakpointTrigger candidate = BreakpointTrigger.Execute; candidate <= BreakpointTrigger.Write; candidate++)
		{
			if (ceName.SequenceEqual(candidate.ToCEName()))
			{
				value = candidate;
				return true;
			}
		}

		value = default;
		return false;
	}

	/// <summary>The <c>co_*</c> name of a <see cref="ContinueMethod" />.</summary>
	/// <param name="value">The member.</param>
	/// <returns>The name, or an empty span for an undefined value.</returns>
	public static ReadOnlySpan<byte> ToCEName(this ContinueMethod value)
	{
		return value switch
		{
			ContinueMethod.Run => "co_run"u8,
			ContinueMethod.StepInto => "co_stepinto"u8,
			ContinueMethod.StepOver => "co_stepover"u8,
			_ => default
		};
	}

	/// <summary>Parses a <c>co_*</c> name.</summary>
	/// <param name="ceName">The name, UTF-8, exact case.</param>
	/// <param name="value">The member; <see cref="ContinueMethod.Run" /> on failure.</param>
	/// <returns><see langword="false" /> when the name is not a member.</returns>
	public static bool TryParseCEName(ReadOnlySpan<byte> ceName, out ContinueMethod value)
	{
		for (ContinueMethod candidate = ContinueMethod.Run; candidate <= ContinueMethod.StepOver; candidate++)
		{
			if (ceName.SequenceEqual(candidate.ToCEName()))
			{
				value = candidate;
				return true;
			}
		}

		value = default;
		return false;
	}

	/// <summary>
	///     The <c>PAGE_*</c> name of a single <see cref="MemoryProtection" /> value; a combination of bits,
	///     <see cref="MemoryProtection.None" /> and undefined values give an empty span.
	/// </summary>
	/// <param name="value">The member.</param>
	/// <returns>The name, or an empty span.</returns>
	public static ReadOnlySpan<byte> ToCEName(this MemoryProtection value)
	{
		return value switch
		{
			MemoryProtection.ReadOnly => "PAGE_READONLY"u8,
			MemoryProtection.ReadWrite => "PAGE_READWRITE"u8,
			MemoryProtection.WriteCopy => "PAGE_WRITECOPY"u8,
			MemoryProtection.Execute => "PAGE_EXECUTE"u8,
			MemoryProtection.ExecuteRead => "PAGE_EXECUTE_READ"u8,
			MemoryProtection.ExecuteReadWrite => "PAGE_EXECUTE_READWRITE"u8,
			MemoryProtection.ExecuteWriteCopy => "PAGE_EXECUTE_WRITECOPY"u8,
			_ => default
		};
	}

	/// <summary>Parses a single <c>PAGE_*</c> name.</summary>
	/// <param name="ceName">The name, UTF-8, exact case.</param>
	/// <param name="value">The member; <see cref="MemoryProtection.None" /> on failure.</param>
	/// <returns><see langword="false" /> when the name is not a member.</returns>
	public static bool TryParseCEName(ReadOnlySpan<byte> ceName, out MemoryProtection value)
	{
		for (MemoryProtection candidate = MemoryProtection.ReadOnly;
		     candidate <= MemoryProtection.ExecuteWriteCopy;
		     candidate = (MemoryProtection) ((uint) candidate << 1))
		{
			if (ceName.SequenceEqual(candidate.ToCEName()))
			{
				value = candidate;
				return true;
			}
		}

		value = default;
		return false;
	}

	/// <summary>The <c>dup*</c> name of a <see cref="DuplicateHandling" />.</summary>
	/// <param name="value">The member.</param>
	/// <returns>The name, or an empty span for an undefined value.</returns>
	public static ReadOnlySpan<byte> ToCEName(this DuplicateHandling value)
	{
		return value switch
		{
			DuplicateHandling.Ignore => "dupIgnore"u8,
			DuplicateHandling.Accept => "dupAccept"u8,
			DuplicateHandling.Error => "dupError"u8,
			_ => default
		};
	}

	/// <summary>Parses a <c>dup*</c> name.</summary>
	/// <param name="ceName">The name, UTF-8, exact case.</param>
	/// <param name="value">The member; <see cref="DuplicateHandling.Ignore" /> on failure.</param>
	/// <returns><see langword="false" /> when the name is not a member.</returns>
	public static bool TryParseCEName(ReadOnlySpan<byte> ceName, out DuplicateHandling value)
	{
		for (DuplicateHandling candidate = DuplicateHandling.Ignore; candidate <= DuplicateHandling.Error; candidate++)
		{
			if (ceName.SequenceEqual(candidate.ToCEName()))
			{
				value = candidate;
				return true;
			}
		}

		value = default;
		return false;
	}
}
