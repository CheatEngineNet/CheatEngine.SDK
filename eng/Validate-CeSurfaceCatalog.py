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


def load_catalog(root: Path) -> dict[str, object]:
    """Load the four committed documents, returning an independent mutable graph for tests."""
    directory = root / CATALOG_DIRECTORY
    result: dict[str, object] = {}
    for file_name in (DECLARATIONS_FILE, CAPABILITIES_FILE, CONFLICTS_FILE, HOST_PROFILES_FILE):
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
    if not all((declarations, capabilities_document, conflicts_document, profiles_document)):
        return errors

    for name, document in (
        (DECLARATIONS_FILE, declarations),
        (CAPABILITIES_FILE, capabilities_document),
        (CONFLICTS_FILE, conflicts_document),
        (HOST_PROFILES_FILE, profiles_document),
    ):
        if document.get("schema_version") != 1:
            errors.append(f"{name}: schema_version must be 1.")
        if document.get("catalog_id") != EXPECTED_CATALOG_ID:
            errors.append(f"{name}: catalog_id must be {EXPECTED_CATALOG_ID!r}.")

    _validate_declarations(declarations, errors)
    profiles = _validate_profiles(profiles_document, errors)
    capabilities = _validate_capabilities(capabilities_document, profiles, repository_root, errors)
    conflicts = _validate_conflicts(conflicts_document, capabilities, errors)
    _validate_conflicted_capabilities(capabilities, conflicts, errors)
    _validate_examples(capabilities_document, capabilities, errors)
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


def _validate_conflicts(document: dict[str, object], capabilities: dict[str, dict[str, object]], errors: list[str]) -> dict[str, dict[str, object]]:
    raw_conflicts = document.get("conflicts")
    conflicts: dict[str, dict[str, object]] = {}
    if not isinstance(raw_conflicts, list):
        errors.append("conflicts: conflicts must be an array.")
        return conflicts
    for conflict in raw_conflicts:
        if not isinstance(conflict, dict) or not isinstance(conflict.get("id"), str):
            errors.append("conflicts: every entry needs an id.")
            continue
        identifier = conflict["id"]
        if identifier in conflicts:
            errors.append(f"conflicts: duplicate id {identifier!r}.")
            continue
        conflicts[identifier] = conflict
        for required in ("c_declaration", "pascal_declaration", "evidence", "resolution", "required_availability", "blocks_live_qualification"):
            if required not in conflict:
                errors.append(f"conflicts: {identifier!r} is missing {required!r}.")
        affected = conflict.get("affected_capability_ids")
        if not isinstance(affected, list):
            errors.append(f"conflicts: {identifier!r} affected_capability_ids must be an array.")
            continue
        for capability_id in affected:
            if capability_id not in capabilities:
                errors.append(f"conflicts: {identifier!r} references unknown capability {capability_id!r}.")
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
    return conflicts


def _validate_conflicted_capabilities(capabilities: dict[str, dict[str, object]], conflicts: dict[str, dict[str, object]],
                                      errors: list[str]) -> None:
    for capability_id, capability in capabilities.items():
        conflict_ids = capability.get("conflict_ids")
        if not isinstance(conflict_ids, list):
            continue
        for conflict_id in conflict_ids:
            conflict = conflicts.get(conflict_id)
            if conflict is None:
                errors.append(f"capabilities: {capability_id!r} references unknown conflict {conflict_id!r}.")
                continue
            if conflict.get("resolution") == "unresolved" and capability.get("availability") != conflict.get("required_availability"):
                errors.append(f"capabilities: unresolved conflict {conflict_id!r} requires {conflict.get('required_availability')!r} availability for {capability_id!r}.")
            if conflict.get("blocks_live_qualification") and capability.get("qualification") == "live-qualified":
                errors.append(f"capabilities: {capability_id!r} cannot be live-qualified while {conflict_id!r} is unresolved.")


def _validate_examples(document: dict[str, object], capabilities: dict[str, dict[str, object]], errors: list[str]) -> None:
    examples = document.get("examples")
    if not isinstance(examples, list) or not examples:
        errors.append("capabilities: at least one source-indexed example is required.")
        return
    for example in examples:
        if not isinstance(example, dict) or example.get("capability_id") not in capabilities:
            errors.append("capabilities: every example must reference a known capability.")


def _valid_locator(locator: dict[str, object]) -> bool:
    return isinstance(locator.get("source_id"), str) and isinstance(locator.get("path"), str) and isinstance(locator.get("line_start"), int) and isinstance(locator.get("line_end"), int) and locator["line_start"] > 0 and locator["line_end"] >= locator["line_start"]


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
