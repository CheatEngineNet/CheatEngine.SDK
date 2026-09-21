#!/usr/bin/env python3
"""Validate the source-indexed Cheat Engine extension-surface catalogue offline."""

import argparse
import copy
import hashlib
import json
import sys
from pathlib import Path


CATALOG_DIRECTORY = Path("documentations/CheatEngine.SDK/catalog")
DECLARATIONS_FILE = "ce-7.7.0.10621-x64.declarations.json"
CAPABILITIES_FILE = "ce-7.7.0.10621-x64.capabilities.json"
CONFLICTS_FILE = "ce-7.7.0.10621-x64.conflicts.json"
HOST_PROFILES_FILE = "ce-7.7.0.10621-x64.host-profiles.json"
ADVANCED_FAMILIES_FILE = "ce-7.7.0.10621-x64.advanced-families.json"
EXPECTED_CATALOG_ID = "cheat-engine-extension-surface"
EXPECTED_CLASSIC_SOURCE_SHA256 = "B6500DF1E94D7BB011B38E173B2603197B7A1F304496D751EDE82E57E36E532F"
EXPECTED_CLASSIC_SLOT_MANIFEST_SHA256 = "AFACA989C7F117FC90D7D18CA30D8C8CB5049972ED91AC80132C209BA085BA5C"
EXPECTED_CLASSIC_SLOT_GROUPS = {
    "direct-prefix-v1": 18,
    "hookable-pointer-indirect-suffix": 64,
    "borrowed-delphi-objects": 2,
    "extension-v2": 3,
    "extension-v3": 8,
    "extension-v4": 60,
    "extension-v5": 4,
}
EXPECTED_CALLBACK_IDS = {
    "classic.callback.address-list",
    "classic.callback.memory-view",
    "classic.callback.debug-event",
    "classic.callback.process-watcher",
    "classic.callback.function-pointer-change",
    "classic.callback.main-menu",
    "classic.callback.disassembler-context-click",
    "classic.callback.disassembler-context-popup",
    "classic.callback.disassembler-render-line",
    "classic.callback.auto-assembler",
}
EXPECTED_ADVANCED_FAMILY_IDS = {
    "advanced.auto-assembler",
    "advanced.dbvm",
    "advanced.debugger",
    "advanced.hashing",
    "advanced.hotkeys",
    "advanced.il2cpp",
    "advanced.mono",
    "advanced.remote-execution-injection",
    "advanced.speedhack",
    "advanced.structures",
    "advanced.timers",
    "advanced.ui-forms",
}
REQUIRED_CAPABILITY_FIELDS = {
    "id",
    "layer",
    "symbol",
    "declaration_refs",
    "interop",
    "thread_affinity",
    "ownership",
    "failure_shape",
    "availability",
    "qualification",
    "profile_ids",
    "conflict_ids",
}
REQUIRED_INTEROP_FIELDS = {"calling_convention", "parameter_widths", "result", "indirection"}
REQUIRED_OWNERSHIP_FIELDS = {"registration", "callback", "arguments"}
REQUIRED_CONFLICT_FIELDS = (
    "c_declaration",
    "pascal_declaration",
    "evidence",
    "resolution",
    "required_availability",
    "blocks_live_qualification",
)
REQUIRED_ADVANCED_FAMILY_FIELDS = {
    "id",
    "title",
    "owner",
    "scope",
    "host_prerequisites",
    "privilege_requirements",
    "inputs_results_cleanup",
    "failure_modes",
    "evidence_gap",
    "source_status",
    "source_refs",
    "dependencies",
    "support_axes",
    "qualification_gates",
    "adoption_decision",
    "availability",
    "qualification",
    "profile_ids",
}
REQUIRED_ADVANCED_OWNER_FIELDS = {"sdk", "client"}
REQUIRED_ADVANCED_SCOPE_FIELDS = {"authorization", "target_scope", "policy"}
REQUIRED_ADVANCED_INPUT_RESULT_CLEANUP_FIELDS = {"inputs", "result", "cleanup"}
REQUIRED_ADVANCED_SUPPORT_AXES = {
    "implementation",
    "artifact",
    "host",
    "live_qualification",
    "policy",
    "lifecycle_cleanup",
}
REQUIRED_ADVANCED_QUALIFICATION_GATES = {"fixture", "live", "negative", "cleanup"}
ALLOWED_AVAILABILITY = {
    "catalogued-only",
    "fixture-only",
    "implemented-with-known-contract-gap",
    "mapped",
    "not-a-support-claim",
    "opaque",
    "planned",
    "source-only",
    "unavailable",
}
ALLOWED_QUALIFICATION = {
    "build-only",
    "fixture-qualified",
    "not-executed",
    "not-qualified",
    "source-indexed-only",
    "unqualified",
}
ALLOWED_UNRESOLVED_CONFLICT_AVAILABILITY = {"opaque", "unavailable"}
ALLOWED_ADVANCED_AXIS_STATES = {
    "contract-not-approved",
    "not-identified",
    "not-observed",
    "not-qualified",
    "explicit-opt-in-required",
}
ALLOWED_ADVANCED_FAMILY_AVAILABILITY = {"unavailable"}


