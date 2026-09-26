#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: sudo deploy/vps/bootstrap.sh --confirm-vps --ssh-source CIDR [--ssh-port PORT]

Run only on a new, dedicated Ubuntu 24.04 amd64 VPS after confirming that the
provider recovery console works and an SSH public key is installed for the
sudo user. This script resets UFW rules, disables SSH password/root login,
installs Ubuntu Docker/Compose packages, and installs/enforces the Docker
published-port firewall. It does not deploy the application.

Public TCP 80 serves ACME/redirect; public IPv4 TCP 443 reaches the Steam-only
Master through Caddy. SSH remains restricted to the management CIDR. No
gameplay UDP or private Master/database/control port is published. Before
enabling a provider firewall, allow only management SSH, public TCP 80/443
and keep console access available for lockout recovery.
EOF
}
confirm_vps=false
ssh_source=
ssh_port=22
while (($#)); do
  case "$1" in
    --confirm-vps) confirm_vps=true; shift ;;
    --ssh-source) (($# >= 2)) || { usage >&2; exit 2; }; ssh_source=$2; shift 2 ;;
    --ssh-port) (($# >= 2)) || { usage >&2; exit 2; }; ssh_port=$2; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) usage >&2; exit 2 ;;
  esac
done

[[ $confirm_vps == true ]] || { echo 'refusing: pass --confirm-vps only on a dedicated VPS' >&2; exit 2; }
[[ $EUID == 0 ]] || { echo 'run with sudo' >&2; exit 1; }
[[ -r /etc/os-release ]] || { echo 'cannot identify operating system' >&2; exit 1; }
# shellcheck disable=SC1091
. /etc/os-release
[[ ${ID:-} == ubuntu && ${VERSION_ID:-} == 24.04 ]] || { echo 'requires Ubuntu 24.04' >&2; exit 1; }
[[ $(dpkg --print-architecture) == amd64 ]] || { echo 'requires amd64' >&2; exit 1; }
[[ -n $ssh_source ]] || { echo 'provide --ssh-source CIDR' >&2; exit 2; }
[[ $ssh_port =~ ^[0-9]+$ ]] && ((ssh_port >= 1 && ssh_port <= 65535)) || {
  echo 'SSH port must be 1-65535' >&2; exit 2;
}
ssh_source=$(python3 - "$ssh_source" <<'PY'
import ipaddress
import sys

network = ipaddress.ip_network(sys.argv[1], strict=False)
if network.prefixlen == 0:
    raise SystemExit(f"refusing unrestricted management source CIDR: {network}")
print(network)
PY
) || { echo 'invalid management CIDR' >&2; exit 2; }
[[ -n ${SUDO_USER:-} && $SUDO_USER != root ]] || {
  echo 'run through sudo as the non-root SSH operator account' >&2; exit 1;
}
operator_home=$(getent passwd "$SUDO_USER" | cut -d: -f6)
[[ -n $operator_home && -s $operator_home/.ssh/authorized_keys ]] || {
  echo "no SSH authorized_keys for sudo user $SUDO_USER; refusing SSH changes" >&2; exit 1;
}

# The operator supplies credentials and release records later; bootstrap only
# prepares private roots, never populates them or touches home-host data.
install -d -o root -g root -m 0700 /etc/sloparena /etc/sloparena/private /var/lib/sloparena

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y ufw iptables


# Rebuild a known-deny host firewall; Docker-published ports are separately
# constrained by DOCKER-USER because UFW does not filter them.
sed -i 's/^IPV6=.*/IPV6=yes/' /etc/default/ufw
ufw --force reset
ufw default deny incoming
ufw default allow outgoing
ufw allow from "$ssh_source" to any port "$ssh_port" proto tcp
ufw allow 80/tcp
ufw allow from 0.0.0.0/0 to any port 443 proto tcp
ufw --force enable

install -d -m 0755 /etc/ssh/sshd_config.d
cat > /etc/ssh/sshd_config.d/00-sloparena-vps.conf <<EOF
PubkeyAuthentication yes
PasswordAuthentication no
PermitRootLogin no
Port $ssh_port
EOF
sshd -t
systemctl reload ssh
apt-get install -y docker.io docker-compose-v2
install -D -m 0755 "$(dirname "$0")/docker-firewall.sh" /usr/local/sbin/sloparena-vps-docker-firewall
install -d -m 0755 /etc/systemd/system/docker.service.d
cat > /etc/systemd/system/docker.service.d/10-sloparena-firewall.conf <<'EOF'
[Service]
ExecStartPost=/usr/local/sbin/sloparena-vps-docker-firewall --verify
ExecStartPre=/usr/local/sbin/sloparena-vps-docker-firewall
EOF
systemctl daemon-reload
# Apply now as well as before every future Docker daemon start. Any failure
# leaves bootstrap unsuccessful; do not run the release procedure afterward.
/usr/local/sbin/sloparena-vps-docker-firewall
systemctl enable --now docker

echo 'Bootstrap complete. Verify provider firewall, DNS and SSH access before release; see deploy/vps/release.py --help.'
