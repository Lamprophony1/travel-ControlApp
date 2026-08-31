#!/usr/bin/env bash
set -Eeuo pipefail

APP_ROOT="${TRAVELCONTROL_ROOT:-/opt/travel-control}"
DB_PATH="${TRAVELCONTROL_DB_PATH:-${APP_ROOT}/data/travel-control.db}"
REQUIRE_BASELINE=""
POST_DEPLOY=false
for argument in "$@"; do
  case "${argument}" in
    --require-baseline) REQUIRE_BASELINE="--require-baseline" ;;
    --post-deploy) POST_DEPLOY=true ;;
    *) echo "Unknown argument" >&2; exit 2 ;;
  esac
done

[[ "$(realpath -m "${APP_ROOT}")" == "/opt/travel-control" ]] || { echo "Invalid app root" >&2; exit 1; }
[[ -s "${DB_PATH}" ]] || { echo "Production database is missing or empty" >&2; exit 1; }
for command in docker sqlite3 realpath find; do command -v "${command}" >/dev/null; done

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
attachment_files="$(find "${APP_ROOT}/attachments" -maxdepth 1 -type f | wc -l | tr -d ' ')"
flight_bookings="$(scalar 'SELECT COUNT(*) FROM "FlightBookings" WHERE "Pnr" IS NOT NULL AND length(trim("Pnr")) > 0;')"
ticketed_passengers="$(scalar 'SELECT COUNT(DISTINCT pf."PassengerId") FROM "PassengerFlights" pf JOIN "FlightBookings" f ON f."Id"=pf."FlightBookingId" WHERE pf."TicketStatus"=0 AND f."Pnr" IS NOT NULL AND length(trim(f."Pnr")) > 0 AND f."Airline" IS NOT NULL AND length(trim(f."Airline")) > 0;')"
copa_passengers="$(scalar "SELECT COUNT(DISTINCT pf.\"PassengerId\") FROM \"PassengerFlights\" pf JOIN \"FlightBookings\" f ON f.\"Id\"=pf.\"FlightBookingId\" WHERE pf.\"TicketStatus\"=0 AND (f.\"Airline\" LIKE 'Copa%' OR upper(trim(f.\"Airline\"))='CM');")"
latam_passengers="$(scalar "SELECT COUNT(DISTINCT pf.\"PassengerId\") FROM \"PassengerFlights\" pf JOIN \"FlightBookings\" f ON f.\"Id\"=pf.\"FlightBookingId\" WHERE pf.\"TicketStatus\"=0 AND (f.\"Airline\" LIKE 'LATAM%' OR upper(trim(f.\"Airline\"))='LA');")"
passengers_without_ticket="$(( passengers - ticketed_passengers ))"
baggage_confirmed="not_migrated"
baggage_pending="not_migrated"
baggage_not_included="not_migrated"
ticket_access_verified="not_migrated"
ticket_access_generated="not_migrated"
ticket_access_missing="not_migrated"

[[ "${baggage_duplicates}" == "0" ]] || { echo "Duplicate baggage records detected; migration stopped" >&2; exit 1; }
[[ "${attachment_hash_duplicates}" == "0" ]] || { echo "Duplicate attachment hashes detected; migration stopped" >&2; exit 1; }