def load_catalog(root: Path) -> dict[str, object]:
    """Load the committed documents, returning an independent mutable graph for tests."""
    directory = root / CATALOG_DIRECTORY
    result: dict[str, object] = {}
    for file_name in (DECLARATIONS_FILE, CAPABILITIES_FILE, CONFLICTS_FILE, HOST_PROFILES_FILE, ADVANCED_FAMILIES_FILE):
        path = directory / file_name
        try:
            result[file_name] = json.loads(path.read_text(encoding="utf-8"))
        except FileNotFoundError:
            result[file_name] = {"_missing_file": str(path)}
        except json.JSONDecodeError as error:
            result[file_name] = {"_invalid_json": f"{path}: {error}"}
    return copy.deepcopy(result)


def validate_catalog(catalog: dict[str, object], repository_root: Path) -> list[str]:
    """Return all deterministic validation errors; never contact a host or a network endpoint."""
    errors: list[str] = []
    declarations = _document(catalog, DECLARATIONS_FILE, errors)
    capabilities_document = _document(catalog, CAPABILITIES_FILE, errors)
    conflicts_document = _document(catalog, CONFLICTS_FILE, errors)
    profiles_document = _document(catalog, HOST_PROFILES_FILE, errors)
    advanced_families_document = _document(catalog, ADVANCED_FAMILIES_FILE, errors)
    if errors:
        return errors

    for name, document in (
        (DECLARATIONS_FILE, declarations),
        (CAPABILITIES_FILE, capabilities_document),
        (CONFLICTS_FILE, conflicts_document),
        (HOST_PROFILES_FILE, profiles_document),
        (ADVANCED_FAMILIES_FILE, advanced_families_document),
    ):
        if document.get("schema_version") != 1:
            errors.append(f"{name}: schema_version must be 1.")
        if document.get("catalog_id") != EXPECTED_CATALOG_ID:
            errors.append(f"{name}: catalog_id must be {EXPECTED_CATALOG_ID!r}.")

    _validate_declarations(declarations, errors)
    profiles = _validate_profiles(profiles_document, errors)
    capabilities = _validate_capabilities(capabilities_document, profiles, repository_root, errors)
    conflicts, conflict_states = _validate_conflicts(conflicts_document, capabilities, declarations, errors)
    _validate_conflicted_capabilities(capabilities, conflicts, conflict_states, errors)
    _validate_examples(capabilities_document, capabilities, errors)
    _validate_advanced_families(advanced_families_document, repository_root, errors)
    return errors


