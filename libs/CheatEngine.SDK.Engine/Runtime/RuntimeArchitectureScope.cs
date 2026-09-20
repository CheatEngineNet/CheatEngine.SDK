namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>The process side to which an architecture constraint applies.</summary>
public enum RuntimeArchitectureScope : byte
{
    /// <summary>The contract does not state an architecture scope.</summary>
    Unknown = 0,

    /// <summary>The constraint applies to the Cheat Engine host process.</summary>
    CheatEngine = 1,

    /// <summary>The constraint applies to the currently selected target process.</summary>
    Target = 2,
}
