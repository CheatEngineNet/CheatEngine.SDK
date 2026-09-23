using System;
using System.Buffers.Binary;

namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>A pointer width that is valid for the supported 32-bit or 64-bit Cheat Engine process models.</summary>
public readonly struct PointerSize : IEquatable<PointerSize>
{
	private readonly byte _bytes;

	/// <summary>Gets an unavailable or not-yet-observed pointer width.</summary>
	public static PointerSize Unknown => default;

	/// <summary>Gets the 32-bit pointer width.</summary>
	public static PointerSize Bit32 => new(4);

	/// <summary>Gets the 64-bit pointer width.</summary>
	public static PointerSize Bit64 => new(8);

	/// <summary>Initializes a pointer width measured in bytes.</summary>
	/// <param name="bytes">Either 4 or 8.</param>
	/// <exception cref="System.ArgumentOutOfRangeException"><paramref name="bytes" /> is not 4 or 8.</exception>
	public PointerSize(int bytes)
	{
		if (bytes is not 4 and not 8)
		{
			throw new ArgumentOutOfRangeException(nameof(bytes), bytes,
				"A Cheat Engine pointer size must be 4 or 8 bytes.");
		}

		_bytes = (byte) bytes;
	}

	/// <summary>Gets the width in bytes, or zero when it is unknown.</summary>
	public int Bytes => _bytes;

	/// <summary>Gets the width in bits, or zero when it is unknown.</summary>
	public int Bits => _bytes * 8;

	/// <summary>Gets a value indicating whether the width has been established.</summary>
	public bool IsKnown => _bytes != 0;

	/// <summary>Returns the natural instruction-set width of an architecture: 4 bytes for x86 and ARM32, 8 for x64 and ARM64.</summary>
	/// <param name="architecture">The architecture.</param>
	/// <returns>The natural ISA width, or <see cref="Unknown" /> when <paramref name="architecture" /> is unknown.</returns>
	/// <remarks>
	///     <para>
	///         Obsolete (<c>CESDK7001</c>). An architecture determines neither Cheat Engine's configured pointer size nor
	///         the target bitness: on CE 7.7.0.10621 x64, <c>getPointerSize</c> reported 4 on a 64-bit x64 target after
	///         <c>setPointerSize(4)</c>, and <c>readPointer</c> kept following the 64-bit flag (spike C3 D3, Lua-only,
	///         ObservedHost design input). Use <see cref="TargetArchitectureObservation.ConfiguredPointerSize" /> for the
	///         configured size and <see cref="TargetArchitectureObservation.Bitness" /> for the bitness.
	///     </para>
	///     <para>
	///         The method remains, unchanged, for binary compatibility with CheatEngine.SDK 1.0.0 and is removed no earlier
	///         than the next major version.
	///     </para>
	/// </remarks>
	[Obsolete(
		"An architecture does not determine Cheat Engine's configured pointer size or the target bitness. Use TargetArchitectureObservation.ConfiguredPointerSize or Bitness.",
		DiagnosticId = "CESDK7001",
		UrlFormat = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md")]
	public static PointerSize FromArchitecture(CheatEngineArchitecture architecture)
	{
		return architecture switch
		{
			CheatEngineArchitecture.X86 or CheatEngineArchitecture.Arm32 => Bit32,
			CheatEngineArchitecture.X64 or CheatEngineArchitecture.Arm64 => Bit64,
			_ => Unknown
		};
	}

	/// <summary>
	///     Reads one little-endian target pointer from the beginning of <paramref name="source" /> without consulting
	///     the managed host pointer width.
	/// </summary>
	/// <param name="source">The bytes supplied by a target-specific primitive.</param>
	/// <param name="value">The unsigned pointer bits, or zero when this method returns <see langword="false" />.</param>
	/// <returns>
	///     <see langword="true" /> when this instance is known and <paramref name="source" /> contains its exact
	///     number of bytes; otherwise, <see langword="false" />.
	/// </returns>
	/// <remarks>
	///     This is explicit primitive marshalling, not an unmanaged-struct projection. A 32-bit target still consumes
	///     four bytes when this SDK runs in CE's supported 64-bit host process. Little-endian byte order is an assumption
	///     of the local x86/x64 target profile (audit A12-04), not a general Cheat Engine fact: the SDK does not observe
	///     a target's byte order.
	/// </remarks>
	public bool TryReadLittleEndian(ReadOnlySpan<byte> source, out ulong value)
	{
		if (source.Length != _bytes)
		{
			value = default;
			return false;
		}

		switch (_bytes)
		{
			case 4:
				if (BinaryPrimitives.TryReadUInt32LittleEndian(source, out uint narrow))
				{
					value = narrow;
					return true;
				}

				break;

			case 8:
				if (BinaryPrimitives.TryReadUInt64LittleEndian(source, out value))
				{
					return true;
				}

				break;
		}

		value = default;
		return false;
	}

	/// <summary>
	///     Writes one little-endian target pointer to <paramref name="destination" /> without consulting the managed
	///     host pointer width.
	/// </summary>
	/// <param name="value">The unsigned target pointer bits.</param>
	/// <param name="destination">The exact target-pointer-sized destination.</param>
	/// <returns>
	///     <see langword="true" /> when this instance is known, <paramref name="value" /> fits it, and
	///     <paramref name="destination" /> has its exact number of bytes; otherwise, <see langword="false" /> and
	///     <paramref name="destination" /> is unchanged.
	/// </returns>
	/// <remarks>
	///     This is explicit primitive marshalling, not an unmanaged-struct projection. A 32-bit target rejects high
	///     bits instead of silently truncating them through the x64 host process. Little-endian byte order is an
	///     assumption of the local x86/x64 target profile (audit A12-04), not a general Cheat Engine fact.
	/// </remarks>
	public bool TryWriteLittleEndian(ulong value, Span<byte> destination)
	{
		if (destination.Length != _bytes)
		{
			return false;
		}

		switch (_bytes)
		{
			case 4:
				if (value > uint.MaxValue)
				{
					return false;
				}

				BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint) value);
				return true;

			case 8:
				BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
				return true;

			default:
				return false;
		}
	}

	/// <inheritdoc />
	public bool Equals(PointerSize other)
	{
		return _bytes == other._bytes;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is PointerSize other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return _bytes.GetHashCode();
	}

	/// <summary>Tests two pointer widths for equality.</summary>
	/// <param name="left">The first width.</param>
	/// <param name="right">The second width.</param>
	/// <returns><see langword="true" /> when the widths are equal.</returns>
	public static bool operator ==(PointerSize left, PointerSize right)
	{
		return left.Equals(right);
	}

	/// <summary>Tests two pointer widths for inequality.</summary>
	/// <param name="left">The first width.</param>
	/// <param name="right">The second width.</param>
	/// <returns><see langword="true" /> when the widths differ.</returns>
	public static bool operator !=(PointerSize left, PointerSize right)
	{
		return !left.Equals(right);
	}
}
