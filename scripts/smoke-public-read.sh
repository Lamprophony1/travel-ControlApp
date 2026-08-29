#!/usr/bin/env bash
set -Eeuo pipefail

BASE_URL="${1:-}"
[[ "${BASE_URL}" =~ ^https://[A-Za-z0-9.-]+$ ]] || { echo "Usage: $0 https://host" >&2; exit 1; }
for command in curl grep mktemp rm; do command -v "${command}" >/dev/null; done

WORK_DIR="$(mktemp -d)"
cleanup() {
  case "${WORK_DIR}" in
    /tmp/*) rm -rf -- "${WORK_DIR}" ;;
    *) echo "Refusing to remove unexpected temporary path" >&2 ;;
  esac
}
trap cleanup EXIT

curl --fail --silent --show-error --retry 5 --retry-delay 2 \
  -D "${WORK_DIR}/dashboard.headers" "${BASE_URL}/api/public/dashboard" \
  -o "${WORK_DIR}/dashboard.json"
curl --fail --silent --show-error --retry 5 --retry-delay 2 \
  "${BASE_URL}/api/public/passengers?page=1&pageSize=1" \
  -o "${WORK_DIR}/passengers.json"
curl --fail --silent --show-error --retry 5 --retry-delay 2 \
  "${BASE_URL}/" -o "${WORK_DIR}/index.html"
curl --fail --silent --show-error --retry 5 --retry-delay 2 \
  "${BASE_URL}/pasajeros" -o "${WORK_DIR}/passengers.html"

grep -Eiq '^cache-control:.*no-store' "${WORK_DIR}/dashboard.headers" \
  || { echo "Public API is missing Cache-Control: no-store" >&2; exit 1; }
grep -Eiq '^x-robots-tag:.*noindex' "${WORK_DIR}/dashboard.headers" \
  || { echo "Public API is missing X-Robots-Tag: noindex" >&2; exit 1; }

if grep -Eiq '"(passportNumber|normalizedPassportNumber|phone|email|birthDate|notes|sourceReference|operatorContact|electronicTicketNumber|auditLogs|attachments|securePath|storedName|userName|ipContext)"[[:space:]]*:' \
  "${WORK_DIR}/dashboard.json" "${WORK_DIR}/passengers.json"; then
  echo "A forbidden property was exposed by a public endpoint" >&2
  exit 1
fi

private_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "${BASE_URL}/api/passengers")"
[[ "${private_status}" == "401" ]] || { echo "Private API returned ${private_status}; expected 401" >&2; exit 1; }

echo "Public read-only and privacy smoke tests passed for ${BASE_URL}."
