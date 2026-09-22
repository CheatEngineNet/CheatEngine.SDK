using System.Globalization;
using System.Text.RegularExpressions;

using CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Infrastructure;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Generator;

/// <summary>Parity tests for the production protected-operation catalogue and its C11 implementation.</summary>
public sealed class NativeBridgeParityTests
{
	private static readonly TimeSpan RegularExpressionTimeout = TimeSpan.FromSeconds(1);

	private static readonly NativeOperation[] ExpectedOperations =
	[
		new("OP_PUSH_BYTES", 0),
		new("OP_CREATE_TABLE", 1),
		new("OP_NEW_USERDATA", 2),
		new("OP_PUSH_CLOSURE", 3),
		new("OP_RAWSET", 4),
		new("OP_RAWSETI", 5),
		new("OP_RAWSETP", 6),
		new("OP_REF", 7),
		new("OP_PUSH_REF", 8),
		new("OP_UNREF", 9),
		new("OP_PUSH_HOST_OBJECT", 10),
		new("OP_PUSH_BYTE_TABLE", 11)
	];

	[Fact]
	public void Production_catalogue_C11_enum_switch_and_operation_mask_remain_in_lockstep()
	{
		string source = ProductionNativeBridge.Read();
		string nativeEnum = ExtractNativeOperationEnum(source);
		string operationSwitch = ExtractOperationSwitch(source);
		string operationMask = ExtractOperationMask(source);

		Assert.Equal(ExpectedOperations.Length, CountOperationEnumValues(nativeEnum));
		Assert.Equal(ExpectedOperations.Length, ExtractOperationCount(nativeEnum));

		for (int i = 0; i < ExpectedOperations.Length; i++)
		{
			NativeOperation operation = ExpectedOperations[i];
			Assert.True(HasNativeEnumValue(nativeEnum, operation),
				$"The native operation enum does not define {operation.Name} = {operation.Opcode}.");
			Assert.True(HasOperationCase(operationSwitch, operation.Name),
				$"The native protected-operation switch has no case for {operation.Name}.");
			Assert.Contains(operation.Name, operationMask, StringComparison.Ordinal);
		}
	}

	private static string ExtractNativeOperationEnum(string source)
	{
		Match match = Regex.Match(source,
			"enum\\s*\\{\\s*(?<values>OP_PUSH_BYTES\\s*=\\s*0,[\\s\\S]*?CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_COUNT\\s*=\\s*\\d+)\\s*\\};",
			RegexOptions.CultureInvariant,
			RegularExpressionTimeout);
		Assert.True(match.Success, "The C11 protected-operation enum was not found.");
		return match.Groups["values"].Value;
	}

	private static string ExtractOperationSwitch(string source)
	{
		Match match = Regex.Match(source,
			"switch\\s*\\(c->op\\)\\s*\\{(?<cases>[\\s\\S]*?)\\n\\s*default:",
			RegexOptions.CultureInvariant,
			RegularExpressionTimeout);
		Assert.True(match.Success, "The C11 protected-operation switch was not found.");
		return match.Groups["cases"].Value;
	}

	private static string ExtractOperationMask(string source)
	{
		Match match = Regex.Match(source,
			"#define\\s+CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_MASK\\s*\\\\(?<mask>[\\s\\S]*?)\\n\\s*\\n",
			RegexOptions.CultureInvariant,
			RegularExpressionTimeout);
		Assert.True(match.Success, "The C11 protected-operation mask was not found.");
		return match.Groups["mask"].Value;
	}

	private static int CountOperationEnumValues(string nativeEnum)
	{
		MatchCollection matches = Regex.Matches(nativeEnum,
			"\\bOP_[A-Z_]+\\s*=\\s*\\d+",
			RegexOptions.CultureInvariant,
			RegularExpressionTimeout);
		return matches.Count;
	}

	private static int ExtractOperationCount(string nativeEnum)
	{
		Match match = Regex.Match(nativeEnum,
			"CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_COUNT\\s*=\\s*(?<count>\\d+)",
			RegexOptions.CultureInvariant,
			RegularExpressionTimeout);
		Assert.True(match.Success, "The C11 protected-operation count sentinel was not found.");
		return int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
	}

	private static bool HasNativeEnumValue(string nativeEnum, NativeOperation operation)
	{
		string pattern = $"\\b{Regex.Escape(operation.Name)}\\s*=\\s*{operation.Opcode}\\b";
		return Regex.IsMatch(nativeEnum, pattern, RegexOptions.CultureInvariant, RegularExpressionTimeout);
	}

	private static bool HasOperationCase(string operationSwitch, string operationName)
	{
		string pattern = $"\\bcase\\s+{Regex.Escape(operationName)}\\s*:";
		return Regex.IsMatch(operationSwitch, pattern, RegexOptions.CultureInvariant, RegularExpressionTimeout);
	}

	private readonly record struct NativeOperation(string Name, int Opcode);
}