def _document(catalog: dict[str, object], name: str, errors: list[str]) -> dict[str, object]:
    document = catalog.get(name)
    if not isinstance(document, dict):
        errors.append(f"{name}: document is missing or is not an object.")
        return {}
    missing = document.get("_missing_file")
    invalid = document.get("_invalid_json")
    if isinstance(missing, str):
        errors.append(f"{name}: required file is missing ({missing}).")
        return {}
    if isinstance(invalid, str):
        errors.append(f"{name}: invalid JSON ({invalid}).")
        return {}
    if not document:
        errors.append(f"{name}: document must be a non-empty object.")
        return {}
    return document


def _validate_declarations(document: dict[str, object], errors: list[str]) -> None:
    sources = document.get("sources")
    if not isinstance(sources, list) or len(sources) != 1 or not isinstance(sources[0], dict):
        errors.append("declarations: exactly one pinned classic source is required.")
    else:
        source = sources[0]
        if source.get("sha256") != EXPECTED_CLASSIC_SOURCE_SHA256:
            errors.append("declarations: cepluginsdk.h SHA-256 does not match the reviewed pinned source.")
        if source.get("revision") != "ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37":
            errors.append("declarations: classic source revision changed without an explicit catalogue update.")

    exported = document.get("classic_exported_functions")
    if not isinstance(exported, dict):
        errors.append("declarations: classic_exported_functions is required.")
        return
    slots = exported.get("slots")
    if not isinstance(slots, list):
        errors.append("declarations: slots must be an array.")
        return
    if exported.get("slot_count") != 159 or len(slots) != 159:
        errors.append("declarations: the classic ExportedFunctions table must contain exactly 159 slots.")

    group_counts: dict[str, int] = {}
    slot_names: set[str] = set()
    manifest: list[dict[str, object]] = []
    for index, slot in enumerate(slots):
        if not isinstance(slot, dict):
            errors.append(f"declarations: slot {index} is not an object.")
            continue
        symbol = slot.get("symbol")
        source = slot.get("source")
        declaration = slot.get("declaration")
        group = slot.get("group")
        if not isinstance(symbol, str) or not symbol:
            errors.append(f"declarations: slot {index} has no symbol.")
            continue
        if symbol in slot_names:
            errors.append(f"declarations: duplicate classic slot {symbol!r}.")
        slot_names.add(symbol)
        if not isinstance(source, dict) or source.get("source_id") != "ce-classic-c-header" or not _valid_locator(source):
            errors.append(f"declarations: slot {symbol!r} has no valid pinned source locator.")
        if not isinstance(declaration, str) or not declaration.endswith(";"):
            errors.append(f"declarations: slot {symbol!r} has no declaration text.")
        if group not in EXPECTED_CLASSIC_SLOT_GROUPS:
            errors.append(f"declarations: slot {symbol!r} has invalid group {group!r}.")
        else:
            group_counts[group] = group_counts.get(group, 0) + 1
        if slot.get("slot_width_bits_x64") not in (32, 64):
            errors.append(f"declarations: slot {symbol!r} has an invalid x64 slot width.")
        if not isinstance(slot.get("indirection"), str) or not isinstance(slot.get("owner"), str):
            errors.append(f"declarations: slot {symbol!r} lacks indirection or owner metadata.")
        if isinstance(source, dict) and isinstance(declaration, str):
            manifest.append({"symbol": symbol, "line": source.get("line_start"), "declaration": declaration})

    if group_counts != EXPECTED_CLASSIC_SLOT_GROUPS:
        errors.append(f"declarations: classic slot groups differ from {EXPECTED_CLASSIC_SLOT_GROUPS!r}.")
    digest = hashlib.sha256(json.dumps(manifest, separators=(",", ":"), ensure_ascii=True).encode("utf-8")).hexdigest().upper()
    if exported.get("slot_manifest_sha256") != EXPECTED_CLASSIC_SLOT_MANIFEST_SHA256 or digest != EXPECTED_CLASSIC_SLOT_MANIFEST_SHA256:
        errors.append("declarations: classic slot manifest no longer matches the reviewed 159-slot baseline.")
    required_slots = {"ReadProcessMemory", "GetAddressFromPointer", "GetLuaState", "MainThreadCall", "FixMem"}
    missing_slots = required_slots - slot_names
    if missing_slots:
        errors.append(f"declarations: required classic slots are absent: {sorted(missing_slots)!r}.")
    hook_slots = [slot for slot in slots if isinstance(slot, dict) and slot.get("group") == "hookable-pointer-indirect-suffix"]
    if any(slot.get("indirection") != "pointer-to-function-pointer-slot" for slot in hook_slots):
        errors.append("declarations: every hookable classic slot must remain pointer-to-function-pointer-slot.")


