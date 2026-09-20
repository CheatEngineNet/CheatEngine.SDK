# CheatEngine.SDK.Lua.FailureProbe

Separate-process regression probe for Lua failures that would terminate the test runner if a native `longjmp` crossed a
managed frame. `CheatEngine.SDK.Lua.Tests` launches it with the bundled Cheat Engine Lua DLL and checks its exit
code.

The probe replaces Lua's allocator temporarily and forces allocations to fail through string, table, userdata,
all three raw-table write forms, reference, callback, and thunk operations. Each failure starts with a lower stack
sentinel and proves the documented error/result values or preserved table are above it; it then proves exact restoration
to that sentinel and clears it. It also confirms that a raising `__gc` finalizer is caught by a protected native
allocation. Finally, it routes the native raising helper `luaL_checkinteger` through the host-object-pusher operation
with zero Lua inputs to prove that `lua_pcallk` catches the non-local exit and restores the pre-existing stack. Before emitting its pass marker, the child executes a
new protected Lua call and checks its result, proving the state and process remained usable after every failure. The
probe is built by the solution and is not shipped.

The first release of an SDK-private `LuaRef` is also exercised while allocation is rejected. It accepts either an
allocation-free `LUA_OK` release or a protected `LUA_ERRMEM`; both outcomes must invalidate the managed reference,
restore the existing Lua stack and permit a fresh private reference to be created, read and released after allocator
recovery. This records the fixture behavior without assuming that Lua's free-list bookkeeping always allocates.

Its `--checkstack-growth` mode is a narrower crash-boundary regression probe. It invokes the current direct managed
`lua_checkstack` binding while the fixture Lua allocator rejects a 4,096-slot growth request. The child emits flushed
markers before and after the call, requires the documented `0` result with an unchanged stack, then proves the same
request and a protected Lua call succeed after allocator recovery. It also fills the reserved stack one scalar at a
time until that direct check returns `0`, then calls the bridge wire export directly with the valid zero-input
`PushBytes` operation. The bridge must return its `NoErrorStatus` sentinel without changing the full stack; the child
then restores the stack and verifies recovery. The mode stays out of process because a regression that lets a native
non-local exit cross the managed call frame must never terminate the main test process.
