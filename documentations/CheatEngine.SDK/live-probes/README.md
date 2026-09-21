# CheatEngine.SDK live-probe evidence

Live probes are opt-in validation against the documented CE 7.7.0.10621 x64 host. They complement, but never replace,
the managed test suite and bundled Lua fixture.

Record the host version and hash, plugin build and hash, target identity, exact procedure, result, relevant host log,
and cleanup outcome. A failed or incomplete run is evidence of a limitation, not permission to broaden a fixture
contract. See [`tests/CheatEngine.SDK.LiveProbe`](../../../tests/CheatEngine.SDK.LiveProbe/README.md) for the probe
artifact and setup. The controlled host/target capture contract is machine-readable in
[the extension-surface catalogue](../catalog/ce-7.7.0.10621-x64.host-profiles.json); captures are operator-retained
evidence, never ordinary CI artifacts.
