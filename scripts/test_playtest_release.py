"""Consumer-visible refusal boundaries for the release coordinator."""
import copy
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location("playtest_release", Path(__file__).with_name("playtest-release.py"))
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)


class SteamReceiptTests(unittest.TestCase):
    def test_rejects_baseline_manifest_or_incomplete_upload(self):
        app = "Successfully finished AppID 5325920 build (BuildID 42)."
        depot = "Building depot 5325921, baseline manifest 99"
        metadata = '"appid" "5325920"\n"depotid" "5325921"\n"manifest" "99"\n'
        with self.assertRaises(release.ReleaseError):
            release.parse_steam_receipt(app, depot, metadata)
        with self.assertRaises(release.ReleaseError):
            release.parse_steam_receipt("Upload failed", "Success! New manifestID 99 created", metadata)

    def test_rejects_foreign_depot_and_mismatched_manifest(self):
        app = "Successfully finished AppID 5325920 build (BuildID 42)."
        depot = "Success! New manifestID 99 created"
        for metadata in (
            '"appid" "5325920"\n"depotid" "1001"\n"manifest" "99"\n',
            '"appid" "1000"\n"depotid" "5325921"\n"manifest" "99"\n',
            '"appid" "5325920"\n"depotid" "5325921"\n"manifest" "98"\n',
        ):
            with self.subTest(metadata=metadata), self.assertRaises(release.ReleaseError):
                release.parse_steam_receipt(app, depot, metadata)

    def test_rejects_ambiguous_or_overflowing_steam_identity(self):
        app = "Successfully finished AppID 5325920 build (BuildID 42)."
        depot = "Success! New manifestID 99 created"
        metadata = '"appid" "5325920"\n"depotid" "5325921"\n"manifest" "99"\n'
        with self.assertRaises(release.ReleaseError):
            release.parse_steam_receipt(app + app, depot, metadata)
        with self.assertRaises(release.ReleaseError):
            release.parse_steam_receipt(app.replace("42", str(2**32)), depot, metadata)
        with self.assertRaises(release.ReleaseError):
            release.parse_steam_receipt(app, depot.replace("99", str(2**64)), metadata.replace("99", str(2**64)))


class DeploymentReceiptTests(unittest.TestCase):
    def setUp(self):
        self.candidate = {
            "release_id": "steam-playtest-42", "version": "0.2.0-playtest.42",
            "source_revisions": {"gameplay": "a" * 40, "master": "b" * 40},
            "images": {"gameplay": "ghcr.io/binoui/sloparena-gameserver@sha256:" + "a" * 64,
                       "master": "ghcr.io/binoui/sloparena-masterserver@sha256:" + "b" * 64,
                       "migration": "ghcr.io/binoui/sloparena-masterserver-migrations@sha256:" + "c" * 64},
            "catalog_hash": "d" * 64, "master_endpoint": "https://master-test.example.com",
            "steam": {"app_id": 5325920, "depot_id": 5325921, "build_id": 42, "manifest_id": "99"},
            "schema": {"target_migration": "20260927000001_HashGameServerApiTokens",
                       "compatible_migrations": ["20260927000001_HashGameServerApiTokens"], "upgrade_from": []},
        }
        self.result = copy.deepcopy(self.candidate)
        self.result.update(outcome="success", schema="20260927000001_HashGameServerApiTokens",
                           services={"master": {"readiness": True}, "game": {"readiness": True}},
                           registration={"available": True, "registered": True, "hosts": [{"fresh": True}]})

    def test_rejects_other_release_source_image_or_steam_build(self):
        for field in ("release_id", "source_revisions", "images", "catalog_hash", "master_endpoint", "steam"):
            changed = copy.deepcopy(self.result)
            changed[field] = "another candidate"
            with self.subTest(field=field), self.assertRaises(release.ReleaseError):
                release.verify_deployment(self.candidate, changed)

    def test_rejects_unready_service_stale_registration_or_unknown_schema(self):
        unready = copy.deepcopy(self.result)
        unready["services"]["game"]["readiness"] = False
        stale = copy.deepcopy(self.result)
        stale["registration"]["hosts"][0]["fresh"] = False
        unregistered = copy.deepcopy(self.result)
        unregistered["registration"]["registered"] = False
        wrong_schema = copy.deepcopy(self.result)
        wrong_schema["schema"] = "new-unapproved-migration"
        for changed in (unready, stale, unregistered, wrong_schema):
            with self.subTest(changed=changed), self.assertRaises(release.ReleaseError):
                release.verify_deployment(self.candidate, changed)

    def test_rejects_malformed_readiness_and_registration_receipts(self):
        for field, value in (("services", []), ("registration", []),
                             ("registration", {"available": True, "registered": True, "hosts": ["fresh"]}),
                             ("registration", {"available": True, "registered": True, "hosts": []})):
            changed = copy.deepcopy(self.result)
            changed[field] = value
            with self.subTest(field=field, value=value), self.assertRaises(release.ReleaseError):
                release.verify_deployment(self.candidate, changed)
        with self.assertRaises(release.ReleaseError):
            release.verify_deployment(self.candidate, [])

    def test_refuses_when_the_only_fresh_registration_becomes_stale(self):
        # Historical rows survive restarts; they do not invalidate a current ack.
        self.result["registration"]["hosts"].append({"fresh": False})
        release.verify_deployment(self.candidate, self.result)
        self.result["registration"]["hosts"][0]["fresh"] = False
        with self.assertRaises(release.ReleaseError):
            release.verify_deployment(self.candidate, self.result)


class EnvironmentPolicyTests(unittest.TestCase):
    def setUp(self):
        self.environment = {
            "deployment_branch_policy": {"protected_branches": False, "custom_branch_policies": True},
            "protection_rules": [{"type": "required_reviewers",
                                  "reviewers": [{"type": "User", "reviewer": {"id": 42}}]}],
        }
        self.branches = {"total_count": 1, "branch_policies": [{"name": "main", "type": "branch"}]}

    def test_refuses_when_required_deployment_approval_is_removed(self):
        release.require_environment_policy("playtest-vps", self.environment, self.branches)
        self.environment["protection_rules"] = []
        with self.assertRaises(release.ReleaseError):
            release.require_environment_policy("playtest-vps", self.environment, self.branches)

    def test_refuses_unrestricted_wildcard_tag_or_additional_branches(self):
        unrestricted = copy.deepcopy(self.environment)
        unrestricted["deployment_branch_policy"] = None
        with self.assertRaises(release.ReleaseError):
            release.require_environment_policy("playtest-vps", unrestricted, self.branches)
        for rules in (
            {"total_count": 1, "branch_policies": [{"name": "*", "type": "branch"}]},
            {"total_count": 1, "branch_policies": [{"name": "main", "type": "tag"}]},
            {"total_count": 2, "branch_policies": [{"name": "main", "type": "branch"},
                                                  {"name": "feature", "type": "branch"}]},
        ):
            with self.subTest(rules=rules), self.assertRaises(release.ReleaseError):
                release.require_environment_policy("playtest-vps", self.environment, rules)


if __name__ == "__main__":
    unittest.main()
