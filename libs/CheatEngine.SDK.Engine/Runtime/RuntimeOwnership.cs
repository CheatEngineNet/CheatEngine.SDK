namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>The ownership fact attached to a capability contract.</summary>
public enum RuntimeOwnership : byte
{
    /// <summary>The contract does not establish ownership.</summary>
    Unknown = 0,

    /// <summary>The capability transfers no native or Lua-owned resource.</summary>
    None = 1,

    /// <summary>The capability exposes a resource that remains owned by Cheat Engine or Lua.</summary>
    Borrowed = 2,

    /// <summary>The capability transfers a resource that the caller must release through its documented owner.</summary>
    Owned = 3,
}
