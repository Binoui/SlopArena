# Local restricted full-stack proof (#231)

This is **not** the VPS deployment profile. HTTPS and five gameplay UDP ports
bind only to host loopback. The current player endpoint and home backend stay
untouched. Application images come from their owning checkouts; neither Master
nor GameServer mounts source. Local tags are not published image digests.

## Inputs

Copy `deploy/local/stack.env.example` to ignored `deploy/local/stack.env`.
Keep `MASTER_TEST_HOST` and `GAMEPLAY_TEST_HOST` distinct and away from the
home tunnel. The examples use `master.localhost` and `game.localhost`, which
resolve to loopback on the tested Linux host without changing public DNS.
Verify that both resolve to loopback on each tester before connecting.
Select the exact local image tags being tested.

Create ignored `deploy/local/private/` with mode 700. Copy the three tracked
`*.env.example`/`game.json.example` templates into it and fill every blank.
Generate independent DB, JWT, host-registration and match-control secrets;
never reuse home credentials. Put the DB password in `private/postgres-password`
and in the connection strings in `private/master.env` and
`private/migration.env`. The latter carries **only** that connection string.
The Master connection uses
`Host=postgres;Port=5432;Database=sloparena;Username=sloparena;Password=...`.
Both applications must have the same stable host GUID and registration/control
keys. GameServer advertises `game.localhost`, calls `http://master:8080`,
uses port 7777 with five match slots, and reads `data/arenas`.

Keep host-owned private files mode 600. The GameServer image runs as UID 1654;
allow only that UID to read the bind-mounted `game.json` using
`setfacl -m u:1654:r deploy/local/private/game.json` after `chmod 600`.
The parent `private/` directory stays mode 700. Never put credentials in
Compose, shell history, tracked examples, logs, or a client build.

Compose passes `GAMEPLAY_TEST_HOST` to Master as its approved public host and
provisions `http://game:7777/match/start` as the separate private control URL.
Caddy uses `172.30.11.10`; Master trusts exactly that address for one forwarded
hop. If the subnet collides locally, change the Compose subnet, Caddy address
and `Proxy__TrustedAddress` **together**.

## Build, migrate, run

From the game checkout, build images locally; do not log into or push to a
registry:

```bash
docker build --platform linux/amd64 -f Dockerfile.gameserver \
  -t sloparena-gameserver:local-231 .
docker build --platform linux/amd64 --target master \
  -t sloparena-master:local-231 ../SlopArena-MasterServer
docker build --platform linux/amd64 --target migrations \
  -t sloparena-master-migrations:local-231 ../SlopArena-MasterServer

docker compose --env-file deploy/local/stack.env -f deploy/local/compose.yaml config --quiet
docker compose --env-file deploy/local/stack.env -f deploy/local/compose.yaml up -d postgres
docker compose --env-file deploy/local/stack.env -f deploy/local/compose.yaml \
  --profile migration run --rm migrate
docker compose --env-file deploy/local/stack.env -f deploy/local/compose.yaml up -d
```

Do not migrate automatically on application startup. Back up a reused
PostgreSQL database before schema changes; never use `down -v`. The apps run
as UID 1654 with read-only filesystems and bounded writable `/tmp` scratch.
PostgreSQL and Caddy persist data/config in named volumes. Master joins proxy,
database and control networks; GameServer joins **only** control. Only Caddy
publishes HTTPS; Master, PostgreSQL and GameServer TCP control publish no host
ports. Exactly UDP **7777–7781** is bound to host loopback, matching the five
match slots. Healthchecks use liveness, not readiness, so dependency outages
do not kill active matches. Logs use Docker's bounded `local` driver.

## Observe and stop

Caddy's test CA persists in `caddy_data`. Export its public root and verify
HTTPS without `curl -k`:

