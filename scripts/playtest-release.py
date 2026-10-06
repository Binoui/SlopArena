#!/usr/bin/env python3
"""Prepare, upload and deploy a pinned Steam Playtest candidate; never activate Steam."""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tarfile
import tempfile
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
APP_ID = 5325920
DEPOT_ID = 5325921
SHA = re.compile(r"[a-f0-9]{40}")
VERSION = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+-playtest\.[0-9]+")
PAYLOADS = ("manifest.json", "character.runtime.json", "poses.bin", "client.bindings")
MASTER_REPOSITORY = "Binoui/SlopArena-MasterServer"


class ReleaseError(RuntimeError):
    pass


def read_json(path: Path) -> dict:
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ReleaseError(f"Duplicate JSON field: {key}")
            result[key] = value
        return result
    value = json.loads(path.read_text(), object_pairs_hook=unique)
    if not isinstance(value, dict):
        raise ReleaseError("Release receipt must be a JSON object")
    return value


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n")


def require(value, condition: bool, message: str):
    if not condition:
        raise ReleaseError(message)
    return value


def validate_version(value: str) -> str:
    return require(value, isinstance(value, str) and len(value) <= 64 and VERSION.fullmatch(value) is not None,
                   "Version must be a bounded X.Y.Z-playtest.N identifier")


def validate_revision(value: str) -> str:
    return require(value, isinstance(value, str) and SHA.fullmatch(value) is not None,
                   "Source revision must be a full lowercase commit SHA")


