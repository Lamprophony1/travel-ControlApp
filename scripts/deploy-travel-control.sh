#!/usr/bin/env bash
set -Eeuo pipefail

APP_ROOT="${TRAVELCONTROL_ROOT:-/opt/travel-control}"
COMPOSE_FILE="${APP_ROOT}/deploy/docker-compose.yml"
CONTAINER_NAME="travel-control"

for variable in TRAVELCONTROL_IMAGE TRAVELCONTROL_HOST_PORT APP_HOSTNAME; do
  [[ -n "${!variable:-}" ]] || { echo "Missing ${variable}" >&2; exit 1; }
done
[[ "$(realpath -m "${APP_ROOT}")" == "/opt/travel-control" ]] || { echo "Invalid app root" >&2; exit 1; }
[[ -f "${COMPOSE_FILE}" ]] || { echo "Missing ${COMPOSE_FILE}" >&2; exit 1; }

previous_image="$(docker inspect --format '{{.Config.Image}}' "${CONTAINER_NAME}" 2>/dev/null || true)"

sanitized_logs() {
  docker logs "${CONTAINER_NAME}" --tail 150 2>&1 \
    | sed -E 's/((password|passport|token|secret|pin|authorization)[^= :]*[= :]+)[^ ,;]+/\1[REDACTED]/Ig' \
    || true
}

wait_for_health() {
  local attempt container_health
  for attempt in {1..30}; do
    container_health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}missing{{end}}' "${CONTAINER_NAME}" 2>/dev/null || true)"
    if [[ "${container_health}" == "healthy" ]] \
      && curl --fail --silent --show-error "http://127.0.0.1:${TRAVELCONTROL_HOST_PORT}/health/ready" >/dev/null; then
      return 0
    fi
    sleep 2
  done
  return 1
}

rollback() {
  [[ -n "${previous_image}" ]] || {
    echo "No previous Travel Control image exists; automatic rollback is unavailable for the first deployment." >&2
    return 1
  }
  echo "Rolling back only Travel Control to ${previous_image}." >&2
  TRAVELCONTROL_IMAGE="${previous_image}" docker compose \
    --project-name travel-control -f "${COMPOSE_FILE}" \
    up -d --no-build travel-control
  wait_for_health
}

if ! docker compose --project-name travel-control -f "${COMPOSE_FILE}" \
  up -d --no-build travel-control; then
  sanitized_logs
  rollback || true
  exit 1
fi

if ! wait_for_health; then
  echo "Travel Control did not become healthy." >&2
  sanitized_logs
  rollback || true
  exit 1
fi

if ! curl --fail --silent --show-error --retry 5 --retry-delay 2 \
  "https://${APP_HOSTNAME}/health/ready" >/dev/null; then
  echo "Public HTTPS health check failed for ${APP_HOSTNAME}." >&2
  sanitized_logs
  rollback || true
  exit 1
fi

if ! "${APP_ROOT}/scripts/smoke-public-read.sh" "https://${APP_HOSTNAME}"; then
  echo "Public read-only or privacy smoke test failed for ${APP_HOSTNAME}." >&2
  sanitized_logs
  rollback || true
  exit 1
fi

container_health="$(docker inspect --format '{{.State.Health.Status}}' "${CONTAINER_NAME}")"
[[ "${container_health}" == "healthy" ]] || { echo "Container is not healthy" >&2; exit 1; }
echo "Travel Control ${TRAVELCONTROL_IMAGE} is healthy locally and through https://${APP_HOSTNAME}."
