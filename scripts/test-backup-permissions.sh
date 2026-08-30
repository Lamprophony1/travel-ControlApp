#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

TEST_ROOT="$(mktemp -d)"
trap 'rm -rf -- "${TEST_ROOT}"' EXIT
DESTINATION="${TEST_ROOT}/20260101T000000Z"
install -d -m 700 "${DESTINATION}"
sqlite3 "${DESTINATION}/travel-control.db" 'CREATE TABLE Fixture (Id INTEGER PRIMARY KEY); INSERT INTO Fixture DEFAULT VALUES;'
tar -czf "${DESTINATION}/persistent-files.tar.gz" --files-from /dev/null
(
  cd "${DESTINATION}"
  sha256sum travel-control.db persistent-files.tar.gz > SHA256SUMS
)
chmod 600 "${DESTINATION}/travel-control.db" "${DESTINATION}/persistent-files.tar.gz" "${DESTINATION}/SHA256SUMS"
chmod 700 "${DESTINATION}"

"$(dirname "$0")/verify-backup-artifact.sh" "${DESTINATION}"
[[ "$(stat -c '%A' "${DESTINATION}")" == "drwx------" ]]
for backup_file in travel-control.db persistent-files.tar.gz SHA256SUMS; do
  [[ "$(stat -c '%A' "${DESTINATION}/${backup_file}")" == "-rw-------" ]]
done