def gh_json(route: str) -> dict:
    result = subprocess.run(["gh", "api", route], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
    if result.returncode:
        raise ReleaseError("Cannot read approved Master source; check MASTER_RELEASE_TOKEN repository access")
    return json.loads(result.stdout)


def require_environment_policy(name: str, environment: dict, branches: dict) -> None:
    policy = environment.get("deployment_branch_policy")
    require(policy, isinstance(policy, dict) and policy.get("protected_branches") is False
            and policy.get("custom_branch_policies") is True,
            f"Configure selected deployment branches for {name}, allowing only the main branch")
    rules = branches.get("branch_policies")
    require(rules, type(branches.get("total_count")) is int and branches["total_count"] == 1
            and isinstance(rules, list) and len(rules) == 1
            and rules[0].get("name") == "main" and rules[0].get("type") == "branch",
            f"{name} must allow exactly the main branch, not wildcard branches or tags")
    if name == "playtest-vps":
        protections = environment.get("protection_rules", [])
        require(protections, any(rule.get("type") == "required_reviewers" and rule.get("reviewers")
                                for rule in protections),
                "Configure at least one required reviewer for playtest-vps before deployment")


def check_environments(_args) -> None:
    for name in ("playtest-build", "playtest-vps"):
        route = f"repos/Binoui/SlopArena/environments/{name}"
        try:
            environment = gh_json(route)
            branches = gh_json(route + "/deployment-branch-policies?per_page=100")
        except ReleaseError as exc:
            raise ReleaseError(f"Create/configure {name} with main-only deployment branches before dispatch") from exc
        require_environment_policy(name, environment, branches)
    print("Main-only build/deploy environments and required VPS approval policy verified")


def resolve(args) -> None:
    validate_version(args.version)
    validate_revision(args.source_revision)
    master_main = validate_revision(gh_json(f"repos/{MASTER_REPOSITORY}/git/ref/heads/main")["object"]["sha"])
    master = validate_revision(args.master_revision) if args.master_revision else master_main
    if master != master_main:
        comparison = gh_json(f"repos/{MASTER_REPOSITORY}/compare/{master}...{master_main}")
        require(master, comparison.get("status") in ("ahead", "identical"),
                "Master source must be an approved main ancestor")
    release_id = f"steam-playtest-{args.run_id}"
    require(release_id, re.fullmatch(r"[1-9][0-9]{0,19}", args.run_id) is not None, "Invalid GitHub run identity")
    require(args.migration_from, not args.migration_from or re.fullmatch(r"[A-Za-z0-9_]+", args.migration_from),
            "Invalid approved migration source")
    output = {"version": args.version, "release_id": release_id, "game_revision": args.source_revision,
              "master_revision": master, "migration_from": args.migration_from}
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a") as stream:
            for key, value in output.items():
                stream.write(f"{key}={value}\n")
    print(json.dumps(output, sort_keys=True))


def checksum(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_client(receipt: dict, version: str, source_revision: str, endpoint: str, root: Path = ROOT) -> Path:
    public_fields = {"version", "sourceRevision", "catalogHash", "masterEndpoint", "outputRelativePath",
                     "packageIdentities", "sharedSha256", "steamApi64Sha256", "buildResult", "errors", "warnings"}
    require(receipt, isinstance(receipt, dict) and set(receipt) == public_fields,
            "Client receipt must contain exactly the approved public fields")
    require(receipt, type(receipt["warnings"]) is int and receipt["warnings"] >= 0
            and isinstance(receipt["packageIdentities"], list), "Malformed client build evidence")
    validate_version(version)
    validate_revision(source_revision)
    require(receipt, receipt.get("version") == version and receipt.get("sourceRevision") == source_revision,
            "Client version/source differs from the approved release")
    require(receipt, receipt.get("masterEndpoint") == endpoint and endpoint.startswith("https://"),
            "Client Master endpoint differs from the approved HTTPS host")
    require(receipt, receipt.get("buildResult") == "Succeeded" and type(receipt.get("errors")) is int and receipt["errors"] == 0,
            "Client build did not succeed without errors")
    require(receipt, isinstance(receipt.get("catalogHash"), str) and re.fullmatch(r"[a-f0-9]{64}", receipt["catalogHash"]) is not None,
            "Client canonical catalog identity is missing")
    relative = f"build/release/SlopArena-{version}"
    require(receipt, receipt.get("outputRelativePath") == relative, "Unexpected client output path")
    output = root / relative
    require(output, output.is_dir() and not output.is_symlink(), "Client artifact directory is missing or symlinked")
    for path in output.rglob("*"):
        require(path, not path.is_symlink(), "Client artifact contains a symlink")
        if path.is_file():
            forbidden = path.suffix.lower() in (".env", ".pem", ".key", ".pfx", ".pdb") or path.name.lower() in (
                "game.json", "server.json", "master.env", "migration.env", "postgres-password")
            forbidden |= "skeleton" in path.name.lower() or any("DoNotShip" in part or "BackUpThisFolder" in part for part in path.parts)
            require(path, not forbidden, "Client artifact contains private or developer-only files")
    require(output, (output / "SlopArena.exe").is_file(), "Windows player is missing")
    data = output / "SlopArena_Data"
    managed = data / "Managed"
    require(receipt, checksum(managed / "SlopArena.Shared.dll") == receipt.get("sharedSha256"), "Shipped Shared hash differs")
    require(receipt, checksum(data / "Plugins/x86_64/steam_api64.dll") == receipt.get("steamApi64Sha256"), "Shipped Steam native hash differs")
    require(receipt, endpoint.encode("utf-16le") in (managed / "Assembly-CSharp.dll").read_bytes(), "Compiled Master endpoint missing")
    require(receipt, version.encode() in (data / "globalgamemanagers").read_bytes(), "Packaged application version differs")
    staged = data / "StreamingAssets"
    require(staged, {p.name for p in staged.iterdir()} == {"arenas", "content-cooked"}, "Unexpected runtime staging content")
    cooked = root / "content-cooked"
    roster = read_json(cooked / "roster/manifest.json")
    paths = ["roster/manifest.json"]
    expected_identities = []
    for entry in roster["entries"]:
        package_id = entry["packageId"]
        require(package_id, re.fullmatch(r"[a-z][a-z0-9.-]*", package_id) is not None, "Unsafe roster package identity")
        package = staged / "content-cooked" / package_id
        require(package, {p.name for p in package.iterdir()} == set(PAYLOADS), "Unexpected packaged character files")
        paths.extend(f"{package_id}/{name}" for name in PAYLOADS)
        manifest = read_json(cooked / package_id / "manifest.json")
        expected_identities.append({"selector": entry["selector"], **{
            name: manifest[name] for name in ("packageId", "version", "sourceHash", "cookedContentHash", "packageHash")}})
    require(roster, {p.name for p in (staged / "content-cooked").iterdir()} == {"roster", *(e["packageId"] for e in roster["entries"])},
            "Packaged cooked root contains an unselected package or authoring input")
    require(roster, {p.name for p in (staged / "content-cooked/roster").iterdir()} == {"manifest.json"},
            "Unexpected roster payload")
    require(receipt, receipt["packageIdentities"] == sorted(expected_identities, key=lambda identity: identity["packageId"]),
            "Client receipt identities differ from the roster-selected shipped packages")
    for relative_path in paths:
        require(relative_path, (cooked / relative_path).read_bytes() == (staged / "content-cooked" / relative_path).read_bytes(),
                "Shipped cooked payload differs from the approved source")
    source_arenas = {p.name: p for p in (root / "data/arenas").glob("*.arena")}
    require(source_arenas, source_arenas and {p.name for p in (staged / "arenas").iterdir()} == set(source_arenas),
            "Packaged arena set differs")
    for name, original in source_arenas.items():
        require(name, original.read_bytes() == (staged / "arenas" / name).read_bytes(), "Packaged arena bytes differ")
    for source, destination in [(root / "CREDITS.md", output / "CREDITS.txt"), (root / "LICENSE", output / "LICENSE.txt")]:
        require(source, source.read_bytes() == destination.read_bytes(), "Release attribution missing or changed")
    for name in ("ArchivoBlack-OFL.txt", "SpaceMono-OFL.txt"):
        require(name, (root / "client/Unity/Assets/Fonts" / name).read_bytes() == (output / "LICENSES" / name).read_bytes(),
                "Font license missing or changed")
    return output


def parse_steam_receipt(app_log: str, depot_log: str, depot_vdf: str) -> dict:
    builds = re.findall(r"Successfully finished AppID 5325920 build \(BuildID ([1-9][0-9]*)\)", app_log)
    manifests = re.findall(r"Success! New manifestID ([1-9][0-9]*) created", depot_log)
    saved = re.findall(r'^\s*"manifest"\s*"([1-9][0-9]*)"\s*$', depot_vdf, re.MULTILINE)
    require(depot_vdf, re.findall(r'^\s*"appid"\s*"([0-9]+)"\s*$', depot_vdf, re.MULTILINE) == [str(APP_ID)]
            and re.findall(r'^\s*"depotid"\s*"([0-9]+)"\s*$', depot_vdf, re.MULTILINE) == [str(DEPOT_ID)],
            "Steam depot metadata belongs to a different application or depot")
    require(builds, len(builds) == 1 and len(manifests) == 1 and saved == manifests,
            "Steam did not report one matching successful app build and depot manifest")
    require(builds, int(builds[0]) <= 2**32 - 1 and int(manifests[0]) <= 2**64 - 1, "Steam identity out of range")
    return {"app_id": APP_ID, "depot_id": DEPOT_ID, "build_id": int(builds[0]), "manifest_id": manifests[0]}


def steam_logs(directory: Path) -> dict:
    return parse_steam_receipt((directory / f"app_build_{APP_ID}.log").read_text(),
                              (directory / f"depot_build_{DEPOT_ID}.log").read_text(),
                              (directory / f"depot_build_{DEPOT_ID}.vdf").read_text())


def upload(args) -> None:
    verify_client(read_json(args.client), args.version, args.source_revision, args.endpoint)
    require(None, os.environ.get("GITHUB_ACTIONS") == "true", "Upload helper is restricted to the isolated Actions checkout")
    username = os.environ.get("STEAM_BUILD_USER", "")
    encoded = os.environ.get("STEAM_CONFIG_VDF", "")
    require(username, re.fullmatch(r"[A-Za-z0-9_]+", username) is not None, "Set STEAM_BUILD_USER to the dedicated builder account")
    require(encoded, bool(encoded) and len(encoded) <= 2_000_000, "STEAM_CONFIG_VDF is missing or oversized")
    try:
        config = base64.b64decode("".join(encoded.split()), validate=True)
    except ValueError as exc:
        raise ReleaseError("STEAM_CONFIG_VDF must be base64-encoded authorized config.vdf") from exc
    require(config, bool(config), "STEAM_CONFIG_VDF decodes to an empty session")
    logs = ROOT / "build/steampipe/output"
    require(logs, not logs.exists(), "Steam output must be fresh; do not reuse another upload's logs")
    with tempfile.TemporaryDirectory(prefix="playtest-steam-", dir=os.environ.get("RUNNER_TEMP")) as temporary:
        home = Path(temporary)
        install = home / "Steam"
        install.mkdir()
        archive = home / "steamcmd.tar.gz"
        with urllib.request.urlopen("https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz", timeout=60) as response:
            with archive.open("wb") as stream:
                while chunk := response.read(1024 * 1024):
                    stream.write(chunk)
        with tarfile.open(archive) as stream:
            stream.extractall(install, filter="data")
        (install / "config").mkdir(exist_ok=True)
        session = install / "config/config.vdf"
        session.write_bytes(config)
        session.chmod(0o600)
        environment = os.environ.copy()
        environment.pop("STEAM_CONFIG_VDF", None)
        environment.update(HOME=str(home), STEAM_HOME=str(install), STEAMCMD=str(install / "steamcmd.sh"), STEAM_BUILD_USER=username)
        with (home / "private-upload.log").open("wb") as private_log:
            result = subprocess.run(["bash", str(ROOT / "scripts/steam-playtest.sh"), args.version, "--upload"],
                                    cwd=ROOT, env=environment, stdin=subprocess.DEVNULL, stdout=private_log,
                                    stderr=subprocess.STDOUT, timeout=1800)
        require(result, result.returncode == 0, "Steam upload failed; reauthorize the builder config.vdf locally and renew its secret")
        receipt = steam_logs(logs)
    write_json(args.output, receipt)
    print(json.dumps(receipt, sort_keys=True))


def validated_candidate(value: dict) -> dict:
    sys.path.insert(0, str(ROOT / "deploy/vps"))
    from ci_release import validate_candidate
    return validate_candidate(value)


def assemble(args) -> None:
    client = read_json(args.client)
    verify_client(client, args.version, args.source_revision, args.endpoint)
    steam = read_json(args.steam)
    candidate = {"release_id": args.release_id, "version": args.version,
                 "source_revisions": {"gameplay": args.source_revision, "master": args.master_revision},
                 "images": {"gameplay": args.game_image, "master": args.master_image, "migration": args.migration_image},
                 "catalog_hash": client["catalogHash"], "master_endpoint": client["masterEndpoint"],
                 "schema": {"target_migration": args.target_migration, "compatible_migrations": [args.target_migration],
                            "upgrade_from": [args.migration_from] if args.migration_from else []}, "steam": steam}
    write_json(args.output, validated_candidate(candidate))
    print(f"Candidate {args.release_id}: Steam BuildID {steam['build_id']}; manual default promotion required")


def verify_deployment(candidate: dict, result: dict) -> None:
    require(result, isinstance(result, dict), "VPS receipt must be an object")
    require(result, result.get("outcome") == "success", "VPS did not report successful deployment")
    for key in ("release_id", "source_revisions", "images", "catalog_hash", "master_endpoint", "steam"):
        require(result, result.get(key) == candidate[key], f"VPS {key} differs from the uploaded candidate")
    require(result, result.get("schema") in candidate["schema"]["compatible_migrations"], "VPS schema differs")
    services = result.get("services", {})
    require(services, isinstance(services, dict) and all(
        isinstance(services.get(service), dict) and services[service].get("readiness") is True
        for service in ("master", "game")), "Master/GameHost readiness is not verified")
    registration = result.get("registration", {})
    require(registration, isinstance(registration, dict), "VPS registration receipt must be an object")
    hosts = registration.get("hosts")
    require(registration, registration.get("available") is True and registration.get("registered") is True
            and isinstance(hosts, list) and hosts and all(isinstance(host, dict) for host in hosts)
            and any(host.get("fresh") is True for host in hosts),
            "VPS registration is not fresh")


def deploy(args) -> None:
    candidate = validated_candidate(read_json(args.candidate))
    key = os.environ.get("PLAYTEST_SSH_PRIVATE_KEY", "")
    known_hosts = os.environ.get("PLAYTEST_SSH_KNOWN_HOSTS", "")
    host = os.environ.get("PLAYTEST_SSH_HOST", "")
    user = os.environ.get("PLAYTEST_SSH_USER", "")
    port = os.environ.get("PLAYTEST_SSH_PORT", "22")
    require(key, bool(key) and bool(known_hosts), "Deployment key and pinned SSH known-hosts secret are required")
    require(host, re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.-]*", host) is not None, "Invalid approved SSH host")
    require(user, re.fullmatch(r"[a-z_][a-z0-9_-]*", user) is not None, "Invalid restricted deployment account")
    require(port, port.isdecimal() and 1 <= int(port) <= 65535, "Invalid SSH port")
    with tempfile.TemporaryDirectory(prefix="playtest-ssh-", dir=os.environ.get("RUNNER_TEMP")) as temporary:
        directory = Path(temporary)
        identity = directory / "identity"
        identity.write_text(key + "\n")
        identity.chmod(0o600)
        hosts = directory / "known_hosts"
        hosts.write_text(known_hosts + "\n")
        hosts.chmod(0o600)
        command = ["ssh", "-T", "-i", str(identity), "-p", port, "-o", "BatchMode=yes", "-o", "IdentitiesOnly=yes",
                   "-o", "StrictHostKeyChecking=yes", "-o", f"UserKnownHostsFile={hosts}", "-o", "ConnectTimeout=15",
                   f"{user}@{host}", "sloparena-release"]
        environment = {k: v for k, v in os.environ.items() if k not in ("PLAYTEST_SSH_PRIVATE_KEY", "PLAYTEST_SSH_KNOWN_HOSTS")}
        result = subprocess.run(command, input=json.dumps(candidate).encode(), stdout=subprocess.PIPE,
                                stderr=subprocess.DEVNULL, env=environment, timeout=5400)
        require(result, result.returncode == 0, "Restricted VPS deployment failed; inspect private host release events, not raw logs")
    require(result.stdout, len(result.stdout) <= 131072, "Oversized VPS response")
    receipt = json.loads(result.stdout)
    verify_deployment(candidate, receipt)
    with urllib.request.urlopen(candidate["master_endpoint"] + "/ready", timeout=20) as response:
        require(response, response.status == 200, "Public Master readiness failed")
    write_json(args.output, {"candidate": candidate, "deployment": receipt,
                             "steamDefaultActivated": False, "manualPromotionUrl": f"https://partner.steamgames.com/apps/builds/{APP_ID}"})
    print(f"VPS {candidate['release_id']} verified. Set Steam BuildID {candidate['steam']['build_id']} live manually.")


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    commands = result.add_subparsers(dest="command", required=True)
    commands.add_parser("check-environments").set_defaults(function=check_environments)
    source = commands.add_parser("resolve")
    source.add_argument("--version", required=True)
    source.add_argument("--source-revision", required=True)
    source.add_argument("--master-revision", default="")
    source.add_argument("--migration-from", default="")
    source.add_argument("--run-id", required=True)
    source.set_defaults(function=resolve)
    for name in ("verify-client", "upload", "assemble"):
        command = commands.add_parser(name)
        command.add_argument("--client", required=True, type=Path)
        command.add_argument("--version", required=True)
        command.add_argument("--source-revision", required=True)
        command.add_argument("--endpoint", required=True)
        if name != "verify-client":
            command.add_argument("--output", required=True, type=Path)
        if name == "assemble":
            for argument in ("release-id", "master-revision", "game-image", "master-image", "migration-image", "target-migration"):
                command.add_argument("--" + argument, required=True)
            command.add_argument("--steam", required=True, type=Path)
            command.add_argument("--migration-from", default="")
        command.set_defaults(function={"verify-client": lambda a: print(verify_client(read_json(a.client), a.version, a.source_revision, a.endpoint)),
                                       "upload": upload, "assemble": assemble}[name])
    deployed = commands.add_parser("deploy")
    deployed.add_argument("--candidate", required=True, type=Path)
    deployed.add_argument("--output", required=True, type=Path)
    deployed.set_defaults(function=deploy)
    return result


def main() -> int:
    try:
        args = parser().parse_args()
        args.function(args)
        return 0
    except (RuntimeError, ValueError, TypeError, KeyError, OSError, subprocess.TimeoutExpired) as exc:
        if isinstance(exc, ReleaseError):
            print(f"Release refused: {exc}", file=sys.stderr)
        else:
            print("Release failed: missing/malformed artifact or failed external operation; no deployment success recorded", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