def _validate_profiles(document: dict[str, object], errors: list[str]) -> dict[str, dict[str, object]]:
    raw_profiles = document.get("profiles")
    profiles: dict[str, dict[str, object]] = {}
    if not isinstance(raw_profiles, list):
        errors.append("host profiles: profiles must be an array.")
        return profiles
    for profile in raw_profiles:
        if not isinstance(profile, dict) or not isinstance(profile.get("id"), str):
            errors.append("host profiles: every profile needs an id.")
            continue
        identifier = profile["id"]
        if identifier in profiles:
            errors.append(f"host profiles: duplicate profile id {identifier!r}.")
            continue
        profiles[identifier] = profile
        host = profile.get("host")
        target = profile.get("target")
        if not isinstance(host, dict) or host.get("architecture") not in {"x64"}:
            errors.append(f"host profiles: {identifier!r} must explicitly state the x64 host architecture.")
        if not isinstance(target, dict):
            errors.append(f"host profiles: {identifier!r} needs a distinct target object.")
            continue
        if target.get("architecture") == "same-as-host" or target.get("pointer_width_bits") == "same-as-host":
            errors.append(f"host profiles: {identifier!r} illegally infers target facts from the host.")
        if "architecture" not in target or "pointer_width_bits" not in target:
            errors.append(f"host profiles: {identifier!r} must state target architecture and pointer-width observation state.")
    return profiles


