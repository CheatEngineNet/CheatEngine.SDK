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

    def test_committed_catalogue_is_valid(self) -> None:
        self.assert_valid(self.catalog)

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
        capabilities = catalog[VALIDATOR.CAPABILITIES_FILE]["capabilities"]
        for capability in capabilities:
            if capability["id"] == "classic.callback.process-watcher":
                capability["availability"] = "catalogued-only"
                break
        self.assert_invalid(catalog, "requires 'opaque' availability")

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


if __name__ == "__main__":
    unittest.main()