```bash
docker compose --env-file deploy/local/stack.env -f deploy/local/compose.yaml \
  exec -T caddy cat /data/caddy/pki/authorities/local/root.crt > /tmp/sloparena-local-root.crt
curl --cacert /tmp/sloparena-local-root.crt \
  --resolve master.localhost:8443:127.0.0.1 \
  https://master.localhost:8443/health
```

Check `/ready` independently. Packaged clients need the test CA trusted in
a controlled test environment and the Master endpoint override set **before**
guest auth. Map both test names to loopback on the tester only. Gameplay UDP
reaches `game.localhost:7777–7781` directly, never Caddy. Prove WebSocket
and long polling separately; compare client admission content with GameServer
Shared state and complete a direct-UDP match.

Use the same `docker compose --env-file ... -f ...` prefix with `ps`,
`logs --since 10m <service>`, and `down` (never `down -v`) to inspect or
stop. No cloud, registry or Docker-socket credential is mounted in an app.
Local proof does **not** establish public DNS, source-IP firewall restrictions,
IPv6 policy, VPS reboot, two-human play or off-host restore. Do not expose
this local profile to the Internet.

## Observed local run (2026-09-25)

These **local, uncommitted** images are identified by Docker image ID, **not**
registry digests or a reproducible release record:

| Image | Local image ID |
| --- | --- |
| GameServer | `sha256:5a118d3577f0297d05a73b9de7b1f33efa2e5d04c10340732e1d19e2007dd8a9` |
| Master | `sha256:0d424a3eef9ecbf4db28c7a1810fa74a4ccde5a2b67d2223784a6a15ded167b5` |
| EF migration | `sha256:46f88d704406e4a6f09c311982a5655e2979d1d39e0783f451faf84d0fea2263` |

Compose accepted the versioned config, ran the migration image against
PostgreSQL **15.19**, and started Caddy **2.11.4**. The support image index
digests are pinned in `compose.yaml`. `docker inspect` showed only
`127.0.0.1:7777–7781/udp` for GameServer; Master and PostgreSQL had no host
port bindings. GameServer and Master ran as UID 1654 with read-only roots;
only GameServer's external config was mounted read-only.

The exported Caddy CA verified HTTPS without `-k`; HTTP port 18080 returned
308 to local HTTPS port 8443. Two **headless** authenticated clients used
WebSocket and long polling, exchanged Global and Server chat, joined and
selected characters, and received the GameServer's four-package content map.
Direct UDP inputs produced an authoritative result packet on port 7777;
Master persisted a finished match and winner. Across the actual Caddy route,
one proxy-network client (source `172.30.11.20`) sent changing spoofed
`X-Forwarded-For` values: 10 guest writes passed, the 11th returned 429.
A distinct client (`172.30.11.21`) still received 200. GameServer stayed
healthy with the complete cooked roster and reported 503 readiness without
registration or with an invalid roster overlay.

During PostgreSQL loss, Master `/health` stayed 200 and `/ready` became 503;
readiness recovered to 200 without restarting either app. A held UDP match
stayed active and completed after PostgreSQL returned, and its result was
persisted. SIGTERM during a separate active UDP listener deregistered
GameServer, stopped workers and released the UDP port; restarting the image
re-registered one approved host without restarting Master. Caddy's CA hash
remained identical after its container restart.

**Observed limit:** Two matches that ended *while PostgreSQL was stopped*
still emitted client UDP results, but Master returned 500 for result reports;
their `Matches.EndedAt` remained null. This task does not add result retries
or claim those reports survived the outage. The test clients were protocol
peers, **not** packaged Unity players; no public ingress, VPS reboot, or
real two-human acceptance was performed. Images were not published at the
operator's request, so exact registry digests and a release identity remain
pending.

The services were stopped with `docker compose down` (without `-v`); named
PostgreSQL/Caddy volumes remain. Disposable test credentials and clients were
removed afterward. Reusing the PostgreSQL volume requires its original
test-only password; start a fresh isolated target instead of silently
discarding this volume if that password is unavailable.
