# Internal Lua direct-API boundary guard

`LuaDirectApiBoundaryGuardTests` is a repository build gate, not a consumer-facing Roslyn diagnostic. It scans the
SDK production layers for calls to raw `CheatEngine.SDK.Lua.Interop.Api.LuaApi` members classified in
[`protected-operations.json`](../../eng/lua-bridge/protected-operations.json) as requiring the native bridge.

The test resolves direct member access, `using static` imports, and aliases of the exact `LuaApi` type. It reports the
source file and line in its assertion message. The raw API facade itself remains exempt because it is the declaration
layer; native-integration fixture tests are likewise outside this production-source gate.

This gate deliberately has no `CESDKxxxx` identifier, code fix, analyzer release row, or NuGet consumer impact. Its
input is still being proven against CE 7.7 and Lua 5.3. It becomes a public analyzer only after the operation catalogue
and the supported source patterns are stable enough to avoid false positives for plugin authors.

The only structured conditional exception is `lua_pushcclosure(state, function, 0)`. Lua 5.3.6 has a light-C-function
fast path for zero upvalues. The repository gate recognizes it only in the audited
`LuaState.PushUncheckedFunction` implementation, only when the first argument is that instance's `Pointer`, and only
when the immediately preceding statement is `if (lua_checkstack(Pointer, 1) == 0) throw ...`. Any other source file,
method, state expression, intervening Lua call, nonzero count or nonconstant count remains a violation. This deliberately
narrow structural proof avoids a false claim that a general control-flow analysis has established stack capacity. A future
public rule needs a sound semantic stack-capacity proof before exposing any conditional exception to consumers.

The native bridge remains mandatory for every other catalogue entry marked `requiresBridge: true`, including string,
table, userdata and upvalue-closure allocation, raw table mutation, registry-reference creation/release, and
`lua_error`. This preserves the C11 `lua_pcallk` boundary so Lua `longjmp` cannot skip a managed frame.
