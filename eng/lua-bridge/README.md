# Protected Lua-operation catalogue

`protected-operations.json` is the versioned, machine-readable source of truth for the **currently supported**
operations of the C11 Lua-protection microkernel. It records the bridge opcode and capability, managed constant and
wrapper name, the native protected stack effect, error class, ownership, and CE/Lua provenance. It also contains the
conservative policy for direct calls to the raw `LuaApi` methods used by this boundary.

It is deliberately an engineering input, not a runtime file. A shipping plugin never opens this JSON file: source
generators, structural tests, and build-time validation consume it at compile/test time and emit constants or compare
the native/managed sides. The C bridge remains hand-written C11 because `lua_pcallk` must establish a native
`setjmp`/`longjmp` boundary. This catalogue must not generate C or turn a raising Lua operation into a managed reverse
callback.

## Contract

The operation entries are ordered by opcode. `bridgeContract.operationBitmap` is a 64-bit hexadecimal bitmap derived
from those opcodes, and lets a generator create the managed required-bitmap constant and tests verify the native
contract. `nativeEnum`, `managed.constant`, and `managed.wrapper` intentionally identify their current C and C#
counterparts so a validator can report drift rather than relying on positional assumptions.

`LuaProtectedOperationContract.Count` is the number of active catalogue entries, not the greatest opcode plus one.
Opcodes are permanent ABI identities and may become sparse after a versioned retirement; `RequiredBitmap` and
`IsDefined` remain the authoritative membership checks.

`stack` is relative to the Lua stack before the bridge's light C closure is pushed. Its `inputCount` therefore means
the values supplied as `lua_pcallk` arguments, not any private stack copies that a managed adapter temporarily adds.
On a protected-call failure, Lua consumes those arguments and leaves one error object; the recorded failure delta makes
that rule explicit.

`raises` uses the Lua 5.3 convention:

- `never` — the documented path has the `-` error marker and can be a direct primitive when all normal Lua stack
  preconditions hold.
- `memory` — the documented path may fail by memory error (`m`), therefore it must run below the native bridge.
- `any` — the operation includes an intentional `lua_error`, host code, or unresolved source conflict, so it must run
  below the native bridge.

`directApiPolicy` is intentionally conservative. It lists only the raw APIs this microkernel needs to classify. An
unlisted raw `LuaApi` method is **not** automatically approved for direct use; it needs its own evidence entry before a
future analyzer or generator can allow it.

The only conditional direct rule currently recorded is `lua_pushcclosure` with `upvalueCount == 0` after a successful
`lua_checkstack(state, 1)` immediately before on the same call path. Lua 5.3.6 implements that exact light-C-function case without a closure
allocation. The generic operation remains protected because nonzero upvalues can allocate.

The exact CE 7.7-identical Lua fixture additionally proves the useful failure behavior of `lua_checkstack(4096)` under
a rejecting allocator: it returns `0`, preserves a zero-height stack, and leaves the state reusable. This is recorded
as `ExactInstalledFile`, not `ObservedLive`: it validates the hash-identical fixture DLL and does not make a claim
about a running Cheat Engine process.

## Consumers and validation

The `CheatEngine.SDK.SourceGenerators.LuaBridgeContract` generator receives this file as an `AdditionalFile`, parses
it deterministically, and emits only the managed enum, operation count, bitmap, membership check, and diagnostics. It
must not inspect the installed CE filesystem, network, time, or environment. The C microkernel remains an independent
compilation unit and is verified against the generated data; it is not generated from JSON.

Before changing an opcode, wrapper name, bitmap, direct-call decision, stack effect, ownership, or provenance:

1. Update `protected-operations.json` and keep it valid against `protected-operations.schema.json`.
2. Update the hand-written C bridge and managed wrapper behavior together where the contract changed; never add a
   hand-written managed opcode declaration beside the generated projection.
3. Extend the generator/structural tests and run the native failure probe.
4. Rebuild the x64 bridge and update its checked-in binary only when its C source or xmake project changed.

The provenance records intentionally keep CE 7.7 host behavior distinct from generic Lua 5.3 behavior. In particular,
the `PushHostObject` failure path remains `any` until P0-LIVE-005 verifies the CE 7.7 pusher against the exact host.

Run the structural drift check from the repository root before handing the catalogue to a new generator or analyzer:

```powershell
pwsh eng/lua-bridge/Test-ProtectedOperationCatalog.ps1
```

The script derives the bitmap from the JSON, checks unique IDs/opcodes/symbols and provenance, then compares every
entry with the hand-written C enum/cases. It does not compile C and it is never loaded by the shipping runtime. The
managed projection is deliberately not checked through a hand-written `.cs` file: the
`CheatEngine.SDK.SourceGenerators.LuaBridgeContract` generator's
`CatalogEmissionTests.Catalog_operations_emit_a_numeric_sorted_enum_and_required_bitmap` test feeds this same JSON as
an `AdditionalText` and asserts the emitted enum and bitmap. That test is the CI-proof of the generated C# half.

Primary evidence: [Lua 5.3 manual](https://www.lua.org/manual/5.3/manual.html#4.8),
[Lua 5.3 `lapi.c`](https://www.lua.org/source/5.3/lapi.c.html),
[Lua 5.3 `lauxlib.c`](https://www.lua.org/source/5.3/lauxlib.c.html), and the pinned
[Cheat Engine `LuaClass.pas`](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaClass.pas).
