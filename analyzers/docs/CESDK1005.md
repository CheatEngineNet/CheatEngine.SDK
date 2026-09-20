# CESDK1005: Plugin lifecycle callback must not be async void

Cheat Engine's enable and disable lifecycle is synchronous. `async void` returns control to the host before its
continuation finishes, so a continuation can run after disable, lose exceptions, or access a detached Lua state.

Keep `OnEnable` and `OnDisable` synchronous. If a feature genuinely waits for external work, expose an explicitly
tracked host operation with cancellation and lifecycle ownership instead of making the callback `async void`.
