#!/usr/bin/env bash
set -Eeuo pipefail

fail() {
  echo "Travel Control preflight failed: $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command '$1' is not installed"
}

require_variable() {
  [[ -n "${!1:-}" ]] || fail "required variable '$1' is empty"
}

container_host_port() {
  docker inspect --format '{{range $port, $bindings := .HostConfig.PortBindings}}{{if eq $port "8080/tcp"}}{{range $bindings}}{{.HostPort}}{{end}}{{end}}{{end}}' "$1" 2>/dev/null || true
}

container_app_hostname() {
  docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$1" 2>/dev/null \
    | sed -n 's/^APP_HOSTNAME=//p' | tail -n 1 | tr -d '\r' || true
}

env_value() {
  local key="$1"
  sed -n "s/^${key}=//p" "${TRAVELCONTROL_ENV_FILE}" | tail -n 1 | tr -d '\r'
}

require_variable TRAVELCONTROL_IMAGE
require_variable TRAVELCONTROL_HOST_PORT
require_variable APP_HOSTNAME

TRAVELCONTROL_ROOT="${TRAVELCONTROL_ROOT:-/opt/travel-control}"
TRAVELCONTROL_ENV_FILE="${TRAVELCONTROL_ENV_FILE:-${TRAVELCONTROL_ROOT}/travel-control.env}"
TRAVELCONTROL_COMPOSE_SOURCE="${TRAVELCONTROL_COMPOSE_SOURCE:-${GITHUB_WORKSPACE:-${PWD}}/deploy/docker-compose.yml}"
EXPECTED_BASE_DOMAIN="${EXPECTED_BASE_DOMAIN:-crg-dev.com}"

[[ "$(realpath -m "${TRAVELCONTROL_ROOT}")" == "/opt/travel-control" ]] \
  || fail "TRAVELCONTROL_ROOT must resolve to /opt/travel-control"
[[ "${RUNNER_ENVIRONMENT:-self-hosted}" == "self-hosted" ]] \
  || fail "the deployment must run on the existing self-hosted runner"
[[ -n "${RUNNER_NAME:-local-preflight}" ]] || fail "RUNNER_NAME is empty"

for command in docker curl ss sqlite3 sha256sum tar realpath stat sed grep; do
  require_command "${command}"
done
docker info >/dev/null 2>&1 || fail "Docker is not available to the runner"
docker compose version >/dev/null 2>&1 || fail "Docker Compose plugin is not available"
docker image inspect "${TRAVELCONTROL_IMAGE}" >/dev/null 2>&1 \
  || fail "the immutable GHCR image has not been pulled: ${TRAVELCONTROL_IMAGE}"
[[ "${TRAVELCONTROL_IMAGE}" =~ ^ghcr\.io/lamprophony1/travel-controlapp:[0-9a-f]{40}$ ]] \
  || fail "TRAVELCONTROL_IMAGE must be the travel-controlapp GHCR image tagged with a full commit SHA"

[[ "${TRAVELCONTROL_HOST_PORT}" =~ ^[0-9]+$ ]] \
  || fail "TRAVELCONTROL_HOST_PORT must be numeric"
(( TRAVELCONTROL_HOST_PORT >= 1024 && TRAVELCONTROL_HOST_PORT <= 65535 )) \
  || fail "TRAVELCONTROL_HOST_PORT must be between 1024 and 65535"
[[ "${TRAVELCONTROL_HOST_PORT}" != "5020" ]] \
  || fail "port 5020 belongs to GymQuest"

for existing_container in gymquest printcost; do
  existing_port="$(container_host_port "${existing_container}")"
  [[ -z "${existing_port}" || "${existing_port}" != "${TRAVELCONTROL_HOST_PORT}" ]] \
    || fail "port ${TRAVELCONTROL_HOST_PORT} is assigned to ${existing_container}"
done

