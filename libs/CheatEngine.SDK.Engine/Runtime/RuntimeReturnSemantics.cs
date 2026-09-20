namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>The normal return shape attached to a capability contract.</summary>
public enum RuntimeReturnSemantics : byte
{
    /// <summary>The contract does not establish a return shape.</summary>
    Unknown = 0,

    /// <summary>The capability returns one value on success.</summary>
    Value = 1,

    /// <summary>The capability may have no value without that absence being a Lua call failure.</summary>
    OptionalValue = 2,

    /// <summary>The capability returns a Boolean status.</summary>
    BooleanStatus = 3,

    /// <summary>The capability has no normal return value.</summary>
    Void = 4,
}
