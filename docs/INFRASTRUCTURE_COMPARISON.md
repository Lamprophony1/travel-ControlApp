# Comparación de infraestructura local

Esta decisión se basa en las copias locales inspeccionadas el 28 de agosto de 2026. Los archivos locales son la fuente de verdad; ningún valor productivo faltante se inventa.

| Aspecto | GymChall / GymQuest | CRAF / PrintCost | Travel Control |
|---|---|---|---|
| Copia local | `G:\Projects VS\Personal\GymChall` | `G:\Projects VS\Personal\craf3D.Calculator` | `G:\Projects VS\Personal\travel-control` |
| Remoto | `Lamprophony1/GymQuest` | `Lamprophony1/craf3D-Calculator` | `Lamprophony1/travel-ControlApp` |
| Workflow | `.github/workflows/ci-cd.yml` | `.github/workflows/ci-cd.yml` | `.github/workflows/ci-cd.yml` |
| Runner labels | `[self-hosted, gymquest]` | `[self-hosted, printcost]` | `[self-hosted, printcost]`, reutilizando el runner existente |
| GitHub Environment | `production` | `production` | `production` |
| Directorio persistente | `/opt/gymquest` | `/opt/printcost` | `/opt/travel-control` |
| Contenedor | `gymquest` | `printcost` | `travel-control` |
| Puerto loopback | `5020` | `PRINTCOST_HOST_PORT` | `TRAVELCONTROL_HOST_PORT` obligatorio |
| Hostname | `rm.crg-dev.com` | `APP_HOSTNAME`, valor no versionado | `APP_HOSTNAME`, valor no versionado |
| Zona comprobada | `crg-dev.com` | misma zona prevista por su despliegue | `crg-dev.com` mediante hostname propio |
| Cloudflare | tunnel nombrado hacia `127.0.0.1:5020` | plantilla de tunnel nombrado hacia su puerto | mismo mecanismo y cuenta/zona; ruta propia hacia loopback |
| Imagen | `ghcr.io/lamprophony1/gymquest:<sha>` | `ghcr.io/lamprophony1/craf3d-calculator:<sha>` | `ghcr.io/lamprophony1/travel-controlapp:<sha>` |
| Compose | `/opt/gymquest/deploy` | `/opt/printcost/deploy` | `/opt/travel-control/deploy` |
| Persistencia | SQLite y Data Protection Keys | SQLite, Keys y backups | SQLite, Keys, adjuntos, workbook privado y backups |
| Health | `GET /health` | `GET /health` | `GET /health/ready` y health Docker |
| Publicación | SHA + `latest` en GHCR | SHA + `latest` en GHCR | SHA + `latest` en GHCR |
| Actualización | `docker compose up -d --pull always` | igual, con preflight | preflight, backup, imagen SHA, health local/HTTPS y rollback |

## Evidencia y diferencias

GymQuest documenta un despliegue operativo en `rm.crg-dev.com`, puerto `5020`, runner `gymquest`, directorio `/opt/gymquest` y tunnel `gymquest-dc-pti` en:

- `G:\Projects VS\Personal\GymChall\.github\workflows\ci-cd.yml`;
- `G:\Projects VS\Personal\GymChall\deploy\docker-compose.yml`;
- `G:\Projects VS\Personal\GymChall\deploy\cloudflared-gymquest.example.yml`;
- `G:\Projects VS\Personal\GymChall\docs\deployment\github-cloudflare-vm.md`.

PrintCost adopta el mismo servidor y patrón, con aislamiento bajo `/opt/printcost`, runner `printcost`, contenedor `printcost`, variables `APP_HOSTNAME` y `PRINTCOST_HOST_PORT`, preflight, backups y restore en:

- `G:\Projects VS\Personal\craf3D.Calculator\.github\workflows\ci-cd.yml`;
- `G:\Projects VS\Personal\craf3D.Calculator\deploy\docker-compose.yml`;
- `G:\Projects VS\Personal\craf3D.Calculator\scripts\backup-printcost.sh`;
- `G:\Projects VS\Personal\craf3D.Calculator\scripts\restore-printcost.sh`;
- `G:\Projects VS\Personal\craf3D.Calculator\docs\deployment.md`.

La copia local de PrintCost no versiona el valor de su puerto ni hostname y su README todavía los enumera como pendientes. Travel Control, por lo tanto, no copia valores supuestos: su preflight consulta sockets y las configuraciones de los contenedores `gymquest` y `printcost`, exige un puerto libre y compara hostnames cuando están disponibles en el servidor.
