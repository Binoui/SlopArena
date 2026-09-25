# Restricted VPS release (#232)

This profile is separate from `deploy/local/` and the current home endpoint. It targets one operator-named Ubuntu 24.04 amd64 VPS; there is no implicit SSH host or automatic deployment from CI. Do not copy home credentials, reuse home hostnames, or point production player builds at this test Master.

## Prepared VPS checkpoint (2026-09-25)

`sloparena-prod-1` was provisioned on Ubuntu 24.04 amd64 with Docker 29.1.3
and Compose 2.40.3. Key-authenticated SSH from the operator IPv4 worked in a
**new** connection after UFW was enabled; the timed rollback was canceled.
UFW denied unsolicited ingress on both address families, allowed SSH/443 TCP
and UDP 7777–7781 only from that operator IPv4 `/32`, and left TCP 80 open
for ACME/redirect. The root-owned Docker-aware tester allowlist and dual-stack
`DOCKER-USER` helper verified before and after a Docker daemon restart.
Private roots were prepared under `/etc/sloparena/` and `/var/lib/sloparena/`;
credentials and a release had not yet been placed there. Password SSH remained
enabled at this checkpoint.

This VPS was prepared stepwise. **Do not rerun `bootstrap.sh` on it:** the
bootstrap resets UFW and changes SSH authentication. At this checkpoint OVH
provider firewall, test DNS, external reachability and published digests had
not been verified; the image-publication workflows existed only in local
commits. Re-check the current state before each subsequent release step.

## Operator prerequisites

1. Acquire an EU VPS and verify its Ubuntu 24.04 amd64 image, public IPv4, optional IPv6, provider-console access, and SSH host key fingerprint out of band. Keep console recovery working before restricting SSH. Back up any existing VPS data before using the target directory. Never run this profile on the home host.
2. Set up an operator SSH account/key, allowlisted tester IPv4 CIDRs (and IPv6 CIDRs only if IPv6 is deliberately enabled), and a separate SSH management CIDR. Restrict SSH sources at the provider firewall as well as on the host; verify one new SSH session before closing the current one. If locked out, recover through the provider console and restore the prior firewall configuration, not by exposing the DB or raw control ports.

Bootstrap (only on the verified fresh VPS, after copying these scripts there):

```bash
sudo deploy/vps/bootstrap.sh --confirm-vps \
  --ssh-source <management-ipv4-or-ipv6-cidr> \
  --tester-source <tester-ipv4-cidr>
```

Repeat `--tester-source` for each authorized IPv4/IPv6 tester range. The command
resets host UFW, checks the operator's key and OS, installs Ubuntu Docker/Compose
packages, disables root/password SSH, and installs the Docker-aware ingress
filter. It is not a command to run on this development workstation or the home
server. Re-run with the complete tester list, not an incremental addition; the
provider firewall remains an independent gate. Test a new SSH connection and
inspect both IPv4 and IPv6 host/provider rules before attempting a release.

If the SSH source rule locks you out, use the provider's out-of-band console,
not the game ports: inspect `sudo ufw status numbered`, restore a rule for the
operator's *current* source CIDR and configured SSH port, and confirm `sshd -t`
before reloading SSH. Correct the provider firewall's management-source rule
through its console too. Open a new key-authenticated SSH session before
closing the recovery session. If the login key itself is rejected, use the
console to correct the sudo operator's `authorized_keys` and file permissions;
do not enable password/root SSH or open SSH to `0.0.0.0/0`.

