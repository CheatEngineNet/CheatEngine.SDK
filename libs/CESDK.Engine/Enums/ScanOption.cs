namespace CESDK.Engine.Enums;

/// <summary>
///     What a memory scan compares (<c>so*</c> in <c>defines.lua</c>): the first argument of <c>MemScan.firstScan</c>
///     and <c>MemScan.nextScan</c>, and the <c>MemScan.ScanOption</c> property.
/// </summary>
/// <remarks>
///     A first scan accepts <see cref="UnknownValue" />, <see cref="ExactValue" />, <see cref="ValueBetween" />,
///     <see cref="BiggerThan" /> and <see cref="SmallerThan" />; a next scan accepts every member except
///     <see cref="UnknownValue" />. Values verified against <c>defines.lua</c> of Cheat Engine 7.7.0.10621.
/// </remarks>
public enum ScanOption
{
    /// <summary>Record every address without comparing (first scan only). CE: <c>soUnknownValue</c>.</summary>
    UnknownValue = 0,

    /// <summary>Equal to the input. CE: <c>soExactValue</c>.</summary>
    ExactValue = 1,

    /// <summary>Between the two inputs. CE: <c>soValueBetween</c>.</summary>
    ValueBetween = 2,

    /// <summary>Greater than the input. CE: <c>soBiggerThan</c>.</summary>
    BiggerThan = 3,

    /// <summary>Less than the input. CE: <c>soSmallerThan</c>.</summary>
    SmallerThan = 4,

    /// <summary>Greater than in the previous scan (next scan only). CE: <c>soIncreasedValue</c>.</summary>
    IncreasedValue = 5,

    /// <summary>Greater than in the previous scan by the input (next scan only). CE: <c>soIncreasedValueBy</c>.</summary>
    IncreasedValueBy = 6,

    /// <summary>Less than in the previous scan (next scan only). CE: <c>soDecreasedValue</c>.</summary>
    DecreasedValue = 7,

    /// <summary>Less than in the previous scan by the input (next scan only). CE: <c>soDecreasedValueBy</c>.</summary>
    DecreasedValueBy = 8,

    /// <summary>Different from the previous scan (next scan only). CE: <c>soChanged</c>.</summary>
    Changed = 9,

    /// <summary>Same as in the previous scan (next scan only). CE: <c>soUnchanged</c>.</summary>
    Unchanged = 10
}
