using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

namespace CheatEngine.SDK.Lua.Interop.Tests.Support;

/// <summary>
///     A minimal IL walker for the forwarder tests: it lists the instructions of a method body with their 32-bit (token,
///     branch) or 8-/16-bit (index) operand. The decoding table comes from <see cref="OpCodes" />, so every instruction
///     length is right and an operand byte can never be mistaken for an opcode.
/// </summary>
internal static class IlReader
{
	private const byte TwoBytePrefix = 0xFE;

	private static readonly Dictionary<ushort, OpCode> s_opCodes = BuildTable();

	/// <summary>Decodes the body of <paramref name="method" />; operands wider than 32 bits are reported as 0.</summary>
	public static List<(OpCode Code, int Operand)> Read(MethodBase method)
	{
		byte[] il = method.GetMethodBody()?.GetILAsByteArray() ??
					throw new InvalidOperationException(method.Name + " has no IL body.");
		List<(OpCode Code, int Operand)> instructions = [];

		int offset = 0;
		while (offset < il.Length)
		{
			ushort value = il[offset++];
			if (value == TwoBytePrefix)
			{
				value = (ushort) ((TwoBytePrefix << 8) | il[offset++]);
			}

			if (!s_opCodes.TryGetValue(value, out OpCode code))
			{
				throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
					$"{method.Name}: unknown opcode 0x{value:X} at offset {offset}."));
			}

			int size = OperandSize(code.OperandType, il, offset);
			int operand = size switch
			{
				1 => il[offset],
				2 => BitConverter.ToUInt16(il, offset),
				4 => BitConverter.ToInt32(il, offset),
				_ => 0
			};

			instructions.Add((code, operand));
			offset += size;
		}

		return instructions;
	}

	private static int OperandSize(OperandType type, byte[] il, int offset)
	{
		return type switch
		{
			OperandType.InlineNone => 0,
			OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
			OperandType.InlineVar => 2,
			OperandType.InlineI8 or OperandType.InlineR => 8,
			OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
			_ => 4
		};
	}

	private static Dictionary<ushort, OpCode> BuildTable()
	{
		Dictionary<ushort, OpCode> table = [];
		foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (field.GetValue(null) is OpCode code)
			{
				table[unchecked((ushort) code.Value)] = code;
			}
		}

		return table;
	}
}