for existing_id in $(docker ps -aq); do
  existing_name="$(docker inspect --format '{{.Name}}' "${existing_id}" | sed 's#^/##')"
  [[ "${existing_name}" == "travel-control" ]] && continue
  while IFS= read -r mount_source; do
    case "${mount_source}" in
      "${TRAVELCONTROL_ROOT}"|"${TRAVELCONTROL_ROOT}"/*)
        fail "container ${existing_name} already mounts Travel Control's persistent directory" ;;
    esac
  done < <(docker inspect --format '{{range .Mounts}}{{println .Source}}{{end}}' "${existing_id}")
done

if ss -H -ltn | awk '{print $4}' | grep -Eq "(^|:)${TRAVELCONTROL_HOST_PORT}$"; then
  own_port="$(container_host_port travel-control)"
  own_running="$(docker inspect --format '{{.State.Running}}' travel-control 2>/dev/null || true)"
  [[ "${own_running}" == "true" && "${own_port}" == "${TRAVELCONTROL_HOST_PORT}" ]] \
    || fail "port ${TRAVELCONTROL_HOST_PORT} is already occupied by another process"
fi

[[ "${APP_HOSTNAME}" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?\.${EXPECTED_BASE_DOMAIN//./\.}$ ]] \
  || fail "APP_HOSTNAME must be a subdomain of ${EXPECTED_BASE_DOMAIN}"
[[ ${#APP_HOSTNAME} -le 253 && "${APP_HOSTNAME}" != *".."* && "${APP_HOSTNAME}" != *".-"* && "${APP_HOSTNAME}" != *"-."* ]] \
  || fail "APP_HOSTNAME is not a valid DNS hostname"
[[ "${APP_HOSTNAME,,}" != "rm.crg-dev.com" ]] \
  || fail "rm.crg-dev.com belongs to GymQuest"

for existing_container in gymquest printcost; do
  existing_hostname="$(container_app_hostname "${existing_container}")"
  [[ -z "${existing_hostname}" || "${existing_hostname,,}" != "${APP_HOSTNAME,,}" ]] \
    || fail "APP_HOSTNAME is already assigned to ${existing_container}"
done
if [[ -r /opt/printcost/printcost.env ]]; then
  printcost_hostname="$(sed -n 's/^APP_HOSTNAME=//p' /opt/printcost/printcost.env | tail -n 1 | tr -d '\r')"
  [[ -z "${printcost_hostname}" || "${printcost_hostname,,}" != "${APP_HOSTNAME,,}" ]] \
    || fail "APP_HOSTNAME is already configured for PrintCost"
fi

umask 027
mkdir -p \
  "${TRAVELCONTROL_ROOT}/deploy" \
  "${TRAVELCONTROL_ROOT}/data" \
  "${TRAVELCONTROL_ROOT}/keys" \
  "${TRAVELCONTROL_ROOT}/attachments" \
  "${TRAVELCONTROL_ROOT}/private" \
  "${TRAVELCONTROL_ROOT}/backups" \
  "${TRAVELCONTROL_ROOT}/scripts" \
  || fail "cannot create the persistent directory tree"

for directory in deploy data keys attachments private backups scripts; do
  [[ -d "${TRAVELCONTROL_ROOT}/${directory}" && -w "${TRAVELCONTROL_ROOT}/${directory}" ]] \
    || fail "${TRAVELCONTROL_ROOT}/${directory} is not writable by the runner"
done

[[ -f "${TRAVELCONTROL_ENV_FILE}" && -r "${TRAVELCONTROL_ENV_FILE}" ]] \
  || fail "create ${TRAVELCONTROL_ENV_FILE} from deploy/travel-control.env.example"
env_mode="$(stat -c '%a' "${TRAVELCONTROL_ENV_FILE}")"
(( (8#${env_mode} & 077) == 0 )) \
  || fail "${TRAVELCONTROL_ENV_FILE} must not be readable or writable by group/others (use chmod 600)"
[[ "$(env_value APP_HOSTNAME)" == "${APP_HOSTNAME}" ]] \
  || fail "APP_HOSTNAME differs between GitHub Environment and ${TRAVELCONTROL_ENV_FILE}"
[[ "$(env_value TRAVELCONTROL_HOST_PORT)" == "${TRAVELCONTROL_HOST_PORT}" ]] \
  || fail "TRAVELCONTROL_HOST_PORT differs between GitHub Environment and ${TRAVELCONTROL_ENV_FILE}"

[[ -f "${TRAVELCONTROL_COMPOSE_SOURCE}" ]] || fail "production Compose source is missing"
if grep -Eq '/opt/(gymquest|printcost)|container_name:[[:space:]]*(gymquest|printcost)' "${TRAVELCONTROL_COMPOSE_SOURCE}"; then
  fail "production Compose references another application"
fi
grep -Eq 'container_name:[[:space:]]*travel-control' "${TRAVELCONTROL_COMPOSE_SOURCE}" \
  || fail "production Compose must use the unique travel-control container name"
docker compose -f "${TRAVELCONTROL_COMPOSE_SOURCE}" config --quiet \
  || fail "production Compose is invalid"

database_path="${TRAVELCONTROL_ROOT}/data/travel-control.db"
database_is_empty=true
if [[ -s "${database_path}" ]]; then
  [[ "$(sqlite3 "${database_path}" 'PRAGMA integrity_check;')" == "ok" ]] \
    || fail "the existing SQLite database failed integrity_check"
  passenger_table="$(sqlite3 "${database_path}" "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Passengers';")"
  if [[ "${passenger_table}" == "1" ]]; then
    passenger_count="$(sqlite3 "${database_path}" 'SELECT COUNT(*) FROM Passengers;')"
    (( passenger_count > 0 )) && database_is_empty=false
  fi
fi

if [[ "${database_is_empty}" == "true" ]]; then
  [[ "$(env_value BootstrapImport__Enabled)" == "true" ]] \
    || fail "BootstrapImport__Enabled=true is required while the database is empty"
  [[ "$(env_value BootstrapImport__Required)" == "true" ]] \
    || fail "BootstrapImport__Required=true is required while the database is empty"
  workbook="${TRAVELCONTROL_ROOT}/private/Control_viaje.xlsx"
  [[ -s "${workbook}" ]] || fail "the empty database requires ${workbook}"
fi

for writable_mount in data keys attachments; do
  docker run --rm --entrypoint sh \
    -v "${TRAVELCONTROL_ROOT}/${writable_mount}:/probe" \
    "${TRAVELCONTROL_IMAGE}" -c 'test -w /probe' >/dev/null \
    || fail "container UID 10001 cannot write ${TRAVELCONTROL_ROOT}/${writable_mount}"
done
if [[ "${database_is_empty}" == "true" ]]; then
  docker run --rm --entrypoint sh \
    -v "${TRAVELCONTROL_ROOT}/private:/probe:ro" \
    "${TRAVELCONTROL_IMAGE}" -c 'test -r /probe/Control_viaje.xlsx' >/dev/null \
    || fail "the container cannot read the private workbook"
fi

echo "Travel Control preflight passed on runner ${RUNNER_NAME:-local-preflight}."
echo "Target: ${APP_HOSTNAME} -> 127.0.0.1:${TRAVELCONTROL_HOST_PORT}; root=${TRAVELCONTROL_ROOT}."
