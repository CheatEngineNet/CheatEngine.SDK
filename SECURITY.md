# Security policy

CheatEngine.SDK is a Windows x64 SDK for Cheat Engine 7.7 plugins: a NuGet package with managed libraries, Roslyn
analyzers and generators, MSBuild assets and a native protection bridge. This page says which versions receive security
fixes, how to report a vulnerability privately, and how to check that a package came from this repository.

## Supported versions

| Version                                  | Package                              | Security fixes                                                   |
|------------------------------------------|--------------------------------------|------------------------------------------------------------------|
| 1.0.x, the latest published release line | `CheatEngine.SDK` on nuget.org       | Yes, as a new 1.0.x patch release                                |
| 2.0.0 prereleases built from `main`      | CI artifacts and prerelease packages | On `main` only; a prerelease is never patched in place           |
| 0.1.0 to 0.2.1                           | the former `CESDK` package ID        | No; move to `CheatEngine.SDK` (see [CHANGELOG.md](CHANGELOG.md)) |

When a new release line ships, the previous line stops receiving fixes; the table is updated in the same pull request.

## Reporting a vulnerability

Report vulnerabilities privately through GitHub private vulnerability reporting:
<https://github.com/CheatEngineNet/CheatEngine.SDK/security/advisories/new>.

Never report a vulnerability in a public issue, discussion or pull request.

A report can be triaged fastest when it identifies the exact combination that was run:

- the `CheatEngine.SDK` version and its `contentHash`, both read from the plugin's `packages.lock.json` (never
  recomputed);
- the SHA-256 of `cheatengine-sdk-lua-bridge.dll` in the plugin output folder;
- the Cheat Engine build (for example `7.7.0.10621`) and the SHA-256 of the executable you started, usually
  `cheatengine-x86_64.exe`;
- the plugin load profile (managed plugin through hostfxr, historical CLR loader, or Native AOT) and the SHA-256 of
  `ce.runtimeconfig.json` when the managed route is used;
- the Windows version and the output of `dotnet --info`;
- the impact (what an attacker controls and what they gain) and a minimal reproduction.

Hashes can be read with PowerShell, for example
`Get-FileHash -Algorithm SHA256 "$env:ProgramFiles\Cheat Engine\cheatengine-x86_64.exe"`. Remove user names and private
paths from logs. Never attach Cheat Engine binaries, target binaries or raw debugger dumps; describe them by name,
version and hash instead.

## Scope

In scope:

- the `CheatEngine.SDK` package: its libraries, analyzers, source generators, MSBuild build assets and the bundled
  native protection bridge `cheatengine-sdk-lua-bridge.dll`;
- the bridge sources under `native/cheatengine-sdk-lua-bridge`;
- this repository's build, test and release workflows, including the provenance of published packages.

Out of scope:

- vulnerabilities in Cheat Engine itself: report them upstream at <https://github.com/cheat-engine/cheat-engine>;
- vulnerabilities in CheatEngine.Client: report them at
  <https://github.com/CheatEngineNet/CheatEngine.Client/security/advisories/new>;
- use of Cheat Engine or of a plugin against software or processes you are not authorized to inspect or modify.

## Response

- An acknowledgement within 7 days and a triage decision within 14 days, on a best-effort basis: the project has a
  single active maintainer.
- Fixes are coordinated through a GitHub Security Advisory on this repository, which credits the reporter unless they
  ask otherwise.
- A fix ships as a new package version with a `Security` entry in [CHANGELOG.md](CHANGELOG.md). The advisory is
  published once the fixed package is available on nuget.org.

## Verifying releases

Packages are built, tested, packed and published by the `release.yml` workflow of this repository from a version tag,
through NuGet trusted publishing (no long-lived API key), and the published package gets a GitHub build provenance
attestation. [RELEASING.md](RELEASING.md) describes the process.

A package therefore has two files with different hashes:

- the package attached to the GitHub release, which is the attested file:
  `gh attestation verify CheatEngine.SDK.<version>.nupkg --repo CheatEngineNet/CheatEngine.SDK`;
- the package served by nuget.org, which nuget.org re-signs with its repository signature, so its SHA-256 differs from
  the GitHub asset: `dotnet nuget verify --all CheatEngine.SDK.<version>.nupkg`.

The `contentHash` that NuGet writes into a consumer's `packages.lock.json` is computed without the repository
signature, so it identifies the package content independently of where it was downloaded.

## Binary files in this repository

Two binaries are committed on purpose; everything else is built from source.

- `native/cheat-engine/lua53-64.dll` is the unmodified 64-bit Lua library of a Cheat Engine 7.7 installation, SHA-256
  `C95DCDFA0F60F97B43D970D77FD1BB907AF4DE04B500A3C89A99600B20B35BD2` ([its README](native/cheat-engine/README.md)).
  Tests and benchmarks bind it so that they exercise the Lua a plugin binds in production; it is never packed.
- `native/cheatengine-sdk-lua-bridge/runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll` is the native protection
  bridge that the package ships. CI rebuilds it from its C source and `xmake.lua` on every run, checks that two builds
  are identical, and checks the committed DLL against the source fingerprint it exports. The weekly scheduled health
  workflow also rebuilds the bridge of the latest release tag and compares it with the released DLL.
