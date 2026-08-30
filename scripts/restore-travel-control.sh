#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 /opt/travel-control/backups/YYYYMMDDTHHMMSSZ" >&2
  exit 2
fi

APP_ROOT="${TRAVELCONTROL_ROOT:-/opt/travel-control}"
BACKUP_ROOT_REAL="$(realpath "${TRAVELCONTROL_BACKUP_ROOT:-${APP_ROOT}/backups}")"
SOURCE_REAL="$(realpath "$1")"
[[ "$(realpath -m "${APP_ROOT}")" == "/opt/travel-control" ]] || { echo "Invalid app root" >&2; exit 1; }
case "${SOURCE_REAL}" in
  "${BACKUP_ROOT_REAL}"/*) ;;
  *) echo "Backup must be under ${BACKUP_ROOT_REAL}" >&2; exit 1 ;;
esac

"$(dirname "$0")/verify-backup-artifact.sh" "${SOURCE_REAL}"

cd "${APP_ROOT}/deploy"
docker compose --project-name travel-control stop travel-control
sqlite3 "${APP_ROOT}/data/travel-control.db" ".restore '${SOURCE_REAL}/travel-control.db'"
for directory in keys attachments private; do
  directory_real="$(realpath "${APP_ROOT}/${directory}")"
  case "${directory_real}" in
    "${APP_ROOT}"/*) find "${directory_real}" -mindepth 1 -delete ;;
    *) echo "Refusing to clear ${directory_real}" >&2; exit 1 ;;
  esac
done
tar -C "${APP_ROOT}" -xzf "${SOURCE_REAL}/persistent-files.tar.gz"
docker compose --project-name travel-control up -d --no-build travel-control
echo "Restore completed. Only Travel Control was restarted; verify /health/ready and login."
