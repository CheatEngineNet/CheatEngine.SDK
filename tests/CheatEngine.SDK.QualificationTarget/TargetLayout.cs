using System.Text;

namespace QualificationTarget;

/// <summary>
///     The fixed byte patterns and value cells of the target. Every value is a constant, so two runs of the same build
///     expose the same patterns and the same initial values; only addresses and the PID differ.
/// </summary>
internal static class TargetLayout
{
	/// <summary>Distance between two heap copies of the marker, so that copies never touch.</summary>
	internal const int HeapCopyStride = 64;

	/// <summary>Length of the many-results pattern.</summary>
	internal const int RepeatedPatternLength = 8;

	/// <summary>Initial value of the Int32 cell (0x5DC3A117); <c>step</c> adds one.</summary>
	internal const int InitialInt32 = 1_573_167_383;

	/// <summary>Initial value of the Int64 cell (0x0123456789ABCDEF); <c>step</c> adds one.</summary>
	internal const long InitialInt64 = 81_985_529_216_486_895;

	/// <summary>
	///     The module-resident marker. A <see cref="ReadOnlySpan{T}" /> over a constant array is emitted as read-only data
	///     of the image, so the executable itself holds it; the heap copies are made from it at run time.
	/// </summary>
	internal static ReadOnlySpan<byte> Marker =>
	[
		0xC3, 0x5D, 0x4B, 0x51, 0x54, 0x9A, 0x7E, 0x21, 0xE8, 0x0F, 0xB2, 0x66, 0x13, 0xD7, 0x4C, 0xA5
	];

	/// <summary>
	///     The many-results pattern. It is computed at run time, so its bytes are not stored in the image and every match
	///     of it lies in the heap region. All eight bytes differ, so matches cannot overlap.
	/// </summary>
	internal static byte[] CreateRepeatedPattern()
	{
		byte[] pattern = new byte[RepeatedPatternLength];
		for (int index = 0; index < pattern.Length; index++)
		{
			pattern[index] = (byte) (0x9E ^ ((index * 0x2B) + 0x11));
		}

		return pattern;
	}

	/// <summary>Formats bytes as the space-separated upper-case hex text of an AOB pattern.</summary>
	internal static string ToPattern(ReadOnlySpan<byte> bytes)
	{
		StringBuilder builder = new(bytes.Length * 3);
		foreach (byte value in bytes)
		{
			if (builder.Length > 0)
			{
				builder.Append(' ');
			}

			builder.Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
		}

		return builder.ToString();
	}
}
