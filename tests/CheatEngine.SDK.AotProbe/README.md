# CheatEngine.SDK.AotProbe

This small `net10.0` executable is the repository's **Native AOT publication probe** for the shipping SDK graph. It
references both `CheatEngine.SDK.Engine` and `CheatEngine.SDK.Hosting`, which transitively cover the six shipping
libraries, and publishes for the supported `win-x64` architecture.

It is deliberately not a Cheat Engine plugin. Native AOT produces a self-contained native executable/library model;
it does not demonstrate that Cheat Engine can load, host, or unload an AOT plugin. The program never enables a plugin,
does not attach to a process, and makes no live Lua call. `TrimmerRootAssembly` roots all six shipping assemblies,
while the executable exercises an `Address` value path and closes a representative generic callback type
(`LuaCallback<ProbeState>`) with its public methods retained. That makes a publish validate the complete shipping graph
and its trim/AOT diagnostics rather than only the two assembly names.

Run the probe from the repository root after restore:

```powershell
dotnet restore tests/CheatEngine.SDK.AotProbe/CheatEngine.SDK.AotProbe.csproj
dotnet publish tests/CheatEngine.SDK.AotProbe/CheatEngine.SDK.AotProbe.csproj -c Release --no-restore
```

The output is a standalone Windows x64 executable plus its PDB. A successful publish is evidence for analyzer and
publication compatibility only; it is not evidence for a CE-hosted Native AOT deployment.

The Windows CI pipeline publishes and executes this probe in its own `Native AOT publication probe` job. That gate
uses the same rebuilt C11 bridge asset as the normal build, test, and package jobs, but the executable does not call
the bridge or exercise a live Cheat Engine host.

The project has no external runtime dependency and is intentionally outside the solution's normal test execution. Its
scope is AOT validation, not benchmarks, live Cheat Engine tests, or packaging the production NuGet artifact.
