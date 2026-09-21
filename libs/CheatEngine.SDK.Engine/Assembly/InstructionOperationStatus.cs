namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>Describes the factual result of a bounded instruction operation.</summary>
/// <remarks>
///     The values intentionally distinguish a caller-supplied profile or destination failure from a missing CE
///     primitive, a protected Lua failure, and a CE result that does not match the pinned contract. No value turns
///     a successful PID-and-ISA observation into a process-incarnation guarantee, a target-selection lock, or live
///     host qualification.
/// </remarks>
public enum InstructionOperationStatus
{
    /// <summary>The operation completed and copied its complete result.</summary>
    Success,

    /// <summary>The instruction profile does not name a supported x86, x64, ARM32, or ARM64 architecture with its matching width.</summary>
    InvalidProfile,

    /// <summary>The input or returned address has bits outside the caller-declared profile width.</summary>
    AddressExceedsProfileWidth,

    /// <summary>Cheat Engine did not report a positive selected target process identifier.</summary>
    TargetNotSelected,

    /// <summary>
    ///     Cheat Engine reported a different selected target before and after an instruction profile observation or
    ///     operation.
    /// </summary>
    TargetChanged,

    /// <summary>The caller-owned destination cannot hold the complete byte result; no byte was written.</summary>
    DestinationTooSmall,

    /// <summary>The raw UTF-8 line exceeds the explicit maximum before the SDK decodes or publishes text.</summary>
    OutputTooLong,

    /// <summary>Cheat Engine rejected an otherwise well-formed assembly instruction.</summary>
    InstructionRejected,

    /// <summary>The required Cheat Engine Lua global was missing or was not callable.</summary>
    GlobalUnavailable,

    /// <summary>A protected Lua lookup, argument push, or call failed.</summary>
    LuaFailure,

    /// <summary>Cheat Engine returned a value whose type, table elements, or instruction length violated the contract.</summary>
    InvalidResult,
}