def _validate_capabilities(document: dict[str, object], profiles: dict[str, dict[str, object]], repository_root: Path,
                           errors: list[str]) -> dict[str, dict[str, object]]:
    raw_capabilities = document.get("capabilities")
    capabilities: dict[str, dict[str, object]] = {}
    if not isinstance(raw_capabilities, list):
        errors.append("capabilities: capabilities must be an array.")
        return capabilities
    for capability in raw_capabilities:
        if not isinstance(capability, dict):
            errors.append("capabilities: each entry must be an object.")
            continue
        identifier = capability.get("id")
        if not isinstance(identifier, str) or not identifier:
            errors.append("capabilities: each entry needs a non-empty id.")
            continue
        if identifier in capabilities:
            errors.append(f"capabilities: duplicate id {identifier!r}.")
            continue
        capabilities[identifier] = capability
        absent = REQUIRED_CAPABILITY_FIELDS - capability.keys()
        if absent:
            errors.append(f"capabilities: {identifier!r} is missing required fields {sorted(absent)!r}.")
        interop = capability.get("interop")
        if not isinstance(interop, dict) or REQUIRED_INTEROP_FIELDS - interop.keys():
            errors.append(f"capabilities: {identifier!r} has incomplete interop metadata.")
        ownership = capability.get("ownership")
        if not isinstance(ownership, dict) or REQUIRED_OWNERSHIP_FIELDS - ownership.keys():
            errors.append(f"capabilities: {identifier!r} has incomplete ownership metadata.")
        if capability.get("availability") not in ALLOWED_AVAILABILITY:
            errors.append(f"capabilities: {identifier!r} has unsupported availability state.")
        if capability.get("qualification") not in ALLOWED_QUALIFICATION:
            errors.append(f"capabilities: {identifier!r} has unsupported qualification state.")
        references = capability.get("declaration_refs")
        if not isinstance(references, list) or not references:
            errors.append(f"capabilities: {identifier!r} needs at least one declaration reference.")
        else:
            for reference in references:
                if not isinstance(reference, dict) or not _valid_locator(reference):
                    errors.append(f"capabilities: {identifier!r} has an invalid declaration locator.")
                    continue
                path = reference.get("path")
                if isinstance(path, str) and not path.startswith("Cheat Engine/") and not (repository_root / path).is_file():
                    errors.append(f"capabilities: {identifier!r} references missing SDK source {path!r}.")
        profile_ids = capability.get("profile_ids")
        if not isinstance(profile_ids, list):
            errors.append(f"capabilities: {identifier!r} profile_ids must be an array.")
            continue
        for profile_id in profile_ids:
            if profile_id not in profiles:
                errors.append(f"capabilities: {identifier!r} references unknown profile {profile_id!r}.")
        if capability.get("qualification") == "live-qualified":
            errors.append(f"capabilities: {identifier!r} cannot claim live qualification without a reviewed capture profile.")
        if capability.get("availability") == "planned" and profile_ids:
            errors.append(f"capabilities: planned {identifier!r} must not claim a host profile.")
        if capability.get("layer") in {"memory-scan", "target-memory"} and capability.get("availability") == "mapped":
            for profile_id in profile_ids:
                target = profiles[profile_id].get("target")
                if isinstance(target, dict) and target.get("architecture") not in {"x64", "x86", "arm64", "arm32"}:
                    errors.append(f"capabilities: target-dependent {identifier!r} cannot be mapped against an unobserved target profile.")

    callback_ids = {identifier for identifier, item in capabilities.items() if item.get("layer") == "classic-callback"}
    if callback_ids != EXPECTED_CALLBACK_IDS:
        errors.append("capabilities: exactly the nine PluginType families and ten callback slots must be catalogued.")
    return capabilities


def _validate_conflicts(document: dict[str, object], capabilities: dict[str, dict[str, object]],
                        declarations: dict[str, object], errors: list[str]) -> tuple[dict[str, dict[str, object]], dict[str, str]]:
    raw_conflicts = document.get("conflicts")
    conflicts: dict[str, dict[str, object]] = {}
    states: dict[str, str] = {}
    if not isinstance(raw_conflicts, list):
        errors.append("conflicts: conflicts must be an array.")
        return conflicts, states
    for conflict in raw_conflicts:
        if not isinstance(conflict, dict) or not isinstance(conflict.get("id"), str):
            errors.append("conflicts: every entry needs an id.")
            continue
        identifier = conflict["id"]
        if identifier in conflicts:
            errors.append(f"conflicts: duplicate id {identifier!r}.")
            continue
        conflicts[identifier] = conflict
        for required in REQUIRED_CONFLICT_FIELDS:
            if required not in conflict:
                errors.append(f"conflicts: {identifier!r} is missing {required!r}.")
        states[identifier] = _conflict_resolution_state(conflict, identifier, errors)

        evidence = conflict.get("evidence")
        if not isinstance(evidence, list) or not evidence:
            errors.append(f"conflicts: {identifier!r} needs at least one source locator.")
        elif any(not isinstance(locator, dict) or not _valid_locator(locator) for locator in evidence):
            errors.append(f"conflicts: {identifier!r} has an invalid source locator.")

        required_availability = conflict.get("required_availability")
        if not isinstance(required_availability, str) or required_availability not in ALLOWED_UNRESOLVED_CONFLICT_AVAILABILITY:
            errors.append(f"conflicts: {identifier!r} requires an opaque or unavailable availability state.")
        if states[identifier] == "unresolved" and conflict.get("blocks_live_qualification") is not True:
            errors.append(f"conflicts: unresolved {identifier!r} must block live qualification.")

        affected = conflict.get("affected_capability_ids")
        if not isinstance(affected, list):
            errors.append(f"conflicts: {identifier!r} affected_capability_ids must be an array.")
            continue
        for capability_id in affected:
            if not isinstance(capability_id, str) or not capability_id:
                errors.append(f"conflicts: {identifier!r} affected_capability_ids must contain non-empty strings.")
                continue
            if capability_id not in capabilities:
                errors.append(f"conflicts: {identifier!r} references unknown capability {capability_id!r}.")
                continue
            capability_conflicts = capabilities[capability_id].get("conflict_ids")
            if not isinstance(capability_conflicts, list) or identifier not in capability_conflicts:
                errors.append(f"conflicts: {identifier!r} is not reciprocated by capability {capability_id!r}.")

        if identifier == "classic.fixmem-null-host-slot":
            _validate_fixmem_conflict_locator(conflict, declarations, errors)
    required_conflicts = {
        "classic.type0-address-width",
        "classic.type3-return-and-width",
        "classic.type4-return-shape",
        "classic.type6-popup-bool-pointer-width",
        "classic.get-address-from-pointer-return-width",
        "classic.fixmem-null-host-slot",
    }
    absent = required_conflicts - conflicts.keys()
    if absent:
        errors.append(f"conflicts: mandatory historical conflicts are missing: {sorted(absent)!r}.")
    return conflicts, states


