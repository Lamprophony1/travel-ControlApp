#!/usr/bin/env bash
set -Eeuo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 BACKUP_DIRECTORY" >&2
  exit 2
fi

SOURCE_REAL="$(realpath "$1")"
[[ -d "${SOURCE_REAL}" ]] || { echo "Backup directory does not exist" >&2; exit 1; }
for command_name in sqlite3 sha256sum realpath stat; do command -v "${command_name}" >/dev/null; done

[[ "$(stat -c '%a' "${SOURCE_REAL}")" == "700" ]] || { echo "Unsafe backup directory permissions" >&2; exit 1; }
for backup_file in travel-control.db persistent-files.tar.gz SHA256SUMS; do
  [[ -f "${SOURCE_REAL}/${backup_file}" ]] || { echo "Backup artifact is incomplete" >&2; exit 1; }
  [[ "$(stat -c '%a' "${SOURCE_REAL}/${backup_file}")" == "600" ]] \
    || { echo "Unsafe backup file permissions" >&2; exit 1; }
done
(
  cd "${SOURCE_REAL}"
  sha256sum --check --status SHA256SUMS
)
[[ "$(sqlite3 "${SOURCE_REAL}/travel-control.db" 'PRAGMA integrity_check;')" == "ok" ]] \
  || { echo "Backup SQLite integrity check failed" >&2; exit 1; }

echo "Backup artifact permissions, checksums and SQLite integrity are valid."
