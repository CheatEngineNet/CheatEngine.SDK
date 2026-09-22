# CheatEngine.SDK.LiveProbe

`CheatEngine.SDK.LiveProbe` is a manually loaded **evidence harness**, not a unit-test project, sample plugin, package
asset, or normal CI input. It records CE 7.7 behaviours that fixture tests cannot establish: the raw managed bootstrap
argument, the disputed packed-record tail, `synchronize`, per-thread Lua states and registry sharing, external
`resetLuaState`, CE userdata, and callback cleanup on plugin disable.

## Safety boundary

The probe is deliberately inert unless all of these are true:

1. The process is x64 and its main executable is exactly CE `7.7.0.10621` x64, SHA-256
   `9727076DA50924E4A097B49A02155E4B34759269C3017FF31375364B8826EB4D`.
2. `CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT` exactly equals `I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET`.
3. `CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE` names an unexpired JSON manifest whose schema is `ce77-live-probe-v1`, repeats
   that acknowledgement and host hash, identifies a live target PID, marks it `disposable: true`, and gives the SHA-256
   of that target's currently running image.
4. After enable, CE's `getOpenedProcessID()` is exactly that manifest PID.

The harness never opens or selects a process, changes target memory, injects, starts a debugger, or executes arbitrary
target code. The one intentionally risky observation is an operator-authorized four-byte canary immediately after the
SDK's conservative packed 36-byte bootstrap record. It runs only during bootstrap after conditions 1–3 pass. It is
isolated to the CE process, not the target, but must still be run only with a disposable test setup. A canary value that
survives **does not prove allocation capacity** on its own; preserve all raw observations for review.

The harness does not write a result file: it logs raw values through `HostLog`/`OutputDebugString`, so capture it with a
debugger or DebugView and save the transcript outside the repository. Do not place installed CE binaries, target
binaries, manifests containing sensitive paths, or captured process memory in source control.

## Build and load

Build it manually; it is intentionally absent from `CheatEngine.SDK.slnx`, so ordinary SDK builds and CI never load or
run it.

```powershell
dotnet build tests/CheatEngine.SDK.LiveProbe/CheatEngine.SDK.LiveProbe.csproj -c Release
```

Keep the complete `artifacts/bin/CheatEngine.SDK.LiveProbe/release/` folder together when loading
`CheatEngine.SDK.LiveProbe.dll` in CE's plugin settings. It needs the SDK assemblies, `.deps.json`,
`.runtimeconfig.json` and `cheatengine-sdk-lua-bridge.dll` next to the plugin. Follow the CE/.NET runtime-host setup
requirements documented by [`CheatEngine.SDK.LivePlugin`](../CheatEngine.SDK.LivePlugin/README.md) before attempting a
live run.

Create a short-lived authorization file on a secure local volume. Substitute only the hash and PID of the disposable
program that the operator has deliberately launched and attached in CE:

```json
{
  "schema": "ce77-live-probe-v1",
  "acknowledgement": "I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET",
  "hostSha256": "9727076DA50924E4A097B49A02155E4B34759269C3017FF31375364B8826EB4D",
  "targetProcessId": 12345,
  "targetSha256": "UPPERCASE_SHA256_OF_THE_LIVE_DISPOSABLE_TARGET_IMAGE",
  "disposable": true,
  "expiresUtc": "2026-09-20T12:30:00Z"
}
```

Set both environment variables in the same process tree that starts CE. Check `ce77_live_probe_status()` immediately
after enabling. If it reports any denied gate, stop: none of the action commands should be used and no result is
evidence.

## Console protocol

Run every command from CE's Lua Engine and preserve the command, UTC time, returned text, DebugView transcript, CE
binary hash, target image hash, PID, architecture and manifest expiry with the result. Commands intentionally do not
guess a pass/fail conclusion.