def _conflict_resolution_state(conflict: dict[str, object], identifier: str, errors: list[str]) -> str:
    """Return an unresolved-safe state while validating structured conflict resolution metadata."""
    resolution = conflict.get("resolution")
    if isinstance(resolution, str):
        if resolution.strip():
            return "unresolved"
        errors.append(f"conflicts: {identifier!r} needs a non-empty resolution summary.")
        return "unresolved"
    if not isinstance(resolution, dict) or set(resolution) != {"state", "summary"}:
        errors.append(f"conflicts: {identifier!r} resolution must be legacy prose or a state/summary object.")
        return "unresolved"
    state = resolution.get("state")
    summary = resolution.get("summary")
    if not isinstance(state, str) or state != "unresolved" or not isinstance(summary, str) or not summary.strip():
        errors.append(f"conflicts: {identifier!r} resolution must declare the unresolved state and a non-empty summary.")
    return "unresolved"


def _validate_fixmem_conflict_locator(conflict: dict[str, object], declarations: dict[str, object], errors: list[str]) -> None:
    """Require the FixMem conflict's C-header evidence to match its canonical slot locator."""
    expected = _classic_slot_locator(declarations, "FixMem")
    evidence = conflict.get("evidence")
    if expected is None or not isinstance(evidence, list) or not any(
            isinstance(locator, dict) and _same_locator(locator, expected) for locator in evidence):
        errors.append("conflicts: FixMem C-header evidence must match the FixMem declaration locator.")


def _classic_slot_locator(declarations: dict[str, object], symbol: str) -> dict[str, object] | None:
    """Find the canonical locator for one named classic export slot."""
    exported = declarations.get("classic_exported_functions")
    if not isinstance(exported, dict):
        return None
    slots = exported.get("slots")
    if not isinstance(slots, list):
        return None
    for slot in slots:
        if isinstance(slot, dict) and slot.get("symbol") == symbol and isinstance(slot.get("source"), dict):
            return slot["source"]
    return None


def _same_locator(left: dict[str, object], right: dict[str, object]) -> bool:
    """Compare the source identity and inclusive range of two source locators."""
    return all(left.get(field) == right.get(field) for field in ("source_id", "path", "line_start", "line_end"))


