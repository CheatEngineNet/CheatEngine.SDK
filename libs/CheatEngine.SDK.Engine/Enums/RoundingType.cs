namespace CheatEngine.SDK.Engine.Enums;

/// <summary>
///     How an exact-value scan of a floating-point type treats the digits the input does not spell out (<c>rt*</c> in
///     <c>defines.lua</c>): the <c>roundingtype</c> argument of <c>MemScan.firstScan</c> and <c>nextScan</c>, and the
///     <c>MemScan.Roundingtype</c> property.
/// </summary>
/// <remarks>Values verified against <c>defines.lua</c> of Cheat Engine 7.7.0.10621.</remarks>
public enum RoundingType
{
	/// <summary>Match values that round to the input at its precision. CE: <c>rtRounded</c>.</summary>
	Rounded = 0,

	/// <summary>
	///     Match a wide band around the input, about one unit of its last digit on each side. CE: <c>rtExtremerounded</c>
	///     .
	/// </summary>
	ExtremeRounded = 1,

	/// <summary>Match values that truncate to the input at its precision. CE: <c>rtTruncated</c>.</summary>
	Truncated = 2
}
