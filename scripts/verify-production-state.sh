#!/usr/bin/env bash
set -Eeuo pipefail

APP_ROOT="${TRAVELCONTROL_ROOT:-/opt/travel-control}"
DB_PATH="${TRAVELCONTROL_DB_PATH:-${APP_ROOT}/data/travel-control.db}"
REQUIRE_BASELINE="${1:-}"

[[ "$(realpath -m "${APP_ROOT}")" == "/opt/travel-control" ]] || { echo "Invalid app root" >&2; exit 1; }
[[ -s "${DB_PATH}" ]] || { echo "Production database is missing or empty" >&2; exit 1; }
for command in docker sqlite3 realpath; do command -v "${command}" >/dev/null; done

CONTAINER_IMAGE="$(docker inspect --format '{{.Config.Image}}' travel-control 2>/dev/null || true)"
PERMISSION_IMAGE="${TRAVELCONTROL_IMAGE:-${CONTAINER_IMAGE}}"
[[ -n "${PERMISSION_IMAGE}" ]] || { echo "Cannot determine the Travel Control image" >&2; exit 1; }

# The live application can recreate SQLite WAL/SHM files with its internal
# group. Normalize all database files immediately before host-side checks so
# the gc runner can inspect a live database without changing its owner UID.
docker run --rm --network none --read-only --user 0:0 --entrypoint sh \
  -v "${APP_ROOT}/data:/persistent" \
  "${PERMISSION_IMAGE}" -c '
    chown 10001:1001 /persistent
    chmod 0770 /persistent
    for name in travel-control.db travel-control.db-shm travel-control.db-wal; do
      path="/persistent/${name}"
      if [ -e "${path}" ]; then chown 10001:1001 "${path}"; chmod 0660 "${path}"; fi
    done
  '

integrity="$(sqlite3 "${DB_PATH}" 'PRAGMA integrity_check;')"
[[ "${integrity}" == "ok" ]] || { echo "SQLite integrity check failed" >&2; exit 1; }

scalar() {
  sqlite3 -batch -noheader "${DB_PATH}" "$1"
}

passengers="$(scalar 'SELECT COUNT(*) FROM "Passengers";')"
rooms="$(scalar 'SELECT COUNT(*) FROM "RoomReservations";')"
users="$(scalar 'SELECT COUNT(*) FROM "AspNetUsers";')"
attachments="$(scalar 'SELECT COUNT(*) FROM "Attachments";')"
top_travel_passengers="$(scalar "SELECT COUNT(*) FROM \"Passengers\" p JOIN \"Operators\" o ON o.\"Id\" = p.\"PrimaryOperatorId\" WHERE o.\"Name\" = 'Top Travel';")"
bespoke_passengers="$(scalar "SELECT COUNT(*) FROM \"Passengers\" p JOIN \"Operators\" o ON o.\"Id\" = p.\"PrimaryOperatorId\" WHERE o.\"Name\" = 'Bespoke';")"
top_travel_rooms="$(scalar "SELECT COUNT(*) FROM \"RoomReservations\" r JOIN \"Operators\" o ON o.\"Id\" = r.\"OperatorId\" WHERE o.\"Name\" = 'Top Travel';")"
bespoke_rooms="$(scalar "SELECT COUNT(*) FROM \"RoomReservations\" r JOIN \"Operators\" o ON o.\"Id\" = r.\"OperatorId\" WHERE o.\"Name\" = 'Bespoke';")"
baggage_duplicates="$(scalar 'SELECT COUNT(*) FROM (SELECT "PassengerId", "FlightBookingId" FROM "BaggageEntitlements" WHERE "FlightBookingId" IS NOT NULL GROUP BY "PassengerId", "FlightBookingId" HAVING COUNT(*) > 1);')"
attachment_hash_duplicates="$(scalar 'SELECT COUNT(*) FROM (SELECT "Sha256" FROM "Attachments" GROUP BY "Sha256" HAVING COUNT(*) > 1);')"

[[ "${baggage_duplicates}" == "0" ]] || { echo "Duplicate baggage records detected; migration stopped" >&2; exit 1; }
[[ "${attachment_hash_duplicates}" == "0" ]] || { echo "Duplicate attachment hashes detected; migration stopped" >&2; exit 1; }

if [[ "${REQUIRE_BASELINE}" == "--require-baseline" ]]; then
  [[ "${passengers}" == "46" ]] || { echo "Expected 46 passengers; found ${passengers}" >&2; exit 1; }
  [[ "${rooms}" == "25" ]] || { echo "Expected 25 rooms; found ${rooms}" >&2; exit 1; }
  [[ "${top_travel_passengers}" == "44" ]] || { echo "Expected 44 Top Travel passengers; found ${top_travel_passengers}" >&2; exit 1; }
  [[ "${bespoke_passengers}" == "2" ]] || { echo "Expected 2 Bespoke passengers; found ${bespoke_passengers}" >&2; exit 1; }
  [[ "${top_travel_rooms}" == "24" ]] || { echo "Expected 24 Top Travel rooms; found ${top_travel_rooms}" >&2; exit 1; }
  [[ "${bespoke_rooms}" == "1" ]] || { echo "Expected 1 Bespoke room; found ${bespoke_rooms}" >&2; exit 1; }
fi

printf '%s\n' \
  "integrity=${integrity}" \
  "passengers=${passengers}" \
  "rooms=${rooms}" \
  "users=${users}" \
  "attachments=${attachments}" \
  "top_travel_passengers=${top_travel_passengers}" \
  "bespoke_passengers=${bespoke_passengers}" \
  "top_travel_rooms=${top_travel_rooms}" \
  "bespoke_rooms=${bespoke_rooms}" \
  "baggage_duplicates=${baggage_duplicates}" \
  "attachment_hash_duplicates=${attachment_hash_duplicates}"