def _validate_conflicted_capabilities(capabilities: dict[str, dict[str, object]], conflicts: dict[str, dict[str, object]],
                                      conflict_states: dict[str, str], errors: list[str]) -> None:
    for capability_id, capability in capabilities.items():
        conflict_ids = capability.get("conflict_ids")
        if not isinstance(conflict_ids, list):
            continue
        for conflict_id in conflict_ids:
            conflict = conflicts.get(conflict_id)
            if conflict is None:
                errors.append(f"capabilities: {capability_id!r} references unknown conflict {conflict_id!r}.")
                continue
            affected_capability_ids = conflict.get("affected_capability_ids")
            if isinstance(affected_capability_ids, list) and capability_id not in affected_capability_ids:
                errors.append(f"capabilities: {capability_id!r} is not listed by conflict {conflict_id!r}.")
            if conflict_states.get(conflict_id) == "unresolved" and capability.get("availability") != conflict.get("required_availability"):
                errors.append(f"capabilities: unresolved conflict {conflict_id!r} requires {conflict.get('required_availability')!r} availability for {capability_id!r}.")
            if conflict_states.get(conflict_id) == "unresolved" and capability.get("qualification") == "live-qualified":
                errors.append(f"capabilities: {capability_id!r} cannot be live-qualified while {conflict_id!r} is unresolved.")


def _validate_examples(document: dict[str, object], capabilities: dict[str, dict[str, object]], errors: list[str]) -> None:
    examples = document.get("examples")
    if not isinstance(examples, list) or not examples:
        errors.append("capabilities: at least one source-indexed example is required.")
        return
    for example in examples:
        if not isinstance(example, dict) or example.get("capability_id") not in capabilities:
            errors.append("capabilities: every example must reference a known capability.")


