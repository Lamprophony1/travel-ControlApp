# Despliegue en la infraestructura existente

Travel Control sigue el patrón local comprobado de GymQuest y PrintCost: imagen única, GHCR, runner self-hosted existente, directorio persistente bajo `/opt`, Docker Compose y Cloudflare Tunnel hacia un puerto exclusivamente loopback. La evidencia comparativa está en [INFRASTRUCTURE_COMPARISON.md](INFRASTRUCTURE_COMPARISON.md).

## Recursos exclusivos

- Directorio: `/opt/travel-control`.
- Proyecto Compose y contenedor: `travel-control`.
- Imagen: `ghcr.io/lamprophony1/travel-controlapp:<sha>`.
- Puerto: variable obligatoria `TRAVELCONTROL_HOST_PORT`.
- Hostname: variable obligatoria `APP_HOSTNAME`, dentro de `crg-dev.com` y diferente de las otras aplicaciones.
- Runner: el runner existente que ya tiene el label `printcost`; no se instala otro runner ni se agrega un label nuevo.
- GitHub Environment: `production`.

No se ejecuta `docker compose down`, no se usa `--remove-orphans` y ningún script entra a `/opt/gymquest` o `/opt/printcost` salvo el preflight de solo lectura que detecta colisiones.

## Variables de GitHub

Crear en `Repository → Settings → Environments → production`:

- `APP_HOSTNAME`: subdominio definitivo, por ejemplo el que el usuario haya creado dentro de `crg-dev.com`; no usar `rm.crg-dev.com`.
- `TRAVELCONTROL_HOST_PORT`: puerto libre entre 1024 y 65535, diferente de `5020` y del puerto real de PrintCost.

No se requiere un secret personalizado. Publicación y pull de GHCR usan el `GITHUB_TOKEN` automático.

## Preparación única del servidor

Identificar primero al usuario real del runner que atiende el label `printcost`. El patrón de PrintCost usa UID/GID `10001`; Travel Control usa el mismo UID/GID dentro del contenedor.

```bash
id github-runner
sudo install -d -o github-runner -g github-runner -m 0750 \
  /opt/travel-control \
  /opt/travel-control/deploy \
  /opt/travel-control/data \
  /opt/travel-control/keys \
  /opt/travel-control/attachments \
  /opt/travel-control/private \
  /opt/travel-control/backups \
  /opt/travel-control/scripts
```

Si el usuario operativo tiene otro nombre, sustituirlo sin cambiar el UID/GID esperado. Confirmar que el runner y el contenedor pueden escribir los bind mounts; el preflight también lo prueba ejecutando la imagen publicada.

Crear el archivo productivo sin versionarlo:

```bash
sudo install -o github-runner -g github-runner -m 0600 \
  deploy/travel-control.env.example \
  /opt/travel-control/travel-control.env
sudo -u github-runner nano /opt/travel-control/travel-control.env
```

Debe contener los valores reales y mantener, para la primera inicialización:

```env
APP_HOSTNAME=HOSTNAME_REAL
TRAVELCONTROL_HOST_PORT=PUERTO_REAL
BootstrapImport__Enabled=true
BootstrapImport__Required=true
Storage__MaxBytes=10485760
```

Copiar el workbook privado, sin incorporarlo a Git ni a la imagen:

```bash
sudo install -o github-runner -g github-runner -m 0400 \
  Control_viaje.xlsx \
  /opt/travel-control/private/Control_viaje.xlsx
```

El directorio se monta `read-only`. El bootstrap solo se ejecuta cuando la base no tiene pasajeros, valida 46 pasajeros y 25 habitaciones antes de confirmar y no vuelve a duplicarlos en despliegues posteriores.

El servidor debe tener `docker`, el plugin `docker compose`, `curl`, `ss`, `sqlite3`, `sha256sum`, `tar`, `realpath`, `sed` y `grep`. El runner debe poder usar Docker sin `sudo`.

## Puerto y preflight

Antes de definir `TRAVELCONTROL_HOST_PORT`, inspeccionar:

```bash
ss -ltn
docker ps -a --format 'table {{.Names}}\t{{.Ports}}'
docker inspect gymquest --format '{{json .HostConfig.PortBindings}}'
docker inspect printcost --format '{{json .HostConfig.PortBindings}}'
```

El script [preflight-travel-control.sh](../scripts/preflight-travel-control.sh) falla si:

