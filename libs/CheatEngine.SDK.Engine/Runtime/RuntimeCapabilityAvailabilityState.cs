namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>The observed state of an optional capability.</summary>
public enum RuntimeCapabilityAvailabilityState : byte
{
    /// <summary>The capability was not probed or cannot be determined by this binding.</summary>
    Unknown = 0,

    /// <summary>The capability was explicitly observed as callable for this runtime snapshot.</summary>
    Available = 1,

    /// <summary>The capability was explicitly observed as unavailable for this runtime snapshot.</summary>
    Unavailable = 2,
}
