# CESDK.Lua.FailureProbe

Separate-process regression probe for Lua failures that would terminate the test runner if a native `longjmp` crossed a
managed frame. `CESDK.Lua.Tests` launches it with the bundled Cheat Engine Lua DLL and checks its exit code.

The probe replaces Lua's allocator temporarily and forces allocations to fail through string, table, userdata, raw-table,
reference, callback, and thunk operations. It also confirms that a raising `__gc` finalizer is caught by a protected native
allocation and that the state remains usable afterward. It is built by the solution and is not shipped.
