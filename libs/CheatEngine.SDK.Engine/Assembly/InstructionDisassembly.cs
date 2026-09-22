using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>A copied, SDK-parsed instruction line from Cheat Engine's disassembler.</summary>
/// <remarks>
///     The fields are UTF-8 Lua strings copied into managed strings while their stack values remain reachable. They
///     own no Lua storage, CE object, registry reference, or native display buffer. <see cref="Utf8ByteLength" /> is
///     the total byte length of the four copied fields, before UTF-8 decoding; invalid input sequences follow the
///     SDK's standard replacement-character decoding rule.
/// </remarks>
/// <param name="Address">The profile-validated target address supplied to the disassembler.</param>
/// <param name="AddressText">The address column returned by CE's <c>splitDisassembledString</c> helper.</param>
/// <param name="Bytes">The byte column returned by CE's helper.</param>
/// <param name="Opcode">The mnemonic and operand column returned by CE's helper.</param>
/// <param name="Extra">The additional annotation column returned by CE's helper.</param>
/// <param name="Utf8ByteLength">The total copied UTF-8 byte length of the four text fields.</param>
public readonly record struct InstructionDisassembly(
	Address Address,
	string AddressText,
	string Bytes,
	string Opcode,
	string Extra,
	int Utf8ByteLength);
