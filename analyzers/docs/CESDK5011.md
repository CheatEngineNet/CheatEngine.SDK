# CESDK5011: First-found AOB scan is experimental

|                    |                                                                                 |
|--------------------|---------------------------------------------------------------------------------|
| Kind               | `[Experimental]` API gate (reported by the C# compiler, not by an SDK analyzer) |
| Default severity   | Error (compiler)                                                                |
| Enabled by default | Yes                                                                             |
| Code fix           | None                                                                            |
| Reported           | While typing and in build, at every use of the gated member                     |

In short: this scan returns whichever match Cheat Engine happens to find first. It is not the only match, not the
lowest match, and not a proof that the pattern is unique. Use it only when any single match will do.

## Cause

Your code calls `AobScanner.TryFindFirstFoundWithinBounds(string, AobScanBounds, AobScanOptions, CancellationToken)`,
which carries `[Experimental("CESDK5011")]`.

## Why it is experimental

The member switches on the one-result mode of Cheat Engine's `MemScan` (`setOnlyOneResult(true)`) and reads the address
with `getOnlyResult()` (`celua.txt` lines 2656-2657). Cheat Engine documents the address as the first result found and
documents no order. The C3 spike of 2026-09-22 (Lua-only, on the pinned `ce-7.7.0.10621-x64-managed-hostfxr` profile,
decision D4.6) saw the lowest in-module address in three runs out of three, which is an observation, not a contract. It
did not observe the no-match path of `getOnlyResult`, nor the SDK's session sequence for this mode (a found list created
before the scan and never initialized). Two further semantics make the result easy to misuse:

- the result is *some* match: using it for "require a single match", "first by address" or a range query silently
  drops every other match (audit F07: an exhaustive scan must never be replaced by an arbitrary first result);
- Cheat Engine's start bound is not byte-exact, so a match that begins just before the range can be reported; the SDK
  reports it as `AobFirstFoundOutcomeKind.FoundOutsideBounds`, which says nothing about whether an in-bounds match
  exists.

For exhaustive range and module scans, and for uniqueness, use the ungated
`AobScanner.TryScanWithinBounds(string, AobScanBounds, AobScanOptions, Span<Address>, CancellationToken)`: uniqueness
needs an exhausted domain (`InBoundsCountIsExact`) or a second in-bounds match.

## How to opt in

Suppress the diagnostic where any single match is acceptable, as narrowly as possible:

```csharp
#pragma warning disable CESDK5011 // Any match of this signature will do; uniqueness is not required.
AobFirstFoundResult first = AobScanner.TryFindFirstFoundWithinBounds(pattern, bounds, AobScanOptions.Default,
    cancellationToken);
#pragma warning restore CESDK5011
```

or for a whole project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);CESDK5011</NoWarn>
</PropertyGroup>
```

Handle `Found`, `NotFound` and the indeterminate `FoundOutsideBounds` separately.

## When the gate is removed

A Q29 C3 receipt on the pinned profile that records this member's call sequence and its no-match path retires this
identifier; it is never reused. Even then the semantics stay "first found, order unspecified".
