#!/usr/bin/env bash
set -u

resolve_ipv4() {
  local target="$1"

  if [[ "$target" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]]; then
    printf '%s\n' "$target"
    return 0
  fi

  local ip=""
  if command -v dscacheutil >/dev/null 2>&1; then
    ip="$(dscacheutil -q host -a name "$target" 2>/dev/null | awk '/^ip_address: /{print $2; exit}')"
  fi
  if [[ -z "$ip" ]] && command -v dig >/dev/null 2>&1; then
    ip="$(dig +short A "$target" | head -n 1)"
  fi
  if [[ -z "$ip" ]] && command -v host >/dev/null 2>&1; then
    ip="$(host -t A "$target" 2>/dev/null | awk '/ has address /{print $4; exit}')"
  fi

  if [[ -n "$ip" ]]; then
    printf '%s\n' "$ip"
    return 0
  fi

  return 1
}

check_one() {
  local target="$1"
  local ip
  if ! ip="$(resolve_ipv4 "$target")"; then
    printf '[FAIL][resolve] %-45s -> resolve failed\n' "$target"
    return 1
  fi

  local url="http://ip-api.com/json/$ip"
  local body
  if ! body="$(curl -sS --max-time 12 "$url" 2>/dev/null)"; then
    printf '[FAIL][request] %-45s -> ip=%-15s request failed\n' "$target" "$ip"
    return 2
  fi

  local status country region city isp message
  status="$(printf '%s' "$body" | jq -r '.status // empty' 2>/dev/null)"
  country="$(printf '%s' "$body" | jq -r '.country // empty' 2>/dev/null)"
  region="$(printf '%s' "$body" | jq -r '.regionName // empty' 2>/dev/null)"
  city="$(printf '%s' "$body" | jq -r '.city // empty' 2>/dev/null)"
  isp="$(printf '%s' "$body" | jq -r '.isp // empty' 2>/dev/null)"
  message="$(printf '%s' "$body" | jq -r '.message // empty' 2>/dev/null)"

  if [[ "$status" == "success" ]]; then
    printf '[OK] %-45s -> ip=%-15s location=%s/%s/%s isp=%s\n' "$target" "$ip" "${country:-?}" "${region:-?}" "${city:-?}" "${isp:-?}"
    return 0
  fi

  printf '[FAIL][api] %-45s -> ip=%-15s status=%s message=%s\n' "$target" "$ip" "${status:-unknown}" "${message:-unknown}"
  return 3
}

main() {
  if ! command -v curl >/dev/null 2>&1; then
    echo "curl not found"
    exit 127
  fi
  if ! command -v jq >/dev/null 2>&1; then
    echo "jq not found (brew install jq)"
    exit 127
  fi

  if [[ "$#" -eq 0 ]]; then
    cat <<'EOF'
Usage:
  ./check-ip-api.sh <ip-or-domain> [more...]

Example:
  ./check-ip-api.sh 104.16.148.244 yg8.ygkkk.dpdns.org
EOF
    exit 1
  fi

  local rc=0
  for target in "$@"; do
    check_one "$target" || rc=$?
  done
  exit "$rc"
}

main "$@"
