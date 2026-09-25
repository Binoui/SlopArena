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
sudo python3 deploy/vps/release.py logs --target-dir /var/lib/sloparena
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
the allowed Docker ingress-rule packet count from zero to one; this proves
the network path, **not** a complete gameplay round trip. Direct HTTPS
over the VPS's global IPv6 timed out from an unallowed IPv6 source.

At this checkpoint, still to prove: non-allowlisted **IPv4** cannot use
HTTPS or gameplay UDP; an allowlisted packaged Unity client can finish a match;
VPS reboot and off-host restore preserve service operation. Those are
distinct from this release's readiness and firewall evidence.
