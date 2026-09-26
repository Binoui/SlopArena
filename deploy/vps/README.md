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

3. The test DNS names `MASTER_TEST_HOST` and `GAMEPLAY_TEST_HOST` point to the VPS IPv4, not the home tunnel. Keep SSH restricted to management CIDRs and HTTPS 443 to tester CIDRs; port 80 serves ACME/redirect. The Steam GameHost listens through Valve relay and Compose publishes **no UDP or private TCP gameplay/control ports**. Existing restricted UDP firewall rules are historical and should be removed separately after the no-listener cutover; do not open new ingress for this release.
4. Publish compatible GameServer, Master and EF migration images under one release ID via their explicit workflows. Pin image digests and both source revisions. An upload/push alone never deploys the VPS.
5. Keep DB/JWT/registration/control credentials in private operator files. Master additionally needs `Steam__ApiKey` from the Playtest publisher; Compose pins `Auth__Mode=steam`, Playtest AppID `5325920` and identity `sloparena-playtest`. Supply the tested Valve SteamCMD `steamclient.so` separately as a read-only runtime file, pin its SHA-256 in the release record, and confirm production redistributable rights before treating this test mount as a shippable image. `game.json` keeps the provisioned host GUID, private `masterServerUrl` and control port; its `publicIp` is metadata, never a Steam identity. Store runtime files outside Git with private permissions and grant container UID 1654 read-only access to the GameHost config. Never put keys or the GameHost config in an image, client or release JSON.

The release command must fail if DNS points to the home host or firewall preflight fails. Treat provider firewall validation and off-host network probing as separate operator gates: a Docker host firewall cannot prove provider rules or a friend's ISP reachability. Certificate data and PostgreSQL data persist in named volumes; make an off-host copy of the pre-migration backup before relying on disaster recovery. Do not use `down -v`, destructive prune, or runtime-config rsync.

## Operator release commands

