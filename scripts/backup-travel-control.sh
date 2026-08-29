#!/usr/bin/env bash
set -Eeuo pipefail

APP_ROOT="${TRAVELCONTROL_ROOT:-/opt/travel-control}"
DB_PATH="${TRAVELCONTROL_DB_PATH:-${APP_ROOT}/data/travel-control.db}"
BACKUP_ROOT="${TRAVELCONTROL_BACKUP_ROOT:-${APP_ROOT}/backups}"
RETENTION_DAYS="${TRAVELCONTROL_BACKUP_RETENTION_DAYS:-30}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
DESTINATION="${BACKUP_ROOT}/${STAMP}"

[[ "$(realpath -m "${APP_ROOT}")" == "/opt/travel-control" ]] || { echo "Invalid app root" >&2; exit 1; }
[[ -f "${DB_PATH}" ]] || { echo "Database does not exist; no backup was created."; exit 0; }
for command in sqlite3 sha256sum realpath find docker; do command -v "${command}" >/dev/null; done

CONTAINER_IMAGE="$(docker inspect --format '{{.Config.Image}}' travel-control 2>/dev/null || true)"
ARCHIVE_IMAGE="${TRAVELCONTROL_IMAGE:-${CONTAINER_IMAGE}}"
[[ -n "${ARCHIVE_IMAGE}" ]] || { echo "Cannot determine the Travel Control image for the backup" >&2; exit 1; }

mkdir -p "${DESTINATION}"
sqlite3 "${DB_PATH}" ".timeout 10000" ".backup '${DESTINATION}/travel-control.db'"
[[ "$(sqlite3 "${DESTINATION}/travel-control.db" 'PRAGMA integrity_check;')" == "ok" ]]

docker run --rm --network none --read-only --user 0:0 --entrypoint sh \
  -v "${APP_ROOT}/data:/persistent" \
  "${ARCHIVE_IMAGE}" -c '
    chown 10001:1001 /persistent
    chmod 0770 /persistent
    for name in travel-control.db travel-control.db-shm travel-control.db-wal; do
      path="/persistent/${name}"
      if [ -e "${path}" ]; then chown 10001:1001 "${path}"; chmod 0660 "${path}"; fi
    done
  '

docker run --rm --network none --read-only --user 10001:10001 --entrypoint tar \
  -v "${APP_ROOT}/keys:/var/lib/travel-control/keys:ro" \
  -v "${APP_ROOT}/attachments:/var/lib/travel-control/attachments:ro" \
  -v "${APP_ROOT}/private:/var/lib/travel-control/private:ro" \
  "${ARCHIVE_IMAGE}" -C /var/lib/travel-control -czf - keys attachments private \
  > "${DESTINATION}/persistent-files.tar.gz"
(
  cd "${DESTINATION}"
  sha256sum travel-control.db persistent-files.tar.gz > SHA256SUMS
)

BACKUP_ROOT_REAL="$(realpath "${BACKUP_ROOT}")"
while IFS= read -r -d '' candidate; do
  candidate_real="$(realpath "${candidate}")"
  case "${candidate_real}" in
    "${BACKUP_ROOT_REAL}"/*) rm -rf -- "${candidate_real}" ;;
    *) echo "Refusing to remove path outside ${BACKUP_ROOT_REAL}" >&2; exit 1 ;;
  esac
done < <(find "${BACKUP_ROOT_REAL}" -mindepth 1 -maxdepth 1 -type d -name '20????????T??????Z' -mtime "+${RETENTION_DAYS}" -print0)

echo "Travel Control backup completed: ${DESTINATION}"
