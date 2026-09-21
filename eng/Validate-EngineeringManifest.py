"""Validate the checked-in engineering roadmap without contacting GitHub."""

from __future__ import annotations

import json
import re
import sys
from collections.abc import Iterable
from pathlib import Path
from typing import Any


MANIFEST_RELATIVE_PATH = Path("documentations/engineering/backlog.json")
ROADMAP_RELATIVE_PATH = Path("ROADMAP.md")
ARCHIVE_RECONCILIATION_RELATIVE_PATH = Path(
    "documentations/engineering/ARCHIVE_RECONCILIATION.md"
)
EXPECTED_SCHEMA_VERSION = 2
EXPECTED_ARCHIVE_SHA256 = "fc1f178916ec14fb993e405018112ddedd8dc316a86aa6fe9251b42c09547802"
REQUIRED_ITEM_FIELDS = (
    "id",
    "repository_key",
    "kind",
    "title",
    "parent",
    "children",
    "blocked_by",
    "blocks",
    "acceptance",
    "sources",
    "path",
    "body_template",
)


def read_manifest(repository_root: Path) -> object:
    """Read the versioned manifest and report malformed JSON as a validation error."""
    manifest_path = repository_root / MANIFEST_RELATIVE_PATH
    try:
        return json.loads(manifest_path.read_text(encoding="utf-8"))
    except FileNotFoundError as error:
        raise ValueError(f"Missing manifest: {MANIFEST_RELATIVE_PATH.as_posix()}") from error
    except json.JSONDecodeError as error:
        raise ValueError(f"Invalid manifest JSON: {error}") from error


def validate_manifest(manifest: object, repository_root: Path) -> list[str]:
    """Return every deterministic manifest error; this verifier intentionally stays offline."""
    if not isinstance(manifest, dict):
        return ["Manifest root must be an object."]

    errors: list[str] = []
    if manifest.get("schema_version") != EXPECTED_SCHEMA_VERSION:
        errors.append(
            f"schema_version must be {EXPECTED_SCHEMA_VERSION}, got {manifest.get('schema_version')!r}."
        )

    items = manifest.get("items")
    if not isinstance(items, list) or not items:
        return [*errors, "items must be a non-empty array."]

    item_by_id = collect_items(items, errors)
    external_reference_ids = collect_external_references(manifest, errors)
    validate_distinct_reference_ids(item_by_id, external_reference_ids, errors)
    validate_archive_context(manifest, repository_root, errors)
    validate_items(item_by_id, external_reference_ids, repository_root, errors)
    validate_dependency_graph(item_by_id, external_reference_ids, errors)
    validate_roadmap_and_reconciliation(item_by_id, repository_root, errors)
    return errors


def collect_items(items: Iterable[Any], errors: list[str]) -> dict[str, dict[str, Any]]:
    item_by_id: dict[str, dict[str, Any]] = {}
    for index, item in enumerate(items):
        if not isinstance(item, dict):
            errors.append(f"items[{index}] must be an object.")
            continue

        identifier = item.get("id")
        if not isinstance(identifier, str) or not identifier:
            errors.append(f"items[{index}].id must be a non-empty string.")
            continue
        if identifier in item_by_id:
            errors.append(f"Duplicate planning ID: {identifier}.")
            continue
        item_by_id[identifier] = item
    return item_by_id


def collect_external_references(manifest: dict[str, Any], errors: list[str]) -> set[str]:
    references = manifest.get("external_references")
    if not isinstance(references, list):
        errors.append("external_references must be an array.")
        return set()

    identifiers: set[str] = set()
    for index, reference in enumerate(references):
        if not isinstance(reference, dict):
            errors.append(f"external_references[{index}] must be an object.")
            continue
        identifier = reference.get("id")
        repository = reference.get("repository")
        role = reference.get("role")
        if not isinstance(identifier, str) or not identifier:
            errors.append(f"external_references[{index}].id must be a non-empty string.")
            continue
        if identifier in identifiers:
            errors.append(f"Duplicate external planning ID: {identifier}.")
        identifiers.add(identifier)
        if repository != "CheatEngineNet/CheatEngine.Client":
            errors.append(
                f"External reference {identifier} must identify CheatEngineNet/CheatEngine.Client."
            )
        if not isinstance(role, str) or not role:
            errors.append(f"External reference {identifier} must have a non-empty role.")
    return identifiers


def validate_distinct_reference_ids(
    item_by_id: dict[str, dict[str, Any]],
    external_reference_ids: set[str],
    errors: list[str],
) -> None:
    for identifier in sorted(item_by_id.keys() & external_reference_ids):
        errors.append(f"Planning ID {identifier} cannot be both local and external.")