The script runs **on the selected VPS itself**, not through an SSH default.
Create a release JSON from `release.example.json` in an operator-owned
directory outside Git. Fill exact published `repository@sha256:<digest>`
references for GameHost, Master and migrations, their two source revisions,
shared `release_id`, target/compatible EF migrations, two test DNS names,
VPS IPv4, five absolute private runtime file paths (including the
SteamCMD `steamclient.so`), and its independently verified SHA-256. Do not
substitute local Docker image IDs, tags or a home endpoint. Keep each release
record; the CLI saves a copy under the private target. Run as the sudo
operator to access Docker and make the database backup. The installed
firewall helper must be executable.

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
```

The disposable override changes only Caddy's public ports. Steam gameplay
has no host UDP port even in the rehearsal.

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
firewall, public DNS or the provider firewall. Never use them for a live
release. Only Caddy binds loopback in the disposable override; Master,
PostgreSQL and private GameHost control remain unbound. Inspect the active
release, schema, backup/checksum and Compose status after each step. Keep
or explicitly clean up disposable volumes separately; never apply `down -v`
to production state.

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
`last-offhost-backup.json` (visible via `release.py status`). Local daily
archives and their manifests live in `backups/daily` and are capped at three
after successful uploads. Existing timestamp-named daily archives in
`backups/` are included during retention cutover; pre-migration backups there
are untouched. Inspect `systemctl status sloparena-backup.service` and the
journal on failure; a local `pg_dump` or VPS snapshot alone is **not**
off-host success.

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

GameServer `/ready` requires cooked content and a Master registration or heartbeat
acknowledgement from the last 15 seconds; `/health` remains a listener check.
An outage can turn readiness to 503 without stopping active fights or dropping
the registration token. A successful heartbeat restores readiness without
re-registration; a 401/404 heartbeat still starts re-registration.

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

For a newly built packaged Windows client, set the test Master origin **before
launch**, in each tester's PowerShell session:

```powershell
$env:SLOPARENA_MASTER_URL = "https://<test-master-host>"
.\SlopArena.exe
Remove-Item Env:SLOPARENA_MASTER_URL
```

The override requires a bare HTTPS origin; an invalid value stops guest session
startup rather than silently connecting to the home Master. Without the variable,
the shipped player continues to use the home endpoint. Set it for both packaged
clients and confirm the browser/chat connect to the test Master before joining.
Previously built players do not contain this launch override. Neither this
override nor the synthetic UDP probe constitutes a completed Unity-client match.

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

The packaged-client match, VPS reboot and distinct live release-pair rollback
remain pending. The off-host database restore was subsequently verified below;
the synthetic UDP clients above did not run a Unity match.

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

## Phase 1 handoff status (#234, 2026-09-25)

This is a restricted friends-demo integration environment, **not** clearance
for unrestricted internet exposure or a cutover of home/player DNS. The
observations above and the local rehearsal establish only the gates marked
verified here. Re-run `release.py status` and retain its timestamped output
before a live play session; the operator VPS SSH key is not available in every
development shell.

| Gate | State | Evidence or remaining proof |
| --- | --- | --- |
| Clean VPS, pinned published pair and explicit schema migration | **Verified** for the current test release | Ubuntu 24.04 amd64, release `vps-test-20260925-1`, GameServer source `5ba1be45603492e944f51f44fe8d8d299054b704`, Master source `263a19dbbad38d79271ff89814d5a044c0923793`. Live status confirms published GameServer digest `62b983660f89d31e5a4c43c5a074ba93e62556665a40b5e5293c2e7428decc48`, Master digest `9fbd739d5f6ddbae4f49207dd25b087bd8b69dbbeb107deabca88c2ee9b9543a`, migration digest `1297e8037b44f3f4d97c21c7197012f00efe018271b0c9a41115a4e03356b94d`, Caddy 2.11.4 and PostgreSQL 15.19. The actual provider/storage monthly quote remains pending. |
| Allowlisted external HTTPS, guest auth, WebSocket and long polling | **Verified** for protocol probes | `/ready`, `/auth/me`, SignalR negotiation, WebSocket upgrade and long polling passed over trusted HTTPS. The chat exchange and lobby/browser path through two packaged clients remain **pending**. |
| Direct gameplay UDP | **Verified** for a synthetic match on 7777 | Two authenticated external UDP clients received authoritative state. A packaged Unity match, its completion and rematch remain **pending**. |
| Non-allowlisted and dual-stack ingress | **Pending** | Mobile IPv4 HTTPS reset and non-allowlisted IPv6 timeout observed, and raw TCP listeners were inaccessible in the allowed-source probe. Independently deny-test guest auth, both hub transports and **each** UDP port 7777–7781 from a non-allowlisted source; verify raw TCP 7777/8080/5432 and provider firewall/IPv6 policy. Password SSH is still enabled; provider firewall was not changed. |
| Recovery under service order, dependency loss and reboot | **Pending** | Same-release redeploy kept health and registration; GameServer re-registered after clearing a synthetic match. GameServer-first startup, Master restart, PostgreSQL stop/start with liveness/readiness observations and a VPS reboot are not recorded as live proofs. |
| Off-host archive restoration | **Verified** | OVH archive was downloaded and SHA-256 checked, restored to an isolated PostgreSQL volume; three EF migrations, one User, one GameServer and Master `/ready` passed. The restored database contained zero Matches; do not claim a match record was recovered. |
| Live second release and compatible rollback | **Pending** | Two-pair deploy/rollback and volume persistence passed only in the local disposable rehearsal; the second pair reused the same binaries. A live second published pair and return to the first pair remain to be exercised with zero active matches. |
| Five-slot resource observation and final handoff | **Pending** | Record observed CPU/memory/disk and slot allocation without claiming five-match capacity. Capture full release identities, actual monthly spend, command outputs, operator-supplied values, test failures, restore/rollback evidence and explicit pending actions. |

Operator-run `release.py status` on 2026-09-25 reported Caddy, Master,
GameServer and PostgreSQL running; the latter three have healthy container
checks. Master and GameServer liveness/readiness were both true, with one
fresh registered host and zero active matches. Current migration was
`20260802141019_AddUniqueIndexGameServerIpPort`; the latest successful
off-host backup was `2026-09-25T15:05:20Z`, 9,115 bytes, SHA-256
`5fcd24c06facf9d067ed191b7818bb7ddfd380d3a4fef92909050e88472933c3`.
The VPS root/data filesystem had 4,260,204,544 of 76,887,154,688 bytes used
at that check. Docker publishes Caddy TCP 80/443 and gameplay UDP 7777–7781
on both address families; that is **not** proof that the host/provider firewall
denies unallowlisted sources. The status output does not include test DNS
names or a monthly bill. This is one live snapshot, not reboot or outage proof.

The operator supplied the two current test names:
`master-test.sloparena.barakaslurp.fr` and
`game-test.sloparena.barakaslurp.fr`. Public DNS resolution on this
workstation returned A `135.125.100.228` for each and no AAAA answers.
From the allowlisted workstation, trusted HTTPS to the test Master
returned `/ready` HTTP 200 with `{"status":"ready"}` and certificate
verification result 0; TCP 80 returned 308 to that HTTPS URL. These
observations confirm this source can reach the test route, not that
non-allowlisted sources are blocked or IPv6 listeners are disabled.

The workstation has a global IPv6 route and is **not** in the configured
tester IPv4 `/32`. Connecting directly to the VPS global IPv6 address with
the test Master hostname as SNI timed out on TCP 443 after five seconds;
TCP 7777, 8080 and 5432 did not connect. IPv6 TCP 80 returned the expected
308 redirect. This confirms source-specific IPv6 TCP behavior from this
network, not disabled IPv6 exposure, denial of gameplay UDP 7777–7781,
or denial from a separate non-allowlisted IPv4 network.

The operator prefers to test gameplay on real hardware. A Windows player
was launched under Wine once and then stopped at the operator's request;
Wine is not accepted as gameplay or packaged-client acceptance proof and
must not be retried for this issue.

Current workstation verification: `unity pipeline list` found the Unity
Editor; `recompile_status` reported `up_to_date`, `failed: false`, no compiler
errors. In Editor play mode, `SLOPARENA_MASTER_URL=https://example.invalid`
selected that origin before guest auth (which failed as expected); clearing the
variable selected the unchanged home origin. An HTTP override raised the
expected `InvalidOperationException` and left `ChatSession.Instance` absent.
That is an Editor smoke, not a packaged-client live match. The Unity Console
also had an unrelated Unity Connect Package Manager access-token error before
these play-mode checks.