def _validate_advanced_families(document: dict[str, object], repository_root: Path, errors: list[str]) -> None:
    families = document.get("families")
    if not isinstance(families, list):
        errors.append("advanced families: families must be an array.")
        return

    identifiers: set[str] = set()
    for family in families:
        if not isinstance(family, dict):
            errors.append("advanced families: every family must be an object.")
            continue
        identifier = family.get("id")
        if not isinstance(identifier, str) or not identifier:
            errors.append("advanced families: every family needs a non-empty id.")
            continue
        if identifier in identifiers:
            errors.append(f"advanced families: duplicate id {identifier!r}.")
        identifiers.add(identifier)
        absent = REQUIRED_ADVANCED_FAMILY_FIELDS - family.keys()
        if absent:
            errors.append(f"advanced families: {identifier!r} is missing required fields {sorted(absent)!r}.")

        owner = family.get("owner")
        if not isinstance(owner, dict) or REQUIRED_ADVANCED_OWNER_FIELDS - owner.keys() or any(
                not isinstance(value, str) or not value for value in owner.values()):
            errors.append(f"advanced families: {identifier!r} needs non-empty SDK and Client ownership statements.")
        scope = family.get("scope")
        if not isinstance(scope, dict) or REQUIRED_ADVANCED_SCOPE_FIELDS - scope.keys():
            errors.append(f"advanced families: {identifier!r} has incomplete scope metadata.")
        elif scope.get("authorization") != "local-authorized-process-only" or scope.get("policy") != "explicit-opt-in-required":
            errors.append(f"advanced families: {identifier!r} must preserve local authorization and explicit policy opt-in.")

        for field in ("host_prerequisites", "privilege_requirements", "failure_modes"):
            value = family.get(field)
            if not isinstance(value, list) or not value or any(not isinstance(item, str) or not item for item in value):
                errors.append(f"advanced families: {identifier!r} needs a non-empty {field} list.")
        inputs_results_cleanup = family.get("inputs_results_cleanup")
        if not isinstance(inputs_results_cleanup, dict) or REQUIRED_ADVANCED_INPUT_RESULT_CLEANUP_FIELDS - inputs_results_cleanup.keys() or any(
                not isinstance(value, str) or not value for value in inputs_results_cleanup.values()):
            errors.append(f"advanced families: {identifier!r} needs inputs, result, and cleanup boundaries.")

        if not isinstance(family.get("evidence_gap"), str) or not family.get("evidence_gap"):
            errors.append(f"advanced families: {identifier!r} needs an explicit evidence gap.")
        source_status = family.get("source_status")
        references = family.get("source_refs")
        if source_status not in {"pinned-call-path-located", "pinned-call-path-not-located"}:
            errors.append(f"advanced families: {identifier!r} has an invalid source status.")
        if not isinstance(references, list):
            errors.append(f"advanced families: {identifier!r} source_refs must be an array.")
        elif source_status == "pinned-call-path-located" and not references:
            errors.append(f"advanced families: {identifier!r} must retain a pinned source locator.")
        elif source_status == "pinned-call-path-not-located" and references:
            errors.append(f"advanced families: {identifier!r} cannot attach a locator it says was not located.")
        for reference in references if isinstance(references, list) else []:
            if not isinstance(reference, dict) or not _valid_locator(reference):
                errors.append(f"advanced families: {identifier!r} has an invalid source locator.")
                continue
            path = reference.get("path")
            if isinstance(path, str) and not path.startswith("Cheat Engine/") and not (repository_root / path).is_file():
                errors.append(f"advanced families: {identifier!r} references missing SDK source {path!r}.")

        dependencies = family.get("dependencies")
        if not isinstance(dependencies, list) or any(not isinstance(item, str) or not item for item in dependencies):
            errors.append(f"advanced families: {identifier!r} dependencies must be a string array.")
        axes = family.get("support_axes")
        if not isinstance(axes, dict) or set(axes) != REQUIRED_ADVANCED_SUPPORT_AXES:
            errors.append(f"advanced families: {identifier!r} must retain all independent support axes.")
        else:
            for axis, value in axes.items():
                if not isinstance(value, dict) or not isinstance(value.get("state"), str) or not isinstance(value.get("requirement"), str) or not value["requirement"]:
                    errors.append(f"advanced families: {identifier!r} axis {axis!r} needs a state and requirement.")
                elif value["state"] not in ALLOWED_ADVANCED_AXIS_STATES:
                    errors.append(f"advanced families: {identifier!r} cannot satisfy {axis!r} before independent review.")
        gates = family.get("qualification_gates")
        if not isinstance(gates, dict) or set(gates) != REQUIRED_ADVANCED_QUALIFICATION_GATES or any(
                not isinstance(value, list) or not value or any(not isinstance(item, str) or not item for item in value)
                for value in gates.values()):
            errors.append(f"advanced families: {identifier!r} needs non-empty fixture, live, negative, and cleanup gates.")
        decision = family.get("adoption_decision")
        if not isinstance(decision, dict) or decision.get("state") != "deferred" or decision.get("implementation_issue") != "not-created" or not isinstance(decision.get("reason"), str) or not decision["reason"]:
            errors.append(f"advanced families: {identifier!r} needs its own deferred adoption decision.")
        availability = family.get("availability")
        if not isinstance(availability, str) or availability not in ALLOWED_ADVANCED_FAMILY_AVAILABILITY or family.get("qualification") != "not-qualified" or family.get("profile_ids") != []:
            errors.append(f"advanced families: {identifier!r} remains unavailable and unqualified without a profile.")

    if identifiers != EXPECTED_ADVANCED_FAMILY_IDS:
        errors.append("advanced families: the independently gated family set does not match the reviewed SDK-020 partition.")


def _valid_locator(locator: dict[str, object]) -> bool:
    source_id = locator.get("source_id")
    path = locator.get("path")
    line_start = locator.get("line_start")
    line_end = locator.get("line_end")
    return isinstance(source_id, str) and bool(source_id.strip()) and isinstance(path, str) and bool(path.strip()) and type(line_start) is int and type(line_end) is int and line_start > 0 and line_end >= line_start


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="Repository root containing the catalogue.")
    arguments = parser.parse_args()
    root = arguments.root.resolve()
    errors = validate_catalog(load_catalog(root), root)
    if errors:
        print("CE extension-surface catalogue validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print("CE extension-surface catalogue validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
