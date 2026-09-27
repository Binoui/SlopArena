#!/usr/bin/env python3
"""Back up the VPS PostgreSQL database to S3 and restore into an isolated container."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlsplit

import release

BUCKET_NAME = re.compile(r"^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$")
REGION = re.compile(r"^[a-z0-9-]{2,32}$")
PREFIX = "postgres/"
KEY = re.compile(r"^postgres/[A-Za-z0-9_-][A-Za-z0-9._-]*\.dump$")
DAILY_DUMP = re.compile(r"^\d{8}T\d{6}Z-[0-9a-f]{32}\.dump$")


def daily_dumps(backup_dir: Path) -> list[Path]:
    return sorted(
        [path for path in backup_dir.glob("*.dump") if DAILY_DUMP.fullmatch(path.name)]
        + [path for path in (backup_dir / "daily").glob("*.dump") if DAILY_DUMP.fullmatch(path.name)],
        key=lambda path: path.name,
        reverse=True,
    )


def prune_daily_dumps(backup_dir: Path, keep: int) -> None:
    for old in daily_dumps(backup_dir)[keep:]:
        old.unlink()
        old.with_name(old.name + ".json").unlink(missing_ok=True)


def command(argv: list[str], *, stdout=None, env=None) -> bytes:
    proc = subprocess.run(argv, stdout=stdout if stdout is not None else subprocess.PIPE,
                          stderr=subprocess.PIPE, env=env, check=False)
    if proc.returncode:
        # Neither curl's configuration nor credentials are passed in argv. Avoid echoing remote error bodies.
        raise release.ReleaseError(f"{argv[0]} failed with exit status {proc.returncode}")
    return proc.stdout if stdout is None else b""


def load_storage(path: Path) -> dict[str, str]:
    if not path.is_absolute() or not path.is_file() or path.stat().st_mode & 0o077:
        raise release.ReleaseError("storage config must be an absolute private file (mode 0600)")
    try:
        config = json.loads(path.read_text())
    except (ValueError, UnicodeError) as exc:
        raise release.ReleaseError("invalid storage config JSON") from exc
    if not isinstance(config, dict) or set(config) != {"bucket", "endpoint", "region"}:
        raise release.ReleaseError("storage config requires bucket, endpoint and region")
    bucket, endpoint, region = (config[field] for field in ("bucket", "endpoint", "region"))
    if not all(isinstance(value, str) for value in (bucket, endpoint, region)):
        raise release.ReleaseError("invalid storage config")
    url = urlsplit(endpoint)
    if (not BUCKET_NAME.fullmatch(bucket) or not REGION.fullmatch(region) or
        url.scheme != "https" or not url.hostname or url.username or url.password or
        url.path not in ("", "/") or url.query or url.fragment):
        raise release.ReleaseError("invalid storage bucket, region or HTTPS endpoint")
    return {"bucket": bucket, "endpoint": endpoint.rstrip("/"), "region": region}


def s3(method: str, key: str, credentials: Path, storage: dict[str, str], file: Path | None = None, *, verify_upload: bool = False) -> bytes:
    if not credentials.is_file() or credentials.stat().st_mode & 0o077:
        raise release.ReleaseError("curl S3 credentials file must be a private regular file (mode 0600)")
    if not KEY.fullmatch(key.removesuffix(".json")) or key.endswith(".json") and not key.endswith(".dump.json"):
        raise release.ReleaseError("invalid backup key")
    url = f"{storage['endpoint']}/{storage['bucket']}/{key}"
    argv = ["curl", "--silent", "--show-error", "--fail", "--proto", "=https", "--tlsv1.2",
            "--retry", "3", "--aws-sigv4", f"aws:amz:{storage['region']}:s3", "--config", str(credentials),
            "--request", method]
    if method == "PUT":
        argv += ["--upload-file", str(file), "--header", "Content-Type: application/octet-stream"]
    elif method == "GET":
        if verify_upload:
            # OVH can return 403 briefly for a freshly uploaded version.
            # Do not mark the archive successful until a real download verifies it.
            argv += ["--retry-all-errors", "--retry", "24", "--retry-delay", "5"]
        argv += ["--output", str(file)]
    else:
        raise release.ReleaseError("unsupported S3 operation")
    return command([*argv, url])


def digest(path: Path) -> str:
    hash_value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            hash_value.update(block)
    return hash_value.hexdigest()


def backup(target: Path, credentials: Path, storage: dict[str, str]) -> dict:
    active = release.active_manifest(target, False)
    if active is None or not (target / "release.env").is_file():
        raise release.ReleaseError("no active VPS release")
    backup_dir = target / "backups"
    release.private_dir(backup_dir)
    # Keep daily archives separate from pre-migration backups; include legacy daily names during cutover.
    prune_daily_dumps(backup_dir, 2)
    daily_dir = backup_dir / "daily"
    release.private_dir(daily_dir)
    key = f"{PREFIX}{datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')}-{uuid.uuid4().hex}.dump"
    path = daily_dir / key.rsplit("/", 1)[1]
    argv = release.compose_argv(target, target / "release.env", None,
                                ["exec", "-T", "postgres", "pg_dump", "-U", "sloparena", "-d", "sloparena", "--format=custom"])
    with path.open("xb") as output:
        os.chmod(path, 0o600)
        command(argv, stdout=output, env=release.docker_env(target))
        output.flush()
        os.fsync(output.fileno())
    if not path.stat().st_size:
        raise release.ReleaseError("pg_dump produced an empty archive")
    migrations = release.query_migrations({"steps": []}, target, target / "release.env", None, release.docker_env(target))
    manifest = {"key": key, "timestamp": release.now(), "sha256": digest(path),
                "bytes": path.stat().st_size, "migrations": migrations,
                "release_id": active["release_id"], "images": active["images"]}
    manifest_path = daily_dir / f"{path.name}.json"
    release.atomic_write(manifest_path, release.json_bytes(manifest))
    s3("PUT", key, credentials, storage, path)
    # A successful upload response alone is not proof that the archive can be recovered.
    fetched = daily_dir / f".{path.name}.verify"
    try:
        s3("GET", key, credentials, storage, fetched, verify_upload=True)
        if digest(fetched) != manifest["sha256"]:
            raise release.ReleaseError("uploaded backup failed downloaded SHA-256 verification")
    finally:
        fetched.unlink(missing_ok=True)
    s3("PUT", key + ".json", credentials, storage, manifest_path)
    release.atomic_write(target / "last-offhost-backup.json", release.json_bytes(manifest))
    # Only successful uploads enter the short local rolling window.
    prune_daily_dumps(backup_dir, 3)
    return manifest


def restore(target: Path, credentials: Path, storage: dict[str, str], key: str) -> dict:
    if not KEY.fullmatch(key):
        raise release.ReleaseError("restore requires an explicit postgres/<backup-id>.dump key")
    if not (target / "release.env").is_file():
        raise release.ReleaseError("no VPS release environment")
    restore_dir = target / "restores" / key.rsplit("/", 1)[1].removesuffix(".dump")
    if restore_dir.exists():
        raise release.ReleaseError(f"restore target already exists: {restore_dir}")
    release.private_dir(restore_dir)
    archive = restore_dir / "backup.dump"
    manifest_path = restore_dir / "manifest.json"
    name = "sloparena-restore-" + uuid.uuid4().hex[:12]
    volume = name + "-data"
    try:
        s3("GET", key + ".json", credentials, storage, manifest_path)
        manifest = release.parse_json(manifest_path)
        if manifest.get("key") != key or not isinstance(manifest.get("sha256"), str):
            raise release.ReleaseError("backup manifest does not match selected archive")
        s3("GET", key, credentials, storage, archive)
        if digest(archive) != manifest["sha256"] or archive.stat().st_size != manifest["bytes"]:
            raise release.ReleaseError("downloaded archive does not match manifest")
        network = name + "-net"
        command(["docker", "network", "create", "--internal", network])
        command(["docker", "volume", "create", volume])
        # An internal-only network has no external route or published port.
        command(["docker", "run", "-d", "--name", name, "--network", network, "--tmpfs", "/var/run/postgresql",
                 "--mount", f"type=volume,src={volume},dst=/var/lib/postgresql/data",
                 "--env", "POSTGRES_HOST_AUTH_METHOD=trust", POSTGRES_IMAGE])
        for _ in range(30):
            if subprocess.run(["docker", "exec", name, "pg_isready", "-U", "postgres"],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0:
                break
            time.sleep(1)
        else:
            raise release.ReleaseError("isolated PostgreSQL did not become ready")
        with archive.open("rb") as stream:
            proc = subprocess.run(["docker", "exec", "-i", name, "pg_restore", "--exit-on-error", "--no-owner",
                                   "--no-privileges", "-U", "postgres", "-d", "postgres"], stdin=stream,
                                  stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        if proc.returncode:
            raise release.ReleaseError("pg_restore failed; inspect isolated target before cleaning it")
        migrations = command(["docker", "exec", name, "psql", "-U", "postgres", "-d", "postgres", "-At", "-v", "ON_ERROR_STOP=1",
                              "-c", 'SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";']).decode().splitlines()
        if migrations != manifest["migrations"]:
            raise release.ReleaseError("restored EF migrations differ from archive manifest")
        # Check real persisted tables, including a data count, without printing user data.
        rows = command(["docker", "exec", name, "psql", "-U", "postgres", "-d", "postgres", "-At", "-v", "ON_ERROR_STOP=1",
                        "-c", 'SELECT (SELECT count(*) FROM \"Users\"), (SELECT count(*) FROM \"Matches\"), '
                              '(SELECT count(*) FROM \"GameServers\");']).decode().strip()
        counts = [int(value) for value in rows.split("|")]
        if len(counts) != 3:
            raise release.ReleaseError("restored application tables are incomplete")
        active = release.active_manifest(target, False)
        if active is None:
            raise release.ReleaseError("missing active release for Master readiness check")
        master = name + "-master"
        secret = uuid.uuid4().hex + uuid.uuid4().hex
        command(["docker", "run", "-d", "--name", master, "--network", network, "--read-only",
                 "--tmpfs", "/tmp", "--env", "Deployment__Profile=development",
                 "--env", "ASPNETCORE_HTTP_PORTS=8080",
                 "--env", f"ConnectionStrings__DefaultConnection=Host={name};Port=5432;Database=postgres;Username=postgres",
                 "--env", f"Jwt__Secret={secret}", active["images"]["master"]])
        probe = 'exec 3<>/dev/tcp/127.0.0.1/8080; printf \"GET /ready HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n\" >&3; read -r line <&3; [[ \"$line\" == *\" 200 \"* ]]'
        for _ in range(30):
            if subprocess.run(["docker", "exec", master, "bash", "-ec", probe],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0:
                break
            time.sleep(1)
        else:
            raise release.ReleaseError("isolated Master did not report readiness")
        result = {"postgres": name, "master": master, "network": network, "volume": volume,
                  "key": key, "migrations": migrations, "users": counts[0], "matches": counts[1],
                  "game_servers": counts[2], "master_ready": True}
        release.atomic_write(restore_dir / "result.json", release.json_bytes(result))
        return result
    except BaseException:
        # Do not delete the isolated target or hide a failed restore.
        raise


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["backup", "restore"])
    parser.add_argument("--target-dir", type=Path, required=True)
    parser.add_argument("--credentials", type=Path, required=True, help="private curl config containing user = access-key:secret-key")
    parser.add_argument("--storage-config", type=Path, required=True, help="private JSON containing bucket, endpoint and region")
    parser.add_argument("--key", help="explicit S3 archive key for isolated restore")
    args = parser.parse_args()
    try:
        target = release.target_path(str(args.target_dir))
        if args.command == "restore" and not args.key or args.command == "backup" and args.key:
            raise release.ReleaseError("--key is required only for restore")
        if not args.credentials.is_absolute():
            raise release.ReleaseError("--credentials must be an absolute private file path")
        storage = load_storage(args.storage_config)
        with release.with_lock(target):
            if args.command == "backup":
                try:
                    result = backup(target, args.credentials, storage)
                except (release.ReleaseError, OSError):
                    release.atomic_write(target / "last-backup-attempt.json",
                                         release.json_bytes({"timestamp": release.now(), "result": "failed"}))
                    raise
                release.atomic_write(target / "last-backup-attempt.json",
                                     release.json_bytes({"timestamp": release.now(), "result": "success"}))
            else:
                result = restore(target, args.credentials, storage, args.key)
            print(json.dumps(result, indent=2))
        return 0
    except (release.ReleaseError, OSError) as exc:
        print(f"recovery failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
