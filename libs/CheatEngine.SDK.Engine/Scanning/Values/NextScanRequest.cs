using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     The positional arguments of Cheat Engine 7.7's <c>MemScan.nextScan</c> method, including its optional saved-result
///     name.
/// </summary>
/// <remarks>
///     CE 7.7.0.10621 <c>celua.txt</c> lines 2618-2646 document nine required arguments and the optional
///     <c>savedresultname</c> tenth argument. A <see langword="null" /> <see cref="SavedResultName" /> means that this
///     wrapper omits the tenth Lua argument entirely; it does not push a trailing Lua <c>nil</c>.
/// </remarks>
public readonly struct NextScanRequest
{
	/// <summary>Initializes a complete next-scan request.</summary>
	/// <param name="scanOption">The comparison mode for the existing result set.</param>
	/// <param name="roundingType">The floating-point comparison rule.</param>
	/// <param name="input1">The primary scan text; use an empty string when the option does not need it.</param>
	/// <param name="input2">The secondary scan text; use an empty string when the option does not need it.</param>
	/// <param name="isHexadecimalInput">Whether CE interprets the inputs as hexadecimal text.</param>
	/// <param name="isNotBinaryString">Whether a binary type's input is decimal rather than a bit string.</param>
	/// <param name="isUnicodeScan">Whether a string scan uses UTF-16 rather than CE's normal UTF-8 mode.</param>
	/// <param name="isCaseSensitive">Whether a string scan matches case.</param>
	/// <param name="isPercentageScan">Whether CE interprets applicable comparisons as percentages.</param>
	/// <param name="savedResultName">An optional saved CE result-set name.</param>
	public NextScanRequest(
		ScanOption scanOption,
		RoundingType roundingType,
		string input1,
		string input2,
		bool isHexadecimalInput,
		bool isNotBinaryString,
		bool isUnicodeScan,
		bool isCaseSensitive,
		bool isPercentageScan,
		string? savedResultName = null)
	{
		ScanOption = scanOption;
		RoundingType = roundingType;
		Input1 = input1;
		Input2 = input2;
		IsHexadecimalInput = isHexadecimalInput;
		IsNotBinaryString = isNotBinaryString;
		IsUnicodeScan = isUnicodeScan;
		IsCaseSensitive = isCaseSensitive;
		IsPercentageScan = isPercentageScan;
		SavedResultName = savedResultName;
	}

	/// <summary>Gets the CE next-scan comparison mode.</summary>
	public ScanOption ScanOption
	{
		get;
	}

	/// <summary>Gets the CE floating-point rounding rule.</summary>
	public RoundingType RoundingType
	{
		get;
	}

	/// <summary>Gets the primary scan text.</summary>
	public string Input1
	{
		get;
	}

	/// <summary>Gets the secondary scan text.</summary>
	public string Input2
	{
		get;
	}

	/// <summary>Gets whether CE parses the input text as hexadecimal.</summary>
	public bool IsHexadecimalInput
	{
		get;
	}

	/// <summary>Gets whether a binary input is decimal rather than a bit string.</summary>
	public bool IsNotBinaryString
	{
		get;
	}

	/// <summary>Gets whether a string scan uses UTF-16.</summary>
	public bool IsUnicodeScan
	{
		get;
	}

	/// <summary>Gets whether a string scan is case-sensitive.</summary>
	public bool IsCaseSensitive
	{
		get;
	}

	/// <summary>Gets whether CE treats applicable comparison values as percentages.</summary>
	public bool IsPercentageScan
	{
		get;
	}

	/// <summary>Gets the optional CE saved-result name, or <see langword="null" /> to omit the argument.</summary>
	public string? SavedResultName
	{
		get;
	}

	/// <summary>Builds an exact-value next-scan request using CE's normal non-hexadecimal settings.</summary>
	/// <param name="input">The exact value text.</param>
	/// <returns>An exact-value request using CE's normal rounded comparison rule.</returns>
	public static NextScanRequest ExactValue(string input)
	{
		return new NextScanRequest(
			ScanOption.ExactValue,
			RoundingType.Rounded,
			input,
			string.Empty,
			false,
			false,
			false,
			false,
			false);
	}
}
