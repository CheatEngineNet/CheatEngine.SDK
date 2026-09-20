# CESDK2006: Lua annotation target cannot receive generated code

`[LuaClass]`, `[LuaMethod]`, and `[LuaProperty]` are generator contracts, not reflection hints. Their target must be a
readonly partial borrowed-handle struct, or a supported partial method/property of such a struct. Unsupported members
are diagnosed at their declaration rather than being silently ignored.

Use the exact signature and partial shape documented by the LuaBindings project. In particular, `LuaMethod` has no
`LuaState` parameter, and `LuaProperty` accessors must be bodyless partial accessors over a supported scalar type.