The first read-only SSH status attempt failed after the VPS accepted the
configured public key because no SSH agent had unlocked the private key.
After the operator unlocked it locally, key-authenticated login as `binoui`
worked. The next `sudo -n release.py status` attempt stopped with `sudo: a
password is required`; `/var/lib/sloparena` is root-only and the Docker socket
is not available to that unprivileged account. The operator subsequently
entered the sudo password locally and supplied the live status summarized
above. The backup
timer reports `active`, with its next scheduled activation on 2026-09-26
06:39:25 CEST. From this workstation (an allowlisted source), TCP 80 and 443
connected to the VPS IP; TCP 7777, 8080 and 5432 did not. This does **not**
prove non-allowlisted denial or UDP ingress. No live service, provider rule or
player DNS changed during these checks.

`./scripts/build-release.sh 0.2.0-vps-test.1` passed Shared build and
1,045 Shared tests (six skipped), published both bundled Windows and
Linux GameServer binaries and built a Windows player. The ZIP integrity
check passed; it contains the roster manifest and all four admitted cooked
package payloads, with no bundled `Server/server.json`. The script now
preserves pre-existing ignored `StreamingAssets` staging rather than deleting
it: a tar checksum of `data`, `Server` and `arenas` matched before and after
the build, and the original `ProjectSettings.asset` version stamp was restored.
The local ZIP is `build/release/SlopArena-0.2.0-vps-test.1.zip`; it has not
been run on Windows, distributed or published. A built artifact is not proof
of guest auth, chat or match completion.

Changed in the game repository for this acceptance slice:
`client/Unity/Assets/Scripts/Runtime/Network/ChatSession.cs`,
`client/Unity/Assets/Scripts/Runtime/UI/ServerBrowserUI.cs`,
`scripts/build-release.sh` and `deploy/vps/README.md`; the ignored local
`TESTING-UNITY.md` has the packaged-client checklist. No Master repository
files changed.

Next operator sequence: confirm the test DNS names and source allowlist from
the private release record without exposing credentials; distribute the newly
built client to two isolated tester sessions under operator control. Run guest
auth → chat → browse/join → match completion/rematch and capture client/server
evidence. Use an independent
non-allowlisted network for the denied-ingress matrix. Schedule controlled
recovery/reboot and a second compatible release/rollback when active matches
are zero. Do not restart live services, alter provider ingress, disable SSH
authentication or publish a new player endpoint as a side effect of this
handoff. Phase 2 may revisit player identity/gameplay admission and public
ingress; those are out of scope for restricted Phase 1.