def validate_archive_context(
    manifest: dict[str, Any], repository_root: Path, errors: list[str]
) -> None:
    review_context = manifest.get("review_context")
    if not isinstance(review_context, dict):
        errors.append("review_context must be an object.")
        return

    archive = review_context.get("archive")
    if not isinstance(archive, dict):
        errors.append("review_context.archive must be an object.")
    elif archive.get("sha256") != EXPECTED_ARCHIVE_SHA256:
        errors.append("review_context.archive.sha256 does not match the reviewed package receipt.")

    reconciliation = review_context.get("reconciliation_document")
    if reconciliation != ARCHIVE_RECONCILIATION_RELATIVE_PATH.as_posix():
        errors.append("review_context.reconciliation_document must point to ARCHIVE_RECONCILIATION.md.")
    elif not (repository_root / reconciliation).is_file():
        errors.append(f"Missing reconciliation document: {reconciliation}.")


def validate_items(
    item_by_id: dict[str, dict[str, Any]],
    external_reference_ids: set[str],
    repository_root: Path,
    errors: list[str],
) -> None:
    for identifier, item in item_by_id.items():
        for field in REQUIRED_ITEM_FIELDS:
            if field not in item:
                errors.append(f"{identifier} is missing required field '{field}'.")

        if item.get("repository_key") != "sdk":
            errors.append(f"{identifier}.repository_key must be 'sdk'.")
        validate_parent_and_children(identifier, item, item_by_id, errors)
        blocked_by = validate_string_collection(identifier, item, "blocked_by", errors)
        blocks = validate_string_collection(identifier, item, "blocks", errors)
        validate_string_collection(identifier, item, "acceptance", errors)
        validate_string_collection(identifier, item, "sources", errors)
        validate_work_item_file(identifier, item, repository_root, errors)

        for dependency in blocked_by:
            if dependency not in item_by_id and dependency not in external_reference_ids:
                errors.append(f"{identifier}.blocked_by references undeclared ID {dependency}.")
        for dependent in blocks:
            if dependent not in item_by_id and dependent not in external_reference_ids:
                errors.append(f"{identifier}.blocks references undeclared ID {dependent}.")


def validate_parent_and_children(
    identifier: str, item: dict[str, Any], item_by_id: dict[str, dict[str, Any]], errors: list[str]
) -> None:
    parent = item.get("parent")
    if parent is not None and (not isinstance(parent, str) or not parent):
        errors.append(f"{identifier}.parent must be a non-empty string or null.")
    elif parent is not None and parent not in item_by_id:
        errors.append(f"{identifier}.parent references undeclared ID {parent}.")
    elif parent is not None and identifier not in safe_string_collection(
        item_by_id[parent], "children"
    ):
        errors.append(f"{identifier}.parent and {parent}.children disagree.")

    children = validate_string_collection(identifier, item, "children", errors)
    for child in children:
        child_item = item_by_id.get(child)
        if child_item is None:
            errors.append(f"{identifier}.children references undeclared ID {child}.")
        elif child_item.get("parent") != identifier:
            errors.append(f"{identifier}.children and {child}.parent disagree.")


def validate_string_collection(
    identifier: str, item: dict[str, Any], field: str, errors: list[str]
) -> list[str]:
    values = item.get(field)
    if not isinstance(values, list):
        errors.append(f"{identifier}.{field} must be an array.")
        return []
    strings: list[str] = []
    for value in values:
        if not isinstance(value, str) or not value:
            errors.append(f"{identifier}.{field} must contain only non-empty strings.")
            return []
        strings.append(value)
    if len(strings) != len(set(strings)):
        errors.append(f"{identifier}.{field} contains duplicate IDs.")
    return strings


def safe_string_collection(item: dict[str, Any], field: str) -> list[str]:
    """Return a collection only when prior validation could safely iterate it."""
    values = item.get(field)
    if not isinstance(values, list):
        return []

    strings: list[str] = []
    for value in values:
        if not isinstance(value, str) or not value:
            return []
        strings.append(value)
    return strings


def validate_work_item_file(
    identifier: str, item: dict[str, Any], repository_root: Path, errors: list[str]
) -> None:
    relative_path = item.get("path")
    if not isinstance(relative_path, str) or not relative_path:
        errors.append(f"{identifier}.path must be a non-empty string.")
        return
    work_items_root = (repository_root / "documentations/engineering/work-items").resolve()
    item_path = (repository_root / relative_path).resolve()
    try:
        item_path.relative_to(work_items_root)
    except ValueError:
        errors.append(
            f"{identifier}.path must resolve under documentations/engineering/work-items/."
        )
        return

    if not item_path.is_file():
        errors.append(f"{identifier}.path does not exist: {relative_path}.")
        return
    contents = item_path.read_text(encoding="utf-8")
    marker = f"<!-- ce-bootstrap:{identifier} -->"
    heading = f"## {identifier} — {item.get('title', '')}"
    if marker not in contents:
        errors.append(f"{relative_path} is missing {marker}.")
    if heading not in contents:
        errors.append(f"{relative_path} does not match the manifest title for {identifier}.")


