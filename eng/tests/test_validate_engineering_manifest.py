"""Regression tests for the offline engineering-manifest verifier."""

from __future__ import annotations

import copy
import importlib.util
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = REPOSITORY_ROOT / "eng/Validate-EngineeringManifest.py"
SPECIFICATION = importlib.util.spec_from_file_location("engineering_manifest", VALIDATOR_PATH)
if SPECIFICATION is None or SPECIFICATION.loader is None:
    raise RuntimeError("Could not load the engineering manifest validator.")
VALIDATOR = importlib.util.module_from_spec(SPECIFICATION)
SPECIFICATION.loader.exec_module(VALIDATOR)


class EngineeringManifestValidationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.manifest = VALIDATOR.read_manifest(REPOSITORY_ROOT)

    def test_current_manifest_is_valid(self) -> None:
        errors = VALIDATOR.validate_manifest(self.manifest, REPOSITORY_ROOT)

        self.assertEqual([], errors)

    def test_duplicate_planning_id_is_rejected(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        manifest["items"].append(copy.deepcopy(manifest["items"][0]))

        errors = VALIDATOR.validate_manifest(manifest, REPOSITORY_ROOT)

        self.assertIn("Duplicate planning ID: SDK-PLAN.", errors)

    def test_non_object_manifest_root_is_rejected(self) -> None:
        errors = VALIDATOR.validate_manifest([], REPOSITORY_ROOT)

        self.assertEqual(["Manifest root must be an object."], errors)

    def test_invalid_collection_is_reported_without_iteration_failure(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        sdk_001 = next(item for item in manifest["items"] if item["id"] == "SDK-001")
        sdk_001["blocked_by"] = None

        errors = VALIDATOR.validate_manifest(manifest, REPOSITORY_ROOT)

        self.assertIn("SDK-001.blocked_by must be an array.", errors)

    def test_missing_external_reference_for_sdk_edge_is_rejected(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        manifest["external_references"] = [
            reference
            for reference in manifest["external_references"]
            if reference["id"] != "CLI-007"
        ]

        errors = VALIDATOR.validate_manifest(manifest, REPOSITORY_ROOT)

        self.assertIn("SDK-008.blocks references undeclared ID CLI-007.", errors)

    def test_local_id_cannot_be_declared_as_an_external_reference(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        manifest["external_references"].append(
            {
                "id": "SDK-001",
                "repository": "CheatEngineNet/CheatEngine.Client",
                "role": "invalid test fixture",
            }
        )

        errors = VALIDATOR.validate_manifest(manifest, REPOSITORY_ROOT)

        self.assertIn("Planning ID SDK-001 cannot be both local and external.", errors)

    def test_inconsistent_reverse_edge_is_rejected(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        sdk_002 = next(item for item in manifest["items"] if item["id"] == "SDK-002")
        sdk_002["blocks"].remove("SDK-004")

        errors = VALIDATOR.validate_manifest(manifest, REPOSITORY_ROOT)

        self.assertIn("SDK-004.blocked_by and SDK-002.blocks disagree.", errors)

    def test_missing_parent_reciprocity_is_rejected(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        sdk_e01 = next(item for item in manifest["items"] if item["id"] == "SDK-E01")
        sdk_e01["children"].remove("SDK-001")

        errors = VALIDATOR.validate_manifest(manifest, REPOSITORY_ROOT)

        self.assertIn("SDK-001.parent and SDK-E01.children disagree.", errors)

    def test_dependency_cycle_is_rejected(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        sdk_001 = next(item for item in manifest["items"] if item["id"] == "SDK-001")
        sdk_001["blocked_by"].append("SDK-002")
        sdk_002 = next(item for item in manifest["items"] if item["id"] == "SDK-002")
        sdk_002["blocks"].append("SDK-001")

        errors = VALIDATOR.validate_manifest(manifest, REPOSITORY_ROOT)

        self.assertTrue(any(error.startswith("Dependency cycle:") for error in errors))

    def test_missing_work_item_file_is_rejected(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        manifest["items"][0]["path"] = "documentations/engineering/work-items/missing.md"

        errors = VALIDATOR.validate_manifest(manifest, REPOSITORY_ROOT)

        self.assertIn(
            "SDK-PLAN.path does not exist: documentations/engineering/work-items/missing.md.",
            errors,
        )

    def test_work_item_path_cannot_escape_the_work_items_directory(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        manifest["items"][0]["path"] = "documentations/engineering/work-items/../../ROADMAP.md"

        errors = VALIDATOR.validate_manifest(manifest, REPOSITORY_ROOT)

        self.assertIn(
            "SDK-PLAN.path must resolve under documentations/engineering/work-items/.",
            errors,
        )

    def test_trace_identifiers_require_alphanumeric_token_boundaries(self) -> None:
        document = "R010 and T0010 are unrelated; R01 and T001 are traced."

        self.assertTrue(VALIDATOR.contains_trace_identifier(document, "R01"))
        self.assertTrue(VALIDATOR.contains_trace_identifier(document, "T001"))
        self.assertFalse(VALIDATOR.contains_trace_identifier("R010", "R01"))
        self.assertFalse(VALIDATOR.contains_trace_identifier("T0010", "T001"))


if __name__ == "__main__":
    unittest.main()
