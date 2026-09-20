# CESDK2005: Lua function name is duplicated

Two otherwise valid `[LuaFunction]` methods in the same containing type cannot export the same Lua name. The
registration table would have no safe deterministic choice, so the generator emits neither conflicting thunk.

Give one method a distinct Lua name or place it in a different binding type. This is the only Lua diagnostic that runs
at compilation end because it must compare sibling declarations.