if [[ "${POST_DEPLOY}" == true ]]; then
  evidence_column="$(scalar "SELECT COUNT(*) FROM pragma_table_info('AttachmentLinks') WHERE name = 'EvidenceType';")"
  passenger_flight_version_column="$(scalar "SELECT COUNT(*) FROM pragma_table_info('PassengerFlights') WHERE name = 'Version';")"
  ticket_access_columns="$(scalar "SELECT COUNT(*) FROM pragma_table_info('PassengerFlights') WHERE name IN ('BookingLookupLastName','AirlineOrderId','TicketAccessUrl','TicketAccessStatus','PublicTicketAccessToken','TicketAccessGeneratedAt','TicketAccessVerifiedAt');")"
  flight_baggage_columns="$(scalar "SELECT COUNT(*) FROM pragma_table_info('FlightBookings') WHERE name IN ('BaggageStatus','CheckedBagIncluded','CheckedBagCount','CheckedBagWeightKg','BaggageAppliesOutbound','BaggageAppliesReturn','BaggageSourceReference','BaggageNotes','BaggageVerifiedAt','BaggageVerifiedById');")"
  [[ "${evidence_column}" == "1" ]] || { echo "EvidenceType column is missing" >&2; exit 1; }
  [[ "${passenger_flight_version_column}" == "1" ]] || { echo "PassengerFlight Version column is missing" >&2; exit 1; }
  [[ "${ticket_access_columns}" == "7" ]] || { echo "Ticket access columns are missing" >&2; exit 1; }
  [[ "${flight_baggage_columns}" == "10" ]] || { echo "Flight baggage columns are missing" >&2; exit 1; }
  invalid_evidence_types="$(scalar 'SELECT COUNT(*) FROM "AttachmentLinks" WHERE "EvidenceType" NOT BETWEEN 0 AND 4;')"
  invalid_link_targets="$(scalar 'SELECT COUNT(*) FROM "AttachmentLinks" WHERE (("PassengerId" IS NOT NULL) + ("RoomReservationId" IS NOT NULL) + ("FlightBookingId" IS NOT NULL) + ("BaggageEntitlementId" IS NOT NULL)) <> 1;')"
  duplicate_typed_links="$(scalar "SELECT COUNT(*) FROM (SELECT \"AttachmentId\", CASE WHEN \"PassengerId\" IS NOT NULL THEN 'P:' || \"PassengerId\" WHEN \"RoomReservationId\" IS NOT NULL THEN 'R:' || \"RoomReservationId\" WHEN \"FlightBookingId\" IS NOT NULL THEN 'F:' || \"FlightBookingId\" ELSE 'B:' || \"BaggageEntitlementId\" END AS target, \"EvidenceType\" FROM \"AttachmentLinks\" GROUP BY \"AttachmentId\", target, \"EvidenceType\" HAVING COUNT(*) > 1);")"
  legacy_links_missing="$(scalar 'SELECT COUNT(*) FROM "Attachments" a WHERE (a."PassengerId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "AttachmentLinks" l WHERE l."AttachmentId"=a."Id" AND l."PassengerId"=a."PassengerId" AND l."EvidenceType"=a."DocumentType")) OR (a."RoomReservationId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "AttachmentLinks" l WHERE l."AttachmentId"=a."Id" AND l."RoomReservationId"=a."RoomReservationId" AND l."EvidenceType"=a."DocumentType")) OR (a."FlightBookingId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "AttachmentLinks" l WHERE l."AttachmentId"=a."Id" AND l."FlightBookingId"=a."FlightBookingId" AND l."EvidenceType"=a."DocumentType")) OR (a."BaggageEntitlementId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "AttachmentLinks" l WHERE l."AttachmentId"=a."Id" AND l."BaggageEntitlementId"=a."BaggageEntitlementId" AND l."EvidenceType"=a."DocumentType"));')"
  invalid_ticket_versions="$(scalar 'SELECT COUNT(*) FROM "PassengerFlights" WHERE "Version" < 1;')"
  duplicate_tickets="$(scalar 'SELECT COUNT(*) FROM (SELECT "PassengerId", "FlightBookingId" FROM "PassengerFlights" GROUP BY "PassengerId", "FlightBookingId" HAVING COUNT(*) > 1);')"
  duplicate_public_tokens="$(scalar 'SELECT COUNT(*) FROM (SELECT "PublicTicketAccessToken" FROM "PassengerFlights" GROUP BY "PublicTicketAccessToken" HAVING COUNT(*) > 1);')"
  missing_public_tokens="$(scalar 'SELECT COUNT(*) FROM "PassengerFlights" WHERE "PublicTicketAccessToken" IS NULL OR length(trim("PublicTicketAccessToken")) < 43;')"
  [[ "${invalid_evidence_types}" == "0" ]] || { echo "Invalid evidence types detected" >&2; exit 1; }
  [[ "${invalid_link_targets}" == "0" ]] || { echo "Invalid attachment link targets detected" >&2; exit 1; }
  [[ "${duplicate_typed_links}" == "0" ]] || { echo "Duplicate typed attachment links detected" >&2; exit 1; }
  [[ "${legacy_links_missing}" == "0" ]] || { echo "Legacy attachment associations were not migrated" >&2; exit 1; }
  [[ "${invalid_ticket_versions}" == "0" ]] || { echo "Invalid ticket versions detected" >&2; exit 1; }
  [[ "${duplicate_tickets}" == "0" ]] || { echo "Duplicate passenger tickets detected" >&2; exit 1; }
  [[ "${duplicate_public_tokens}" == "0" ]] || { echo "Duplicate public ticket tokens detected" >&2; exit 1; }
  [[ "${missing_public_tokens}" == "0" ]] || { echo "Missing or weak public ticket tokens detected" >&2; exit 1; }
  baggage_confirmed="$(scalar 'SELECT COUNT(*) FROM "FlightBookings" WHERE "BaggageStatus"=0;')"
  baggage_pending="$(scalar 'SELECT COUNT(*) FROM "FlightBookings" WHERE "BaggageStatus" IN (1,2);')"
  baggage_not_included="$(scalar 'SELECT COUNT(*) FROM "FlightBookings" WHERE "BaggageStatus"=3;')"
  ticket_access_verified="$(scalar 'SELECT COUNT(*) FROM "PassengerFlights" WHERE "TicketAccessStatus"=2;')"
  ticket_access_generated="$(scalar 'SELECT COUNT(*) FROM "PassengerFlights" WHERE "TicketAccessStatus"=1;')"
  ticket_access_missing="$(scalar 'SELECT COUNT(*) FROM "PassengerFlights" WHERE "TicketAccessStatus"=0;')"
fi

if [[ "${REQUIRE_BASELINE}" == "--require-baseline" ]]; then
  [[ "${passengers}" == "46" ]] || { echo "Expected 46 passengers; found ${passengers}" >&2; exit 1; }
  [[ "${rooms}" == "25" ]] || { echo "Expected 25 rooms; found ${rooms}" >&2; exit 1; }
  [[ "${top_travel_passengers}" == "44" ]] || { echo "Expected 44 Top Travel passengers; found ${top_travel_passengers}" >&2; exit 1; }
  [[ "${bespoke_passengers}" == "2" ]] || { echo "Expected 2 Bespoke passengers; found ${bespoke_passengers}" >&2; exit 1; }
  [[ "${top_travel_rooms}" == "24" ]] || { echo "Expected 24 Top Travel rooms; found ${top_travel_rooms}" >&2; exit 1; }
  [[ "${bespoke_rooms}" == "1" ]] || { echo "Expected 1 Bespoke room; found ${bespoke_rooms}" >&2; exit 1; }
  [[ "${ticketed_passengers}" == "42" ]] || { echo "Expected 42 ticketed passengers; found ${ticketed_passengers}" >&2; exit 1; }
  [[ "${copa_passengers}" == "29" ]] || { echo "Expected 29 Copa passengers; found ${copa_passengers}" >&2; exit 1; }
  [[ "${latam_passengers}" == "13" ]] || { echo "Expected 13 LATAM passengers; found ${latam_passengers}" >&2; exit 1; }
  [[ "${passengers_without_ticket}" == "4" ]] || { echo "Expected 4 passengers without ticket; found ${passengers_without_ticket}" >&2; exit 1; }
fi

printf '%s\n' \
  "integrity=${integrity}" \
  "passengers=${passengers}" \
  "rooms=${rooms}" \
  "users=${users}" \
  "attachments=${attachments}" \
  "attachment_files=${attachment_files}" \
  "top_travel_passengers=${top_travel_passengers}" \
  "bespoke_passengers=${bespoke_passengers}" \
  "top_travel_rooms=${top_travel_rooms}" \
  "bespoke_rooms=${bespoke_rooms}" \
  "flight_bookings=${flight_bookings}" \
  "ticketed_passengers=${ticketed_passengers}" \
  "copa_passengers=${copa_passengers}" \
  "latam_passengers=${latam_passengers}" \
  "passengers_without_ticket=${passengers_without_ticket}" \
  "flight_bookings_baggage_confirmed=${baggage_confirmed}" \
  "flight_bookings_baggage_pending=${baggage_pending}" \
  "flight_bookings_baggage_not_included=${baggage_not_included}" \
  "ticket_access_verified=${ticket_access_verified}" \
  "ticket_access_generated=${ticket_access_generated}" \
  "ticket_access_missing=${ticket_access_missing}" \
  "baggage_duplicates=${baggage_duplicates}" \
  "attachment_hash_duplicates=${attachment_hash_duplicates}"
