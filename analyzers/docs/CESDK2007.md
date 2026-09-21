# CESDK2007: User member collides with a generated Lua binding identity

Lua bindings add members that are part of their ABI: ownership-aware and legacy registration methods, thunks, cached
globals, and the borrowed handle/marshaller members of a `[LuaClass]`. A source member with the same identity makes
generated code fail to compile.

Rename the user member or change the binding declaration. The rule reports the source declaration, never generated
output, and compares the actual SDK annotation symbols rather than matching an attribute name as text.