3. Have the operator create two **new** public DNS A records, `MASTER_TEST_HOST` and `GAMEPLAY_TEST_HOST`, pointing to the VPS IPv4, not the home tunnel. If no IPv6 policy is deployed, do **not** add AAAA records. Check both names from an external resolver before ACME issuance. Configure the provider firewall: port 80/tcp to the VPS for ACME HTTP challenge/HTTPS redirect; 443/tcp and 7777–7781/udp only from tester CIDRs; SSH only from management CIDRs; deny all other inbound traffic including IPv6. Verify the provider rules independently of the Docker host rules.
4. Publish one GameServer image and one Master + EF migration image under one release ID via the explicit publication workflows. Record immutable registry image digests and both source revisions. Publication must never deploy a push to `main`. Keep this release record and any secrets outside Git and outside the images.
5. Create private operator-owned runtime files from `../local/master.env.example`, `../local/migration.env.example`, and `../local/game.json.example`, with new independent DB/JWT/registration/control credentials. The migration environment contains only the DB connection string; the Master and GameServer share only the approved host identity/registration/control credentials. Set the GameServer `publicIp` to the new gameplay test DNS, Master `ApprovedHost__PublicHost` to the same value, and `masterServerUrl` to `http://master:8080`. Keep runtime files at mode 600 in a mode-700 private directory; permit container UID 1654 read-only access to the GameServer JSON with an ACL. Never put secrets in a release JSON, Compose command line, shell history, client, registry image, or tracked config.

The release command must fail if DNS points to the home host or firewall preflight fails. Treat provider firewall validation and off-host network probing as separate operator gates: a Docker host firewall cannot prove provider rules or a friend's ISP reachability. Certificate data and PostgreSQL data persist in named volumes; make an off-host copy of the pre-migration backup before relying on disaster recovery. Do not use `down -v`, destructive prune, or runtime-config rsync.

## Operator release commands

The script runs **on the selected VPS itself**, not through an SSH default.
Create a release JSON from `release.example.json` in an operator-owned
directory outside Git. Fill the exact published `repository@sha256:<digest>`
references from the GameServer and Master workflow artifacts, both source
revisions, their shared `release_id`, the actual EF migration target and
compatibility list, two new test DNS names and the VPS public IPv4 (set
`public_ipv4_is_provider_nat` only for verified provider 1:1 NAT), and absolute
paths to the private runtime files. Do not substitute local Docker image IDs,
tags, or a home endpoint. Keep each release JSON; the CLI saves a copy in the
private target state directory. Run as the sudo operator to access Docker and
write the state/backup directory; do not grant Docker-group access merely to
avoid `sudo`. The installed bootstrap firewall helper must be executable.

```bash
sudo python3 deploy/vps/release.py deploy \
  --target-dir /var/lib/sloparena --config /etc/sloparena/release.json
sudo python3 deploy/vps/release.py status --target-dir /var/lib/sloparena
sudo python3 deploy/vps/release.py logs --target-dir /var/lib/sloparena --service master
sudo python3 deploy/vps/release.py logs --target-dir /var/lib/sloparena --service caddy --tail 50
sudo python3 deploy/vps/release.py rollback \
  --target-dir /var/lib/sloparena --release-id <previous-compatible-release-id>
```

No command targets the home host. The deployment lock serializes changes;
image/digest/label, DNS and Compose checks precede writer disruption. Schema
changes stop writers, require a successful `pg_dump` before running the pinned
EF bundle, then check the applied migration. Incompatible rollback aborts
without replacing the database or certificate volumes. Inspect saved event
history, previous logs and database backup before retrying a failed migration.

## Disposable two-pair rehearsal

Use a fresh disposable target **outside the repository** and a private fixture
directory. Create two release JSON records from `release.example.json` with
different release IDs, their matching image labels/source revisions, the same
explicit compatible migration ID, and matching private test credentials.
Locally built `#231` images use pseudo RepoDigests equal to Docker image IDs;
they are *not* published registry digests. For this isolated rehearsal only,
provide a loopback-only Compose override:

```yaml
services:
  caddy:
    ports: !override
      - "127.0.0.1:18080:80/tcp"
      - "127.0.0.1:18443:443/tcp"
  game:
    ports: !override
      - "127.0.0.1:17777-17781:7777-7781/udp"
```

With that override saved as `<loopback.yaml>` and an explicit executable
firewall fixture `<fixture>` for the disposable host, execute the *same*
operator commands with isolation flags:

```bash
python3 deploy/vps/release.py deploy --target-dir <absolute-disposable-state> \
  --config <first-release.json> --allow-disposable-host \
  --compose-override <loopback.yaml> --firewall-helper <fixture> \
  --allow-local-image-digests
python3 deploy/vps/release.py deploy --target-dir <absolute-disposable-state> \
  --config <second-compatible-release.json> --allow-disposable-host \
  --compose-override <loopback.yaml> --firewall-helper <fixture> \
  --allow-local-image-digests
python3 deploy/vps/release.py rollback --target-dir <absolute-disposable-state> \
  --release-id <first-release-id> --allow-disposable-host \
  --compose-override <loopback.yaml> --firewall-helper <fixture> \
  --allow-local-image-digests
```

