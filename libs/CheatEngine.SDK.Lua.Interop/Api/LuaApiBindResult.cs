namespace CheatEngine.SDK.Lua.Interop.Api;

/// <summary>Outcome of one attempt to bind the function-pointer table; the caller maps it to an exception or a message.</summary>
internal enum LuaApiBindResult
{
    /// <summary>The table is bound to the requested module (now, or already before the call).</summary>
    Bound,

    /// <summary>The module handle was zero.</summary>
    NullModule,

    /// <summary>The module lacks at least one export of the table; nothing was changed.</summary>
    MissingExports,

    /// <summary>The table is already bound to another module; nothing was changed.</summary>
    BoundToAnotherModule
}
