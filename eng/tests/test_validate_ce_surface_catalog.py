import copy
import importlib.util
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = REPOSITORY_ROOT / "eng" / "Validate-CeSurfaceCatalog.py"
SPECIFICATION = importlib.util.spec_from_file_location("validate_ce_surface_catalog", VALIDATOR_PATH)
if SPECIFICATION is None or SPECIFICATION.loader is None:
    raise RuntimeError("Could not load the CE surface catalogue validator.")
VALIDATOR = importlib.util.module_from_spec(SPECIFICATION)
SPECIFICATION.loader.exec_module(VALIDATOR)


class CeSurfaceCatalogValidationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.catalog = VALIDATOR.load_catalog(REPOSITORY_ROOT)

    def assert_valid(self, catalog: dict[str, object]) -> None:
        self.assertEqual([], VALIDATOR.validate_catalog(catalog, REPOSITORY_ROOT))

    def assert_invalid(self, catalog: dict[str, object], expected: str) -> None:
        errors = VALIDATOR.validate_catalog(catalog, REPOSITORY_ROOT)
        self.assertTrue(any(expected in error for error in errors), errors)

    def conflict(self, catalog: dict[str, object], identifier: str) -> dict[str, object]:
        conflicts = catalog[VALIDATOR.CONFLICTS_FILE]["conflicts"]
        for conflict in conflicts:
            if conflict["id"] == identifier:
                return conflict
        self.fail(f"Conflict {identifier!r} was not found.")

    def capability(self, catalog: dict[str, object], identifier: str) -> dict[str, object]:
        capabilities = catalog[VALIDATOR.CAPABILITIES_FILE]["capabilities"]
        for capability in capabilities:
            if capability["id"] == identifier:
                return capability
        self.fail(f"Capability {identifier!r} was not found.")

    def test_committed_catalogue_is_valid(self) -> None:
        self.assert_valid(self.catalog)

    def test_empty_catalogue_document_fails(self) -> None:
        for document_name in (
                VALIDATOR.DECLARATIONS_FILE,
                VALIDATOR.CAPABILITIES_FILE,
                VALIDATOR.CONFLICTS_FILE,
                VALIDATOR.HOST_PROFILES_FILE,
                VALIDATOR.ADVANCED_FAMILIES_FILE):
            with self.subTest(document=document_name):
                catalog = copy.deepcopy(self.catalog)
                catalog[document_name] = {}
                self.assert_invalid(catalog, f"{document_name}: document must be a non-empty object")

    def test_removing_a_classic_slot_fails(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        slots = catalog[VALIDATOR.DECLARATIONS_FILE]["classic_exported_functions"]["slots"]
        slots.pop()
        self.assert_invalid(catalog, "exactly 159 slots")

    def test_reordering_a_classic_slot_fails_manifest_integrity(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        slots = catalog[VALIDATOR.DECLARATIONS_FILE]["classic_exported_functions"]["slots"]
        slots[0], slots[1] = slots[1], slots[0]
        self.assert_invalid(catalog, "manifest no longer matches")

    def test_duplicate_capability_id_fails(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        capabilities = catalog[VALIDATOR.CAPABILITIES_FILE]["capabilities"]
        capabilities.append(copy.deepcopy(capabilities[0]))
        self.assert_invalid(catalog, "duplicate id")

    def test_unresolved_conflict_cannot_be_promoted(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        capability = self.capability(catalog, "classic.callback.process-watcher")
        capability["availability"] = "catalogued-only"
        self.assert_invalid(catalog, "requires 'opaque' availability")

    def test_unresolved_conflict_legacy_prose_cannot_be_promoted(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        conflict = self.conflict(catalog, "classic.type3-return-and-width")
        conflict["resolution"] = "unresolved; pending exact host proof"
        capability = self.capability(catalog, "classic.callback.process-watcher")
        capability["availability"] = "catalogued-only"
        self.assert_invalid(catalog, "requires 'opaque' availability")

    def test_unresolved_conflict_structured_state_cannot_be_promoted(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        conflict = self.conflict(catalog, "classic.type3-return-and-width")
        conflict["resolution"] = {"state": "unresolved", "summary": "Pending exact host proof."}
        capability = self.capability(catalog, "classic.callback.process-watcher")
        capability["availability"] = "catalogued-only"
        self.assert_invalid(catalog, "requires 'opaque' availability")

    def test_unknown_structured_conflict_state_fails_closed(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        conflict = self.conflict(catalog, "classic.type3-return-and-width")
        conflict["resolution"] = {"state": "resolved", "summary": "Unreviewed promotion."}
        capability = self.capability(catalog, "classic.callback.process-watcher")
        capability["availability"] = "catalogued-only"
        self.assert_invalid(catalog, "resolution must declare the unresolved state")
        self.assert_invalid(catalog, "requires 'opaque' availability")

    def test_conflict_capability_link_must_be_bidirectional(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        capability = self.capability(catalog, "classic.callback.process-watcher")
        capability["conflict_ids"] = []
        self.assert_invalid(catalog, "is not reciprocated by capability")

    def test_conflict_affected_capability_ids_non_list_fails_without_exception(self) -> None:
        for case, value in (
                ("missing", None),
                ("null", None),
                ("string", "classic.callback.process-watcher"),
                ("object", {})):
            with self.subTest(case=case):
                catalog = copy.deepcopy(self.catalog)
                conflict = self.conflict(catalog, "classic.type3-return-and-width")
                if case == "missing":
                    del conflict["affected_capability_ids"]
                else:
                    conflict["affected_capability_ids"] = value
                self.assert_invalid(catalog, "affected_capability_ids must be an array")

    def test_conflict_affected_capability_ids_rejects_non_string_entries(self) -> None:
        for value in ([], {}):
            with self.subTest(value=value):
                catalog = copy.deepcopy(self.catalog)
                conflict = self.conflict(catalog, "classic.type3-return-and-width")
                conflict["affected_capability_ids"] = [value]
                self.assert_invalid(catalog, "affected_capability_ids must contain non-empty strings")

    def test_conflict_required_availability_rejects_unhashable_value(self) -> None:
        for value in ([], {}):
            with self.subTest(value=value):
                catalog = copy.deepcopy(self.catalog)
                conflict = self.conflict(catalog, "classic.type3-return-and-width")
                conflict["required_availability"] = value
                self.assert_invalid(catalog, "requires an opaque or unavailable availability state")

    def test_fixmem_conflict_locator_must_match_declaration_catalogue(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        conflict = self.conflict(catalog, "classic.fixmem-null-host-slot")
        conflict["evidence"][0]["line_start"] = 289
        conflict["evidence"][0]["line_end"] = 289
        self.assert_invalid(catalog, "FixMem C-header evidence must match the FixMem declaration locator")

    def test_conflict_locator_rejects_boolean_line_numbers(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        conflict = self.conflict(catalog, "classic.type3-return-and-width")
        conflict["evidence"][0]["line_start"] = True
        conflict["evidence"][0]["line_end"] = True
        self.assert_invalid(catalog, "has an invalid source locator")

    def test_target_architecture_cannot_be_inferred_from_host(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        profiles = catalog[VALIDATOR.HOST_PROFILES_FILE]["profiles"]
        profiles[0]["target"]["architecture"] = "same-as-host"
        self.assert_invalid(catalog, "illegally infers target facts")

    def test_live_qualification_is_rejected_without_a_reviewed_capture(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        capabilities = catalog[VALIDATOR.CAPABILITIES_FILE]["capabilities"]
        capabilities[0]["qualification"] = "live-qualified"
        self.assert_invalid(catalog, "cannot claim live qualification")

    def test_advanced_family_cannot_drop_an_independent_support_axis(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        family = catalog[VALIDATOR.ADVANCED_FAMILIES_FILE]["families"][0]
        del family["support_axes"]["host"]
        self.assert_invalid(catalog, "all independent support axes")

    def test_advanced_family_cannot_be_promoted_from_its_ledger(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        family = catalog[VALIDATOR.ADVANCED_FAMILIES_FILE]["families"][0]
        family["availability"] = "mapped"
        self.assert_invalid(catalog, "remains unavailable and unqualified")

    def test_advanced_family_availability_rejects_unhashable_value(self) -> None:
        for value in ([], {}):
            with self.subTest(value=value):
                catalog = copy.deepcopy(self.catalog)
                family = catalog[VALIDATOR.ADVANCED_FAMILIES_FILE]["families"][0]
                family["availability"] = value
                self.assert_invalid(catalog, "remains unavailable and unqualified")

    def test_advanced_family_rejects_unknown_support_axis_state(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        family = catalog[VALIDATOR.ADVANCED_FAMILIES_FILE]["families"][0]
        family["support_axes"]["host"]["state"] = "review-complete"
        self.assert_invalid(catalog, "cannot satisfy 'host' before independent review")

    def test_advanced_family_requires_its_own_live_gate(self) -> None:
        catalog = copy.deepcopy(self.catalog)
        family = catalog[VALIDATOR.ADVANCED_FAMILIES_FILE]["families"][0]
        family["qualification_gates"]["live"] = []
        self.assert_invalid(catalog, "needs non-empty fixture, live, negative, and cleanup gates")


if __name__ == "__main__":
    unittest.main()
