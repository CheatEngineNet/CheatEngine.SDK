namespace CheatEngine.SDK.Lua.Registration;

/// <summary>Controls how a registration set treats an effective global value that already exists.</summary>
public enum LuaRegistrationCollisionPolicy
{
    /// <summary>Refuse the complete set before publication when any requested global has a non-<see langword="nil" /> value.</summary>
    RejectExisting = 0,

    /// <summary>
    ///     Replace existing values and retain them in the lease. Releasing the lease restores a retained value only while
    ///     the registered closure is still the effective global value.
    /// </summary>
    ReplaceExisting = 1,
}
