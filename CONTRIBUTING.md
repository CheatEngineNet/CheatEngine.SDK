# Contributing to CheatEngine.SDK

Thanks for contributing. CheatEngine.SDK is a Windows x64, .NET 10 SDK for Cheat Engine 7.7 plugins. Keep changes
focused, preserve existing patterns, and update documentation when behavior or public APIs change.

## Prerequisites

- Windows x64
- .NET SDK 10.0.401, as pinned in [`global.json`](global.json)
- Git

Ordinary managed work uses the checked-in Lua bridge and does not need a C toolchain. If you change [
`native/cheatengine-sdk-lua-bridge`](native/cheatengine-sdk-lua-bridge/README.md), also install xmake and a Windows x64
C toolchain.

## Build and test

Run these commands from the repository root:

```powershell
dotnet restore CheatEngine.SDK.slnx
dotnet build CheatEngine.SDK.slnx -c Debug --no-restore
dotnet test --solution CheatEngine.SDK.slnx -c Debug --fail-skips on
dotnet test --solution CheatEngine.SDK.slnx -c Release --fail-skips on
```

To create the package locally:

```powershell
dotnet pack src/CheatEngine.SDK -c Release -o artifacts/nuget
```

For manual host validation, use the [live-plugin guide](tests/CheatEngine.SDK.LivePlugin/README.md).

## Continuous integration

Pull requests run `Pull request CI`, pushes to `main` run `Main CI`, and version tags run `Release`. All three call the
reusable [`ci.yml`](.github/workflows/ci.yml), which builds each thing once and passes it on as an artifact:

1. `native` rebuilds the Lua bridge twice to prove it is reproducible, checks the checked-in DLL against its source,
   and builds the classic ABI fixture facts.
2. `build-test` builds the solution once per configuration (Debug and Release) and runs every
   `tests/**/*.Tests` project in a single `dotnet test --solution` run. A skipped test fails both configurations.
   Debug also collects coverage and compares the ABI fixture facts with the managed layouts; Release packs the tested
   build as the `nuget-package` artifact, which you can download from the run.
3. `aot` publishes and runs the Native AOT probes.
4. `sonar` analyzes the code with SonarQube Cloud from the Debug coverage. It runs for branches of this repository only;
   fork and Dependabot pull requests skip it. The quality gate fails pull requests and is only reported on `main`.
5. `lint` runs actionlint on the workflows.

`CI / Gate` is the only required check: it fails when any job fails, and only `sonar` may be skipped. Drafts do not
run CI until they are marked ready for review. CodeRabbit reviews every pull request, but its findings and pre-merge
checks are advisory.

## Style and analyzers

- Follow [`.editorconfig`](.editorconfig): UTF-8, tab-based C# indentation (spaces only for continuation alignment), and
  two-space configuration/project-file indentation. [`.gitattributes`](.gitattributes) owns working-tree line-ending
  normalization.
- Use file-scoped namespaces, explicit accessibility, PascalCase public members, and the local naming patterns already
  present.
- Public APIs require XML documentation. Builds treat compiler and analyzer diagnostics as errors, except for configured
  exceptions.
- Do not use LINQ, including query expressions or `System.Linq` operators. Prefer explicit loops and collection APIs.
- Preserve native Lua identifiers and ABI layouts. Avoid unrelated refactors.

The package includes analyzers and code fixes. See the [diagnostic reference](analyzers/docs/README.md) for the `CESDK`
rules and their fixes.

## Branches and pull requests

1. Update your local `main`, then create a focused branch from it.
2. Make the smallest change that solves the problem.
3. Run the relevant build, test, and package commands.
4. Open a pull request against `main`; do not push directly to the protected branch.
5. Record consumer-visible changes under `[Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md).

Fill in the pull request template: state the problem, resulting behavior, validation commands and results, and any
remaining live-host limitations. Include documentation changes that the work requires. Pull requests are
squash-merged once `CI / Gate` passes, so the pull request title becomes the commit subject on `main`.

## Commits

Use focused commits with short, imperative subjects, such as `Fix CI validation findings`. Conventional Commit prefixes
are not required. Do not add `Co-authored-by` trailers.

## Releases

Versions are derived by MinVer from the nearest `v*` tag; the current minimum major/minor line is `1.0`, as configured
in [`Directory.Build.props`](Directory.Build.props). Pushing a valid `v<major>.<minor>.<patch>` tag (an optional SemVer
prerelease is allowed) starts the release workflow. It builds and tests the tag, waits for manual approval on the
`nuget` environment, publishes the tested package to NuGet, and creates a GitHub release whose notes are the
`CHANGELOG.md` section of that version. See [`RELEASING.md`](RELEASING.md).