| Command                                                                           | Observation                                                                                                                           | Operator action / interpretation                                                                                                                                                |
|-----------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `ce77_live_probe_status()`                                                        | Every raw bootstrap integer, tail-canary record, gate decision and prior outcome.                                                     | The second integer is reported as `opaqueSecondInt`; never label it size/version from this output alone.                                                                        |
| `ce77_live_probe_host_profile()`                                                  | One JSON identity record for the authorized CE host, loaded Lua module, adjacent bridge binary, plugin binary, and disposable target. | Save the returned JSON with the DebugView transcript outside the repository. An observed file is not a live qualification until the artifact is reviewed against the catalogue. |
| `ce77_live_probe_begin_synchronize()` then `ce77_live_probe_synchronize_status()` | Worker, thunk and nested-invoke managed thread IDs; return round-trip and propagated exception.                                       | Do not block the GUI; poll until completion. Compare IDs with the enable-thread log.                                                                                            |
| `ce77_live_probe_begin_lua_threads()` then `ce77_live_probe_lua_threads_status()` | GUI and worker `lua_State*` identities and a private raw-registry marker read by the worker.                                          | Do not execute other Lua for one second. This is a narrow observation, not permission for arbitrary concurrent Lua.                                                             |
| `ce77_live_probe_snapshot_before_reset()`                                         | State pointer, SDK epoch and reference slot before reset.                                                                             | Manually call CE's `resetLuaState()`; the harness never calls it.                                                                                                               |
| `ce77_live_probe_snapshot_after_reset()`                                          | State/epoch after reset and whether the old SDK reference pushed.                                                                     | Record raw outcome. External reset without a corresponding SDK notification remains unsupported.                                                                                |
| `ce77_live_probe_userdata()`                                                      | `type(getMainForm())` and `tostring(getMainForm())`.                                                                                  | This observes CE userdata without retaining it or invoking the host-object pusher.                                                                                              |
| `ce77_live_probe_prepare_callback_shutdown()`                                     | Installs a counter callback.                                                                                                          | Call `pcall(ce77_live_probe_callback_shutdown)` once; disable the plugin; call it again under `pcall`; then re-enable and collect `status()`.                                   |

The callback probe intentionally leaves the callback registered in `OnDisable`; `LuaRuntime.Detach` is responsible for
neutralizing it. Do not force-unload assemblies or use CE's process-killing actions to end a run. Disable the plugin,
close CE normally, delete the short-lived authorization manifest, and terminate only the disposable target through its
normal cleanup route.

## Scope and limitations

- `ce77_live_probe_host_profile()` and every protected command re-evaluate the authorization manifest, target image,
  and CE opened-process PID immediately before acting. This is a current-state check, not proof that CE did not select
  another target between observations; PID reuse by an identical executable is not distinguishable without an
  operator-supplied incarnation value, which the `ce77-live-probe-v1` manifest does not contain.
- The plugin does not implement the classic native plugin Type-6 popup callback. That callback belongs to the classic
  ABI and needs a separately compiled, header-pinned native probe after the CE 7.7 header/Pascal divergence has been
  resolved.
- The tail canary is a bounded experimental write, not a new ABI rule. The SDK production path continues to write only
  the packed 36-byte record.
- `synchronize` facts become evidence only when raw thread IDs, exception text, return value and re-entrancy outcome are
  captured from the pinned host. `inMainThread()` capture and disable-while-worker drain remain future controlled
  probes; this harness does not claim either result.
- This version observes an external `resetLuaState()` only. It must not be mistaken for the planned SDK-controlled
  reset/generation contract.
- The worker-and-registry observation is opt-in only. A distinct worker Lua pointer may be a coroutine sharing the main
  virtual machine, heap and registry, so it is not evidence of independent heaps or safe concurrent execution.
- No live test is invoked by `dotnet test`, normal CI, Release validation or packaging.

Result recording and evidence rules: [local qualification protocol](../../docs/qualification/local-protocol.md).
