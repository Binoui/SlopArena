#!/usr/bin/env bash
set -euo pipefail

allowlist=/etc/sloparena-vps/tester-cidrs
for tool in iptables ip6tables; do
  command -v "$tool" >/dev/null || { echo "missing required firewall tool: $tool" >&2; exit 1; }
done
[[ -f $allowlist && $(stat -c %u "$allowlist") == 0 ]] || {
  echo "missing root-owned tester allowlist: $allowlist" >&2; exit 1;
}
[[ $((8#$(stat -c %a "$allowlist") & 18)) == 0 ]] || {
  echo "tester allowlist must not be group/other writable" >&2; exit 1;
}
normalized_cidrs=$(python3 - "$allowlist" <<'PY'
import ipaddress
import sys

with open(sys.argv[1], encoding="ascii") as source:
    lines = [line.strip() for line in source if line.strip()]
if not lines:
    raise SystemExit("tester allowlist is empty")
for line in lines:
    network = ipaddress.ip_network(line, strict=True)
    if network.prefixlen == 0:
        raise SystemExit(f"unrestricted tester network: {line}")
    print(network)
PY
) || { echo "tester allowlist is invalid" >&2; exit 1; }
mapfile -t cidrs <<< "$normalized_cidrs"
[[ -n ${cidrs[0]:-} ]] || { echo "tester allowlist is empty" >&2; exit 1; }

expected_policy() {
  local tool=$1 chain=$2 cidr
  printf '%s\n' \
    "-A $chain -m conntrack --ctstate RELATED,ESTABLISHED -j RETURN" \
    "-A $chain -i docker0 -j RETURN" \
    "-A $chain -i br+ -j RETURN" \
    "-A $chain -p tcp --dport 80 -j RETURN"
  for cidr in "${cidrs[@]}"; do
    if [[ $cidr == *:* && $tool == iptables || $cidr != *:* && $tool == ip6tables ]]; then
      continue
    fi
    printf '%s\n' \
      "-A $chain -s $cidr -p tcp --dport 443 -j RETURN" \
      "-A $chain -s $cidr -p udp --dport 7777:7781 -j RETURN"
  done
  printf '%s\n' "-A $chain -j DROP"
}

verify_family() {
  local tool=$1 active count expected_count rule actual_count
  local -a args
  "$tool" -w -C FORWARD -j DOCKER-USER
  [[ $("$tool" -w -S FORWARD | sed -n '2p') == '-A FORWARD -j DOCKER-USER' ]] || {
    echo "$tool DOCKER-USER is not first in FORWARD" >&2; return 1;
  }
  active=$("$tool" -w -S DOCKER-USER | sed -n 's/^-A DOCKER-USER -j \(SLOPARENA-VPS-[AB]\)$/\1/p' | sed -n '1p')
  count=$("$tool" -w -S DOCKER-USER | grep -Ec '^-A DOCKER-USER -j SLOPARENA-VPS-[AB]$')
  [[ -n $active && $count == 1 && $("$tool" -w -S DOCKER-USER | sed -n '2p') == "-A DOCKER-USER -j $active" ]] || {
    echo "$tool DOCKER-USER is not uniquely linked first" >&2; return 1;
  }
  expected_count=0
  while IFS= read -r rule; do
    rule=${rule#"-A $active "}
    read -r -a args <<< "$rule"
    "$tool" -w -C "$active" "${args[@]}"
    ((expected_count+=1))
  done < <(expected_policy "$tool" "$active")
  actual_count=$("$tool" -w -S "$active" | grep -c "^-A $active")
  [[ $actual_count == "$expected_count" ]] || {
    echo "$tool $active contains unexpected firewall rules" >&2; return 1;
  }
}

if [[ ${1:-} == --verify ]]; then
  verify_family iptables
  verify_family ip6tables
  exit 0
fi
[[ $# == 0 ]] || { echo "usage: $0 [--verify]" >&2; exit 2; }

apply_family() {
  local tool=$1 active= staging= rule chain jump_count index
  local -a args forward_rules
  "$tool" -w -N DOCKER-USER 2>/dev/null || true
  for chain in SLOPARENA-VPS-A SLOPARENA-VPS-B; do
    "$tool" -w -N "$chain" 2>/dev/null || true
  done
  active=$("$tool" -w -S DOCKER-USER | sed -n 's/^-A DOCKER-USER -j \(SLOPARENA-VPS-[AB]\)$/\1/p' | sed -n '1p')
  if [[ -z $active ]]; then
    # Start fail-closed before connecting DOCKER-USER to packet forwarding.
    "$tool" -w -F SLOPARENA-VPS-A
    "$tool" -w -A SLOPARENA-VPS-A -j DROP
    "$tool" -w -I DOCKER-USER 1 -j SLOPARENA-VPS-A
    active=SLOPARENA-VPS-A
  fi
  jump_count=$("$tool" -w -S DOCKER-USER | grep -Ec '^-A DOCKER-USER -j SLOPARENA-VPS-[AB]$')
  [[ $jump_count == 1 && $("$tool" -w -S DOCKER-USER | sed -n '2p') == "-A DOCKER-USER -j $active" ]] || {
    echo "$tool DOCKER-USER policy hook is not uniquely first; refusing update" >&2; return 1;
  }
  if [[ $active == SLOPARENA-VPS-A ]]; then staging=SLOPARENA-VPS-B; else staging=SLOPARENA-VPS-A; fi
  "$tool" -w -F "$staging"
  while IFS= read -r rule; do
    rule=${rule#"-A $staging "}
    read -r -a args <<< "$rule"
    "$tool" -w -A "$staging" "${args[@]}"
  done < <(expected_policy "$tool" "$staging")
  # One atomic jump replacement: never flush the active policy chain.
  "$tool" -w -R DOCKER-USER 1 -j "$staging"

  forward_rules=()
  mapfile -t forward_rules < <("$tool" -w -S FORWARD | grep '^-A FORWARD')
  if [[ ${forward_rules[0]:-} != '-A FORWARD -j DOCKER-USER' ]]; then
    "$tool" -w -I FORWARD 1 -j DOCKER-USER
  fi
  mapfile -t forward_rules < <("$tool" -w -S FORWARD | grep '^-A FORWARD')
  for ((index=${#forward_rules[@]}; index > 1; index--)); do
    if [[ ${forward_rules[index-1]} == '-A FORWARD -j DOCKER-USER' ]]; then
      "$tool" -w -D FORWARD "$index"
    fi
  done
}

apply_family iptables
apply_family ip6tables
verify_family iptables
verify_family ip6tables
