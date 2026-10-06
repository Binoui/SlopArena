#!/usr/bin/env bash
set -euo pipefail

readonly interface=sloparena-ci
readonly server=10.253.253.1
readonly ssh_port=2223
readonly ssh_user=sloparena-ci
readonly state="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/sloparena-ci-network"

fail() { printf '%s\n' "$1" >&2; exit 1; }

case "${1-}" in
  up)
    [[ $# -eq 1 ]] || fail 'Usage: playtest-vps-network.sh up|check|down'
    [[ -n "${PLAYTEST_WIREGUARD_CONFIG-}" ]] || fail 'PLAYTEST_WIREGUARD_CONFIG is required'
    [[ "${GITHUB_ACTIONS-}" == true ]] || fail 'Tunnel setup is restricted to an isolated Actions runner'
    ! ip link show dev "$interface" >/dev/null 2>&1 || fail 'The CI tunnel interface already exists'
    [[ ! -e "$state" && ! -L "$state" ]] || fail 'Runner network state already exists'
    umask 077
    mkdir -m 700 -- "$state"
    config="$state/$interface.conf"
    printf '%s\n' "$PLAYTEST_WIREGUARD_CONFIG" >"$config"
    unset PLAYTEST_WIREGUARD_CONFIG
    # Reject wg-quick shell hooks and any route or peer outside the frozen tunnel contract.
    if ! awk '
      /^[[:space:]]*($|#)/ { next }
      /^\[Interface\][[:space:]]*$/ { section="Interface"; interfaces++; next }
      /^\[Peer\][[:space:]]*$/ { section="Peer"; peers++; next }
      /^[[:space:]]*\[/ { exit 1 }
      {
        split($0, part, "="); key=tolower(part[1]); value=part[2]
        gsub(/^[[:space:]]+|[[:space:]]+$/, "", key)
        gsub(/^[[:space:]]+|[[:space:]]+$/, "", value)
        if (section == "Interface") {
          if (key == "address" && value == "10.253.253.2/32") address++
          else if (key != "privatekey") exit 1
        } else if (section == "Peer") {
          if (key == "allowedips" && value == "10.253.253.1/32") routes++
          else if (key == "endpoint" && value == "135.125.100.228:51830") endpoint++
          else if (key != "publickey" && key != "persistentkeepalive") exit 1
        } else exit 1
      }
      END { if (interfaces != 1 || peers != 1 || address != 1 || routes != 1 || endpoint != 1) exit 1 }
    ' "$config"; then
      rm -rf -- "$state"
      fail 'WireGuard configuration does not match the private CI tunnel contract'
    fi
    if ! command -v wg >/dev/null || ! command -v wg-quick >/dev/null; then
      if ! sudo -n env DEBIAN_FRONTEND=noninteractive NEEDRESTART_MODE=l apt-get update >"$state/tooling.log" 2>&1 ||
         ! sudo -n env DEBIAN_FRONTEND=noninteractive NEEDRESTART_MODE=l apt-get install -y --no-install-recommends wireguard-tools >>"$state/tooling.log" 2>&1; then
        fail 'Could not install the required WireGuard tools; private setup output withheld'
      fi
    fi
    if ! sudo -n wg-quick up "$config" >/dev/null 2>&1; then
      fail 'Could not bring up the private CI WireGuard tunnel; unconditional teardown will remove private state'
    fi
    ;;
  check)
    [[ $# -eq 1 ]] || fail 'Usage: playtest-vps-network.sh up|check|down'
    [[ -d "$state" && ! -L "$state" ]] || fail 'Private CI tunnel is not initialized'
    [[ -n "${PLAYTEST_SSH_HOST-}" && -n "${PLAYTEST_SSH_PORT-}" && -n "${PLAYTEST_SSH_USER-}" && -n "${PLAYTEST_SSH_PRIVATE_KEY-}" && -n "${PLAYTEST_SSH_KNOWN_HOSTS-}" ]] || fail 'Pinned private SSH settings are required'
    [[ "$PLAYTEST_SSH_HOST" == "$server" && "$PLAYTEST_SSH_PORT" == "$ssh_port" && "$PLAYTEST_SSH_USER" == "$ssh_user" ]] || fail 'SSH target does not match the private CI tunnel contract'
    umask 077
    identity="$state/identity"
    known_hosts="$state/known_hosts"
    diagnostic="$state/ssh-diagnostic"
    printf '%s\n' "$PLAYTEST_SSH_PRIVATE_KEY" >"$identity"
    printf '%s\n' "$PLAYTEST_SSH_KNOWN_HOSTS" >"$known_hosts"
    chmod 600 "$identity" "$known_hosts"
    unset PLAYTEST_SSH_PRIVATE_KEY PLAYTEST_SSH_KNOWN_HOSTS
    ssh_base=(ssh -i "$identity" -p "$ssh_port" -o BatchMode=yes -o IdentitiesOnly=yes
      -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$known_hosts" -o ConnectTimeout=15
      -o LogLevel=ERROR)

    candidate_status=0
    printf '%s\n' '{' | "${ssh_base[@]}" "$ssh_user@$server" sloparena-release >/dev/null 2>"$diagnostic" || candidate_status=$?
    [[ "$candidate_status" -eq 1 ]] &&
      grep -Fxq 'candidate rejected: candidate JSON is malformed' "$diagnostic" ||
      fail 'VPS malformed-candidate validation was not proven'
    rm -f -- "$diagnostic"
    shell_status=0
    "${ssh_base[@]}" "$ssh_user@$server" 'id' >/dev/null 2>&1 || shell_status=$?
    [[ "$shell_status" -eq 126 ]] || fail 'VPS shell refusal did not return the forced-command refusal status'

    tty_status=0
    "${ssh_base[@]}" -tt "$ssh_user@$server" 'id' >/dev/null 2>"$diagnostic" || tty_status=$?
    [[ "$tty_status" -eq 126 ]] && grep -Eq 'PTY allocation request failed|PTY allocation disabled' "$diagnostic" || fail 'VPS did not refuse the TTY request'
    rm -f -- "$diagnostic"

    local_port="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')"
    [[ "$local_port" =~ ^[0-9]+$ ]] || fail 'Could not allocate a local forwarding probe port'
    "${ssh_base[@]}" -N -L "127.0.0.1:$local_port:$server:$ssh_port" "$ssh_user@$server" >/dev/null 2>"$diagnostic" &
    ssh_pid=$!
    trap 'kill "$ssh_pid" 2>/dev/null || true; wait "$ssh_pid" 2>/dev/null || true' EXIT
    forward_result=0
    python3 - "$local_port" 2>/dev/null <<'PY' || forward_result=$?
import socket, sys, time
port = int(sys.argv[1])
end = time.monotonic() + 20
while time.monotonic() < end:
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=1) as stream:
            stream.settimeout(8)
            try:
                data = stream.recv(1)
            except (ConnectionResetError, TimeoutError, socket.timeout):
                data = b""
            if data:
                raise SystemExit(1)
            raise SystemExit(0)
    except ConnectionRefusedError:
        time.sleep(0.2)
raise SystemExit(2)
PY
    kill "$ssh_pid" 2>/dev/null || true
    wait "$ssh_pid" 2>/dev/null || true
    trap - EXIT
    grep -q 'administratively prohibited' "$diagnostic" || fail 'VPS did not explicitly refuse TCP forwarding'
    rm -f -- "$diagnostic"
    [[ "$forward_result" -eq 0 ]] || fail 'VPS TCP forwarding was accepted or its refusal could not be verified'
    rm -f -- "$identity" "$known_hosts"
    printf '%s\n' 'Pinned private SSH verified; malformed candidate, shell, TTY and forwarding refused'
    ;;
  down)
    [[ $# -eq 1 ]] || fail 'Usage: playtest-vps-network.sh up|check|down'
    if [[ -d "$state" && ! -L "$state" ]]; then
      teardown_status=0
      if ip link show dev "$interface" >/dev/null 2>&1; then
        sudo -n wg-quick down "$state/$interface.conf" >/dev/null 2>&1 || teardown_status=$?
      fi
      rm -rf -- "$state"
      [[ "$teardown_status" -eq 0 ]] || fail 'CI tunnel teardown failed; private runner files were removed'
    fi
    ;;
  *) fail 'Usage: playtest-vps-network.sh up|check|down' ;;
esac
