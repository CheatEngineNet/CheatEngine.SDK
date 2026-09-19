namespace CESDK.Engine.Enums;

/// <summary>
///     What a sorted <c>Stringlist</c> does with a line equal to one it already holds (<c>dup*</c> in
///     <c>defines.lua</c>): the <c>Duplicates</c> property and the <c>setDuplicates</c>/<c>getDuplicates</c> methods.
///     Only consulted while <c>Sorted</c> is true.
/// </summary>
/// <remarks>
///     Values verified against <c>defines.lua</c> of Cheat Engine 7.7.0.10621. The property is published through LCL
///     RTTI and may read back as the <c>dup*</c> name; see <see cref="CEEnumNames" />.
/// </remarks>
public enum DuplicateHandling
{
    /// <summary>Drop the duplicate silently. CE: <c>dupIgnore</c>.</summary>
    Ignore = 0,

    /// <summary>Keep the duplicate. CE: <c>dupAccept</c>.</summary>
    Accept = 1,

    /// <summary>Raise an error. CE: <c>dupError</c>.</summary>
    Error = 2
}
