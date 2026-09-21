# Validation and Release Protocol

## Package validation versus product validation

Run `python eng/Validate-EngineeringManifest.py` from the repository root to validate the checked-in SDK manifest, work-item mirrors, roadmap links, declared cross-repository planning IDs, and local dependency DAG. Run `python -m unittest discover -s eng/tests -v` for the validator's synthetic regression tests. These commands do not contact GitHub and cannot validate deployed issue relationships, Project views, source compilation, or Cheat Engine behavior. Acceptance records begin with Not executed.

## Source and fixture gates

Recheck the exact call path before implementing an inherited audit observation. The process Try failure test must compose the production dispatcher behavior; a fake that catches a different exception cannot validate the seam. Runtime probes must distinguish missing global from a present throwing function. AOB must distinguish a qualified no-match shape from Lua/global/userdata failures. Retained codec contexts must fail before SDK entry after the invocation expires.

Inject failures before native mutation, after native success, before owner publication, during each cleanup stage and after mutation before snapshot refresh. Report partial effects and uncertain cleanup; do not assume false means nothing happened. Two target fixtures must prove that old owners never release into a newly selected process.

## Package and generated consumer gates

Use shipped packages in clean consumers, not sibling project references. Record content hashes and bridge/generator identity. Test minimum and selected later SDK versions; the version range is not a claim that every possible intermediate package was tested. Compile generated code and inspect public nested types rather than relying only on emitted text or shallow reflection tests.

## Live and performance gates

Record host executable, Lua/bridge identity, OS/architecture, target profile and exact plugin artifacts. Include enable/failure/disable/re-enable, target changes and simultaneous plugins. A standalone AOT executable publish is not a native plugin load/unload test. Explicitly retain unavailable support when the live gate is missing.

Benchmark direct SDK, immediate/queued Client, primitive/batch/codec/snapshot/AOB/event and cleanup paths separately. Record managed bytes, native/Lua measures when available, boundary-call counts and latency distributions. Preserve target, cancellation, outcome and ownership semantics. No performance values are supplied by this bootstrap.

## GitHub deployment verification

Read back each issue marker and milestone, all native parents and blocking edges, the two documentation branch trees and draft PRs. No Project verifier is supplied by this PR: membership, configured fields, saved-view names/layouts/filters, grouping, roadmap date-field binding and automation remain explicit operator checks. Preserve the receipt when a run stops. A partial run is not transactionally rolled back or relabeled successful.