def validate_dependency_graph(
    item_by_id: dict[str, dict[str, Any]], external_reference_ids: set[str], errors: list[str]
) -> None:
    declared_external_edges: set[str] = set()
    for identifier, item in item_by_id.items():
        for dependent in safe_string_collection(item, "blocks"):
            if dependent in external_reference_ids:
                declared_external_edges.add(dependent)
            elif dependent in item_by_id and identifier not in item_by_id[dependent].get(
                "blocked_by", []
            ):
                errors.append(f"{identifier}.blocks and {dependent}.blocked_by disagree.")

        for dependency in safe_string_collection(item, "blocked_by"):
            if dependency in item_by_id and identifier not in item_by_id[dependency].get(
                "blocks", []
            ):
                errors.append(f"{identifier}.blocked_by and {dependency}.blocks disagree.")

    unused_external_references = external_reference_ids - declared_external_edges
    for identifier in sorted(unused_external_references):
        errors.append(f"External reference {identifier} has no SDK blocks edge.")
    find_dependency_cycles(item_by_id, errors)


def find_dependency_cycles(item_by_id: dict[str, dict[str, Any]], errors: list[str]) -> None:
    visiting: set[str] = set()
    visited: set[str] = set()

    def visit(identifier: str, trail: list[str]) -> None:
        if identifier in visiting:
            cycle_start = trail.index(identifier)
            cycle = trail[cycle_start:] + [identifier]
            errors.append(f"Dependency cycle: {' -> '.join(cycle)}.")
            return
        if identifier in visited:
            return
        visiting.add(identifier)
        trail.append(identifier)
        for dependency in safe_string_collection(item_by_id[identifier], "blocked_by"):
            if dependency in item_by_id:
                visit(dependency, trail)
        trail.pop()
        visiting.remove(identifier)
        visited.add(identifier)

    for identifier in item_by_id:
        visit(identifier, [])


def validate_roadmap_and_reconciliation(
    item_by_id: dict[str, dict[str, Any]], repository_root: Path, errors: list[str]
) -> None:
    roadmap_path = repository_root / ROADMAP_RELATIVE_PATH
    if not roadmap_path.is_file():
        errors.append(f"Missing roadmap: {ROADMAP_RELATIVE_PATH.as_posix()}.")
    else:
        roadmap = roadmap_path.read_text(encoding="utf-8")
        for item in item_by_id.values():
            if item.get("kind") in {"roadmap", "epic"}:
                continue
            relative_path = item.get("path")
            if isinstance(relative_path, str) and f"({relative_path})" not in roadmap:
                errors.append(f"ROADMAP.md does not link {relative_path}.")

    reconciliation_path = repository_root / ARCHIVE_RECONCILIATION_RELATIVE_PATH
    if not reconciliation_path.is_file():
        return
    reconciliation = reconciliation_path.read_text(encoding="utf-8")
    if EXPECTED_ARCHIVE_SHA256 not in reconciliation.lower():
        errors.append("ARCHIVE_RECONCILIATION.md does not contain the archive SHA-256.")
    for number in range(1, 37):
        identifier = f"R{number:02d}"
        if not contains_trace_identifier(reconciliation, identifier):
            errors.append(f"ARCHIVE_RECONCILIATION.md does not trace {identifier}.")
    for number in range(1, 81):
        identifier = f"T{number:03d}"
        if not contains_trace_identifier(reconciliation, identifier):
            errors.append(f"ARCHIVE_RECONCILIATION.md does not trace {identifier}.")


def contains_trace_identifier(document: str, identifier: str) -> bool:
    pattern = rf"(?<![A-Z0-9]){re.escape(identifier)}(?![A-Z0-9])"
    return re.search(pattern, document) is not None


def main() -> int:
    repository_root = Path(__file__).resolve().parents[1]
    try:
        manifest = read_manifest(repository_root)
    except ValueError as error:
        print(f"Engineering manifest validation failed: {error}", file=sys.stderr)
        return 1

    errors = validate_manifest(manifest, repository_root)
    if errors:
        print("Engineering manifest validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print(
        f"Validated {len(manifest['items'])} work items, "
        f"{len(manifest['external_references'])} external references, and the local dependency DAG."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
