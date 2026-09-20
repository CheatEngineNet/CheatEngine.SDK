using System;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>Optional arguments of Cheat Engine's string-form <c>AOBScan</c> call.</summary>
/// <remarks>
///     <para>
///         CE 7.7.0.10621 accepts
///         <c>
///             AOBScan(aobstring, protectionflags OPTIONAL, alignmenttype OPTIONAL,
///             alignmentparam HALFOPTIONAL)
///         </c>
///         . The protection string is deliberately not parsed here: it is CE's compact
///         <c>X</c>/<c>W</c>/<c>C</c>/<c>+</c>/<c>-</c>/<c>*</c> grammar, and preserving it exactly avoids a managed
///         normalizer changing host semantics.
///     </para>
///     <para>
///         With no options the binding passes only the pattern. A protection string alone is passed as the second
///         argument. An alignment option passes all four positions; a missing protection string is an explicit Lua
///         <c>nil</c> so the alignment cannot shift into the wrong argument slot.
///     </para>
/// </remarks>
public readonly struct AobScanOptions : IEquatable<AobScanOptions>
{
    /// <summary>Initializes options that leave every CE optional argument absent.</summary>
    public AobScanOptions()
        : this(null, FastScanMethod.NotAligned, null)
    {
    }

    /// <summary>Initializes AOB scan options.</summary>
    /// <param name="protectionFlags">CE's optional protection flag string, or <see langword="null" /> to omit it.</param>
    /// <param name="alignmentMethod">The CE alignment rule.</param>
    /// <param name="alignmentParameter">
    ///     The divisor for <see cref="FastScanMethod.Aligned" /> or hexadecimal trailing digits for
    ///     <see cref="FastScanMethod.LastDigits" />; omitted for <see cref="FastScanMethod.NotAligned" />.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alignmentMethod" /> is not a defined CE value.</exception>
    /// <exception cref="ArgumentException">
    ///     A non-default alignment has no parameter, or an alignment parameter was supplied with no alignment rule.
    /// </exception>
    public AobScanOptions(string? protectionFlags, FastScanMethod alignmentMethod, string? alignmentParameter)
    {
        if (alignmentMethod is < FastScanMethod.NotAligned or > FastScanMethod.LastDigits)
            throw new ArgumentOutOfRangeException(nameof(alignmentMethod), alignmentMethod,
                "AOBScan accepts only the CE fsmNotAligned, fsmAligned, or fsmLastDigits alignment values.");

        if (alignmentMethod == FastScanMethod.NotAligned && alignmentParameter is not null)
            throw new ArgumentException("An alignment parameter requires an alignment method.",
                nameof(alignmentParameter));

        if (alignmentMethod != FastScanMethod.NotAligned && string.IsNullOrEmpty(alignmentParameter))
            throw new ArgumentException("A non-default AOB alignment method requires a non-empty parameter.",
                nameof(alignmentParameter));

        ProtectionFlags = protectionFlags;
        AlignmentMethod = alignmentMethod;
        AlignmentParameter = alignmentParameter;
    }

    /// <summary>Gets options that pass only the AOB pattern.</summary>
    public static AobScanOptions Default => default;

    /// <summary>Gets CE's protection string, or <see langword="null" /> when it is omitted.</summary>
    public string? ProtectionFlags { get; }

    /// <summary>Gets CE's alignment rule.</summary>
    public FastScanMethod AlignmentMethod { get; }

    /// <summary>Gets CE's alignment parameter, or <see langword="null" /> for no alignment.</summary>
    public string? AlignmentParameter { get; }

    /// <summary>Gets a value indicating whether the binding must pass CE's alignment argument positions.</summary>
    internal bool HasAlignment => AlignmentMethod != FastScanMethod.NotAligned;

    /// <inheritdoc />
    public bool Equals(AobScanOptions other)
    {
        return string.Equals(ProtectionFlags, other.ProtectionFlags, StringComparison.Ordinal) &&
               AlignmentMethod == other.AlignmentMethod &&
               string.Equals(AlignmentParameter, other.AlignmentParameter, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is AobScanOptions other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(ProtectionFlags, AlignmentMethod, AlignmentParameter);
    }

    /// <summary>Compares AOB scan options by their exact CE argument values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    public static bool operator ==(AobScanOptions left, AobScanOptions right)
    {
        return left.Equals(right);
    }

    /// <summary>Compares AOB scan options by their exact CE argument values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    public static bool operator !=(AobScanOptions left, AobScanOptions right)
    {
        return !left.Equals(right);
    }
}