These flags deliberately do **not** validate registry publication, host
firewall, public DNS or the provider firewall. Never use them for live release.
Only `caddy` and gameplay UDP bind loopback in the disposable override; the
Master, database and TCP control remain unbound. Inspect the active release,
`schema.json`, history, SHA-256 backup and `docker compose ps` after each
step. Keep or explicitly clean up the disposable volumes separately; never
apply `down -v` to a production target.

### Observed isolated rehearsal (2026-09-25)

With local Docker Compose 5.5.1, the `local-231` Master/GameServer/migration
images started in a fresh, target-derived project. A first attempt with older
`local-229` images applied the explicit
`20260802141019_AddUniqueIndexGameServerIpPort` migration only **after** a
successful `pg_dump` (SHA-256
`c18cf688e673cf669c8fe5d4b202481c67b5421357b0556ab308f77427fd5850`),
but failed the application-readiness gate: that older Master image returns 404
for `/ready`. It was **not** counted as a successful release.

The successful sequence was `local-231` → `local-232-b` → rollback
`local-231`. The second pair had distinct image IDs and release labels but was
derived from the same `local-231` binaries; this rehearses immutable-identity
replacement and rollback, **not** compatibility across different application
code revisions. Both releases and the rollback returned success with schema
`20260802141019_AddUniqueIndexGameServerIpPort`. A deliberately incompatible
record was rejected before the writer-stop step, leaving `local-231` active.
PostgreSQL and both Caddy volume identities matched before and after rollback.
The exported persistent Caddy CA validated `https://master.localhost:18443/ready`
without insecure TLS flags and returned `{\"status\":\"ready\"}`. `status` reported
Master/GameServer/PostgreSQL healthy, no host port for raw Master/PostgreSQL
or GameServer TCP, and loopback-only TCP 18080/18443 plus UDP 17777–17781.

The disposable stack was stopped without `down -v`, then its uniquely named
test volumes, test image tags and temporary fixtures were removed. Original
`local-229` and `local-231` images remain untouched.

The local firewall helper was an explicit disposable fixture; this run proves
neither firewall enforcement nor public DNS, external non-tester blocking,
off-host tester traffic, ACME, VPS reboot, or a packaged Unity match.

## Recovery boundaries

Application rollback does not undo EF migrations. Use it only while the
current database migration is explicitly within the previous application's
declared schema compatibility range. For an incompatible schema, stop writers,
make a separate recovery decision, and restore a verified pre-migration
`pg_dump` into a fresh PostgreSQL volume under operator control before
reintroducing the old application pair. Do not restore automatically into the
live volume, drop that volume, or remove the Caddy certificate volumes.
Retain both image/revision records, the migration outcome, the backup path and
its checksum, and bounded pre-replacement logs for audit and recovery.

## Off-host PostgreSQL recovery (#233)

