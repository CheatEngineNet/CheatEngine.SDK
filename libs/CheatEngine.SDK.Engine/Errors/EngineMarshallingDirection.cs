namespace CheatEngine.SDK.Engine.Errors;

/// <summary>Identifies the direction of a value that could not cross the Engine/Lua boundary.</summary>
public enum EngineMarshallingDirection
{
    /// <summary>A managed argument could not be represented by the declared Lua contract.</summary>
    Argument = 0,

    /// <summary>A Lua result could not be represented by the declared managed contract.</summary>
    Result = 1
}
