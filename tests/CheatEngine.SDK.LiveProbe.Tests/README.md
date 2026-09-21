# CheatEngine.SDK.LiveProbe.Tests

Deterministic unit tests for the manually loaded CE 7.7 LiveProbe evidence harness. The project compiles the four
evidence-only sources under test directly and supplies a local stub for the generated CE Lua global, so it does not load
the plugin or invoke its source generator. Tests inject only in-process authorization/PID and file-open results; they
never start Cheat Engine, load a CE host, select a target, or produce a live qualification artifact.