The OVH SBG bucket `sloparena-vps-db-backups` is separate from VPS snapshots.
It has AES256 at-rest encryption, versioning, 30-day governance Object Lock for
new objects, and the `postgres/` lifecycle in `lifecycle.json` (30 days current,
30 days noncurrent, incomplete multipart uploads after seven days). Public
Cloud user `808255` has a policy allowing Put/Get under `postgres/` and
explicitly denying deletion, ACL mutation and governance bypass. The tested
object/version deletes returned 403. **Do not infer complete public-access
blocking from that policy:** OVH [does not implement PutPublicAccessBlock in
Regions](https://docs.ovhcloud.com/en/guides/storage-and-backup/object-storage/s3-s3-compliancy)
and the uploader-owned probe accepted `PUT ?acl` immediately after an
explicit deny was installed. After the policy settled, the same ACL change
returned 403. A temporary `s3:GetObject` deny still allowed reads of both
an older and a recent uploader-owned object after roughly a minute; a new
key also read an older object shortly after issuance. Policy propagation
is therefore a factor, and upload-only protection was **not established**.
The test-only key was revoked; the working policy intentionally permits
Put/Get. A known private object returned 403 to an anonymous GET. Keep the
key private, do not use account IAM user `sloparena-backup` for S3, and
rotate it separately from the DB password.

**Accepted demo exception (2026-09-25):** keep OVH with this single
root-owned backup key, which can both upload and read archives. Do not claim
upload-only access, protection from a stolen key reading the database, or
AWS-style public-access blocking. Treat key disclosure as database archive
disclosure, revoke/reissue the key and any exposed GameServer API tokens,
then review object ACLs. No customer data should be regarded as protected
from someone who has acquired this key. Object Lock and versioning protect
the existing versions against the tested deletion path for 30 days, not
archive confidentiality. A separate operator read key would need a settled
test proving the uploader can no longer read its own archives; that is not
the chosen demo contract.

On the **VPS**, review the staged files before installing them into
`/opt/sloparena/deploy/vps/` alongside the existing `release.py` and
`compose.yaml`. Python 3, curl with `--aws-sigv4`, and Docker must already
be present; do not install tools implicitly. The dedicated S3 key belongs
in `/etc/sloparena/private/s3-curl.conf` as a root-owned mode 0600 curl
config:

```text
user = "ACCESS_KEY:SECRET_KEY"
```

Keep this file and the credential JSON outside Git, images, logs, and chat.
Use a private transfer rather than a shell command containing either key.
The generated workstation credential is in
`~/.config/sloparena/ovh-s3-credential.json`; a prepared local curl config is
in `~/.config/sloparena/s3-curl.conf`. Only an operator moves the config
into its root-only VPS destination. The S3 endpoint is
`https://s3.sbg.io.cloud.ovh.net/` (signing region `sbg`). A single PUT
supports archives below 5 GiB; larger databases require a multipart-capable
client before this job can be relied on.

For the current `sloparena` SSH target, all eight non-secret deploy assets
have been checksum-verified in
`/home/binoui/.cache/sloparena-233-20260925/` (0700), alongside a temporary
0600 curl config. From an interactive SSH session as `binoui`, enter the
sudo password **locally**, verify this staging directory still matches the
reviewed files, and install:

```bash
stage="$HOME/.cache/sloparena-233-20260925"
for file in release.py recovery.py; do
  sudo install -o root -g root -m 0755 "$stage/$file" "/opt/sloparena/deploy/vps/$file"
done
for file in compose.yaml Caddyfile sloparena-backup.service sloparena-backup.timer lifecycle.json README.md; do
  sudo install -o root -g root -m 0644 "$stage/$file" "/opt/sloparena/deploy/vps/$file"
done
sudo install -o root -g root -m 0600 "$stage/s3-curl.conf" /etc/sloparena/private/s3-curl.conf
rm -- "$stage/s3-curl.conf"
```

Installing Compose/Caddy configuration does not itself recreate running
containers. Do not enable the timer or replace Caddy/Master until the first
off-host backup and isolated restore pass; check the active-match count
before any service replacement.

After placing the key and code, run one backup and inspect its result before
attempting the isolated restore:

```bash
sudo python3 /opt/sloparena/deploy/vps/recovery.py backup \
  --target-dir /var/lib/sloparena \
  --credentials /etc/sloparena/private/s3-curl.conf
```

The daily timer starts at 04:15 UTC plus up to 30 minutes of jitter, catches
up after downtime, and uses the same deployment lock; systemd also prevents
overlap of its own service. The dump is PostgreSQL 15 custom format. The job
uploads the archive, downloads it for SHA-256 verification, uploads a
non-secret migration/image manifest, then marks success in
`last-offhost-backup.json` (visible via `release.py status`). Local daily dumps
are capped at three; pre-migration backups are separate. Inspect
`systemctl status sloparena-backup.service` and the journal on failure; a
local `pg_dump` or VPS snapshot alone is **not** off-host success.

OVH has returned a transient 403 to an immediate GET after a successful
PUT. The verification download retries for at most 24 × 5 seconds; success
still requires the downloaded SHA-256 to match before the manifest and
success marker are uploaded. A `.dump` with no paired `.dump.json` from an
older failed attempt is **not** a completed backup; leave it to the
30-day lifecycle instead of claiming or restoring it.

To prove recovery, select an actual archive key from a completed backup:

```bash
sudo python3 /opt/sloparena/deploy/vps/recovery.py restore \
  --target-dir /var/lib/sloparena \
  --credentials /etc/sloparena/private/s3-curl.conf \
  --key postgres/<backup-id>.dump
```

The restore downloads the archived manifest and dump, checks size/SHA-256,
creates **new** Docker volume and containers on a new internal-only network
with no published ports, runs `pg_restore --exit-on-error`, compares EF
migration IDs, counts persisted Users/Matches/GameServers, and requires an
isolated Master `/ready` response. The result lists its isolated container,
network and volume names for inspection and explicit cleanup. It never
replaces the live database, copies a PostgreSQL data directory, or exposes
restored host credentials on a live route. A restore against a different
active Master schema must fail rather than pass readiness by inference.

Only after the downloaded-archive restore and Master readiness checks pass,
enable the daily timer:

```bash
sudo install -m 0644 /opt/sloparena/deploy/vps/sloparena-backup.service \
  /etc/systemd/system/sloparena-backup.service
sudo install -m 0644 /opt/sloparena/deploy/vps/sloparena-backup.timer \
  /etc/systemd/system/sloparena-backup.timer
sudo systemctl daemon-reload
sudo systemctl enable --now sloparena-backup.timer
sudo systemctl list-timers sloparena-backup.timer
```

To apply the new Caddy error-log filter and Master request-log suppression,
first inspect `release.py status` and wait for zero active matches. Then deploy
the **same saved, schema-compatible release record** through the deploy
command, which captures the pre-replacement log bundle and runs normal
image/firewall preflight. This replaces application containers and may
interrupt an in-progress match; never do it blind. The Caddyfile is a
bind-mounted file; Compose may retain the existing Caddy container, so
explicitly reload its running configuration after deploy:

```bash
sudo python3 /opt/sloparena/deploy/vps/release.py status \
  --target-dir /var/lib/sloparena
release_id=$(sudo python3 /opt/sloparena/deploy/vps/release.py status \
  --target-dir /var/lib/sloparena | python3 -c 'import json,sys; print(json.load(sys.stdin)["active_release"])')
sudo python3 /opt/sloparena/deploy/vps/release.py deploy \
  --target-dir /var/lib/sloparena \
  --config "/var/lib/sloparena/releases/$release_id.json"
sudo python3 - <<'PY'
import subprocess
import sys
from pathlib import Path
sys.path.insert(0, "/opt/sloparena/deploy/vps")
import release
target = Path("/var/lib/sloparena")
command = release.compose_argv(
    target, target / "release.env", None,
    ["exec", "-T", "caddy", "caddy", "reload", "--config", "/etc/caddy/Caddyfile"],
)
subprocess.run(command, env=release.docker_env(target), check=True)
PY
sudo python3 /opt/sloparena/deploy/vps/release.py status \
  --target-dir /var/lib/sloparena
```

An OVH VPS automated backup is a separate whole-machine recovery aid, not
evidence of a database restore. Rotating/reissuing database, JWT, approved
host, registry and storage credentials is separate from restoring data;
restored GameServer registrations/tokens must not be reused to enroll a new
host. Do not import home lobby/chat in-memory state or delete home data.

### Operations and credential reissue

`release.py status` reports recorded release/source/image identities, container
state, `/health` and `/ready` for both applications, the Master registration
row's heartbeat freshness and reported active-match count, root/state/Docker
data filesystem use, and the last off-host success **and** last backup attempt.
The match count comes from the latest Master heartbeat, not a synchronous
simulation query; an unavailable database reports registration as unavailable
instead of inventing zero. `logs --service caddy|postgres|master|game --tail N`
is bounded to 200 lines and 1 MiB. Before replacement the release saves up
to 200 recent lines/1 MiB across Caddy, Master and GameServer in a private
history file. Docker `local` logging rotates each long-lived service at
10 MB × 3. The proxy does not enable access-request logging; its structured
error logger deletes `request.uri` because upstream failures otherwise log
SignalR `access_token` query strings. The Master suppresses ASP.NET Hosting
request-start/end logs for the same reason. The operator logs command and
pre-replacement bundle also discard legacy request/JWT lines and strip
structured Caddy request metadata before printing or saving them. This does
not erase old raw Docker log files; treat them as private until their
containers are replaced. Check a known event before replacement and in the
private bundle afterward; never paste raw logs, tokens or chat bodies into
an issue.

**Reissue is not restore.** Stop writers/deploys as appropriate, prepare each
replacement in private files, replace one credential domain at a time, check
readiness/registration, and revoke the old value only after the new path
works:

| Domain | Reissue boundary |
| --- | --- |
| PostgreSQL | Change the `sloparena` role password in PostgreSQL and atomically update the postgres password file plus Master/migration connection files; restart affected services. Changing `POSTGRES_PASSWORD_FILE` alone does **not** update an existing database role. Verify Master `/ready` and a fresh dump. |
| JWT | Change only the Master's `Jwt__Secret`, restart Master; existing guest sessions become invalid and must authenticate again. A DB restore must not reintroduce the old secret. |
| Approved host registration and match control | Replace registration and control keys separately in both private Master/GameServer configs, then restart the affected processes during maintenance. Existing VPS `GameServers.ApiToken` survives re-registration: changing only the registration key does **not** revoke it. To reissue a compromised API token, stop GameServer, drain/accept match interruption, invalidate the provisioned registration row under operator control, then re-register and verify a new token; do not delete an active row mid-match. |
| VPS SSH | Add and verify a new key from an allowed management CIDR in a **new** session before revoking the old key. Preserve provider-console recovery and SSH source restrictions. |
| GHCR read credential | Rotate the VPS registry read-only credential through the registry; test a pinned manifest pull before revoking the old credential. Do not place it in release JSON. |
| OVH Object Storage | Create a new S3 access pair/user with the same scoped policy, install a private curl config, prove upload/download and both object/version delete rejection, then revoke the old pair. OVH ACL mutation remains a known limitation despite explicit deny; never treat the scoped policy as an AWS public-access block. Never copy S3 secrets into database archives. |

Home export/import is **not** part of this VPS release. If chosen later,
`pg_dump` must read the home source without modifying/deleting it; restore to
a new isolated PostgreSQL target and verify records/migrations before any
cutover. Do not copy home runtime configs, reusable GameServer registration
rows/API tokens, or in-memory lobby/chat state into a reachable Master.
Enroll the target GameServer afresh with new host credentials. A PostgreSQL
data-directory copy of a running home server is not a safe import.

## Acceptance on a live target

From a **non-allowlisted** external network, attempt guest auth, hub WebSocket/long polling, UDP 7777–7781, and TCP 7777, 5432, 8080; none may reach the service. From an allowlisted external client verify trusted HTTPS, guest auth, hub both transports, character catalog, direct UDP match and rematch. Check 80/tcp only redirects or handles ACME validation, and HTTPS certificates are publicly trusted. Verify IPv6 separately or that no AAAA/public IPv6 listener exists. Restart the VPS and verify service readiness/registration and certificate persistence. A failed firewall, readiness, migration, backup, or digest check blocks release rather than falling through to a broader ingress rule.

## Observed live test release (2026-09-25)

The operator installed the existing SSH public key and opened a new
key-authenticated session. On `sloparena-prod-1`, Ubuntu Docker 29.1.3 and
Compose 2.40.3 run with UFW denying unsolicited IPv4/IPv6 ingress, operator
IPv4 `/32` access to SSH/HTTPS/gameplay UDP, and public TCP 80 for ACME. The
Docker-aware `DOCKER-USER` filter survived a daemon restart. Password SSH
remains enabled; the OVH provider firewall was not changed.

Both DNS-only test A records resolved to the VPS IPv4 with no AAAA. Under
release ID `vps-test-20260925-1`, the immutable GameServer (`5ba1be4`) and
Master/migration (`263a19d`) images passed the digest/label gate. Before the
explicit EF migration, `pg_dump` wrote a nonempty, SHA-256-verified backup.
The active schema is `20260802141019_AddUniqueIndexGameServerIpPort`;
Master, GameServer and PostgreSQL report healthy, GameServer registered,
and Master heartbeat updates succeeded. The first deploy attempt was
rejected before service startup because Docker 29 identifies genuine
registry-pulled images by their manifest digest; the registry check was
corrected before the successful retry.

From the allowed external IPv4, trusted public HTTPS returned 200 from
`/ready`; TCP 80 returned 308 to HTTPS. Guest auth and authenticated
`/auth/me` returned 200; SignalR negotiation, a fresh WebSocket upgrade
(101) and long polling (200) passed through Caddy. Public raw TCP
7777/8080/5432 did not connect. A direct UDP datagram to 7777 incremented
the allowed Docker ingress-rule packet count from zero to one. A separate
authenticated private match-start for Manki and FightGuy then allocated
UDP 7777 and returned the cooked content map. Two external, allowlisted
UDP clients sent valid version-1 inputs and both were logged as connected;
each client received a 127-byte authoritative state packet from 7777.
The synthetic match was cleared by restarting only GameServer. It
re-registered; the server directory returned to zero active matches.
This is not a packaged Unity-client match.

Direct HTTPS over the VPS's global IPv6 timed out from an unallowed IPv6
source. From mobile data, the operator reported **connection reset** when
opening the test Master's `/ready` URL; no HTTPS response reached that
non-allowlisted IPv4 client. The live Docker filter admits gameplay UDP
only from the configured IPv4 `/32`, but no separate mobile-data UDP packet
was captured. OVH's provider firewall was not changed.

Still to prove in the later end-to-end acceptance: an allowlisted packaged
Unity client completes a match, VPS reboot preserves registration/certificates,
and an off-host database restore succeeds. This test release does not
establish those behaviors.

## Off-host recovery checkpoint (2026-09-25)

The operator ran `recovery.py backup` on the restricted VPS under release
`vps-test-20260925-1`. The completed archive
`postgres/20260925T145904Z-7be3bc12c6b3496dbd5a32255f239b17.dump`
has 9,116 bytes and SHA-256
`944da02fac0aa83cd6c32bbcd592191d922d90793961c76cba505ae0516d86f0`.
Its non-secret manifest was independently downloaded from OVH and matched
the reported key, size, checksum and three EF migration IDs. Earlier two
`.dump` objects without manifests failed the immediate read-after-write
check; they are **not** completed backups. The bounded retry for transient
403 was exercised against a 403-then-200 HTTPS fixture and a fresh OVH
upload/download. The operator then downloaded that **live** archive into
isolated PostgreSQL container `sloparena-restore-115280153537` on internal
network `sloparena-restore-115280153537-net` with a new Docker data volume.
`pg_restore` and the three EF migrations passed; the restored database had
one User, zero Matches and one GameServer, and isolated Master `/ready`
returned 200. No live database was replaced or exposed. The operator then
installed and enabled `sloparena-backup.timer`; its next run is scheduled
for 2026-09-26 06:39:25 CEST. A service-run backup reported success at
15:05:20 UTC with archive
`postgres/20260925T150519Z-2118388eb50c482e80c15d92a92a1fdb.dump`
(9,115 bytes, SHA-256
`5fcd24c06facf9d067ed191b7818bb7ddfd380d3a4fef92909050e88472933c3`).
Live `release.py status` reported Master/GameServer liveness and readiness,
one fresh registration, zero active matches, pinned image identities, disk
usage and the successful off-host backup. With zero active matches, the
operator replayed the same release; event
`cf0deaaa7b854f4eab957fe486b14fb0` succeeded without changing schema
or image identities. Post-deploy `/health` and `/ready` for both apps
remained true and registration was fresh with zero active matches. The
pre-replacement bundle was captured by the deploy path. The operator reports
reloading live Caddy. The 39,602-byte private bundle contains Caddy, Master
and GameServer entries; the operator's inspection found neither
`access_token` nor `Bearer ` markers. The local upstream-failure probe
also showed the new Caddy filter drops token-bearing request metadata.
The operator reports cleaning up only the isolated restore containers,
network and volume after saving their result; the live PostgreSQL/Caddy
volumes were not part of that cleanup. No further infrastructure expansion
is needed for the friends playtest.
