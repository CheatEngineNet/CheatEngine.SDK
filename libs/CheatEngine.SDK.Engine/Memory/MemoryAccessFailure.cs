using System;

namespace CheatEngine.SDK.Engine.Memory;

/// <summary>Classifies a non-throwing Cheat Engine memory operation that did not complete successfully.</summary>
/// <remarks>
///     <para>
///         The failure is part of the memory API's stable result contract. A missing or unreadable target address is
///         different from a missing Lua global, a protected Lua error, or a value whose shape did not match the CE 7.7
///         contract. The <see cref="None" /> value is reported only when the operation returned <see langword="true" />.
///     </para>
///     <para>
///         A detached plugin is deliberately not represented here: every memory operation needs the ambient Lua runtime,
///         and therefore throws <see cref="InvalidOperationException" /> before it can contact Cheat Engine.
///     </para>
/// </remarks>
public enum MemoryAccessFailure
{
    /// <summary>The operation succeeded.</summary>
    None,

    /// <summary>The CE Lua global was absent, not a function, or could not be resolved under protection.</summary>
    GlobalUnavailable,

    /// <summary>The protected Lua call raised, including an allocation failure reported by the Lua protection bridge.</summary>
    LuaError,

    /// <summary>Cheat Engine returned <see langword="nil" /> or an incomplete byte table for a read operation.</summary>
    ReadFailed,

    /// <summary>
    ///     Cheat Engine returned a contiguous byte-table prefix that was shorter than the requested destination. This is
    ///     reported only by the overload that returns a copied byte count.
    /// </summary>
    PartialRead,

    /// <summary>The caller-provided destination was smaller than the Lua string returned by Cheat Engine.</summary>
    DestinationTooSmall,

    /// <summary>The caller requested a target-qualified pointer read without an observed target pointer width.</summary>
    PointerWidthUnknown,

    /// <summary>The pointer returned by Cheat Engine cannot fit in the caller's observed target pointer width.</summary>
    PointerValueExceedsTargetWidth,

    /// <summary>
    ///     Cheat Engine explicitly returned <see langword="false" /> for a scalar or string write, or reported a byte
    ///     count different from the requested non-empty byte write.
    /// </summary>
    WriteFailed,

    /// <summary>The Lua global returned a value of a different kind or outside the documented range.</summary>
    InvalidResult,
}