- no corre en el runner self-hosted esperado;
- faltan Docker, Compose o utilidades operativas;
- la imagen SHA no existe localmente;
- el puerto no es numérico, está fuera de rango, es `5020` o colisiona con un socket/contenedor;
- el hostname no pertenece a `crg-dev.com` o coincide con GymQuest/PrintCost;
- falta el `.env`, tiene permisos inseguros o difiere de las variables GitHub;
- faltan directorios o permisos para UID `10001`;
- SQLite no supera `integrity_check`;
- la base vacía no tiene workbook o bootstrap obligatorio;
- Compose referencia nombres, rutas o recursos de otra aplicación.

## Flujo CI/CD

Un push a `main` o una ejecución manual realiza:

1. Restore, build y tests .NET.
2. Instalación, lint, tests y build web.
3. Build del contenedor y Playwright real en desktop/móvil.
4. Publicación en GHCR con tags `<sha>` y `latest`.
5. Ejecución en `[self-hosted, printcost]` y Environment `production`.
6. Pull explícito de la imagen SHA y preflight.
7. Instalación del Compose y scripts en `/opt/travel-control`.
8. Backup SQLite online junto con adjuntos, keys y workbook privado cuando ya existe una base.
9. `docker compose up` solo para el servicio `travel-control`.
10. Verificación del health Docker, `127.0.0.1:${TRAVELCONTROL_HOST_PORT}/health/ready` y `https://${APP_HOSTNAME}/health/ready`.
11. Rollback a la imagen anterior si falla el despliegue o cualquiera de los health checks.

Los logs de error se limitan a 150 líneas y pasan por redacción de campos sensibles. La aplicación tampoco registra payloads, nombres ni documentos del workbook.

## Cloudflare

La zona real comprobada en GymQuest es `crg-dev.com`. El usuario debe crear un hostname propio para Travel Control en la misma cuenta/zona y usando el mismo mecanismo de tunnels del servidor. No debe modificar `rm.crg-dev.com` ni la ruta de PrintCost.

El destino del Public Hostname es:

```text
http://127.0.0.1:${TRAVELCONTROL_HOST_PORT}
```

La plantilla [cloudflared-travel-control.example.yml](../deploy/cloudflared-travel-control.example.yml) contiene placeholders; el dominio definitivo no está hardcodeado. Si el servidor usa un tunnel administrado desde el dashboard, agregar allí el Public Hostname en lugar de instalar otro reverse proxy. Si la convención operativa mantiene un tunnel nombrado por aplicación, crear solo la nueva ruta/tunnel dentro de la misma cuenta y servicio `cloudflared` ya utilizado.

ASP.NET procesa `X-Forwarded-For` y `X-Forwarded-Proto`; producción fuerza cookies Secure. El puerto nunca se publica en `0.0.0.0`.

## Backups y restore

El workflow crea un backup antes de cada actualización cuando existe la base. También puede ejecutarse manualmente:

```bash
sudo -u github-runner /opt/travel-control/scripts/backup-travel-control.sh
```

Cada backup contiene:

- snapshot online de `travel-control.db` mediante `.backup` de SQLite;
- `PRAGMA integrity_check`;
- checksums SHA-256;
- Data Protection Keys;
- adjuntos;
- workbook privado;
- retención local predeterminada de 30 días.

Copiar los backups a almacenamiento externo cifrado. Para restaurar un backup validado:

```bash
cd /opt/travel-control/deploy
export TRAVELCONTROL_IMAGE=ghcr.io/lamprophony1/travel-controlapp:SHA
export TRAVELCONTROL_HOST_PORT=PUERTO_REAL
export APP_HOSTNAME=HOSTNAME_REAL
sudo -u github-runner -E /opt/travel-control/scripts/restore-travel-control.sh \
  /opt/travel-control/backups/YYYYMMDDTHHMMSSZ
```

El restore comprueba rutas, checksums e integridad, detiene y reinicia exclusivamente `travel-control`. No toca contenedores, volúmenes, archivos o directorios de GymQuest y PrintCost.

## Rollback manual

```bash
cd /opt/travel-control/deploy
export TRAVELCONTROL_IMAGE=ghcr.io/lamprophony1/travel-controlapp:SHA_ANTERIOR
export TRAVELCONTROL_HOST_PORT=PUERTO_REAL
export APP_HOSTNAME=HOSTNAME_REAL
docker compose --project-name travel-control up -d --no-build travel-control
curl -fsS "http://127.0.0.1:${TRAVELCONTROL_HOST_PORT}/health/ready"
curl -fsS "https://${APP_HOSTNAME}/health/ready"
```

La base, adjuntos, workbook y keys permanecen en `/opt/travel-control`; cambiar la imagen no los reemplaza.
