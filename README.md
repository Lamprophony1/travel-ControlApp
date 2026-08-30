# Control de Viaje

Aplicación mobile-first y PWA con consulta pública sanitizada y gestión privada por roles para pasajeros, documentación, habitaciones, vuelos, tickets, equipaje de 23 kg, seguimiento y un único estado global de transfer.

La consulta sin login vive en `/`, `/pasajeros` y `/pasajeros/:id`. La gestión autenticada vive bajo `/gestion`; nunca se vuelven anónimos los endpoints privados existentes. Véase [acceso público](docs/PUBLIC_READ_ACCESS.md).

## Stack y arquitectura

- `src/TravelControl.Domain`: entidades y enums sin dependencias de infraestructura.
- `src/TravelControl.Application`: contratos, normalización y reglas calculadas.
- `src/TravelControl.Infrastructure`: EF Core/SQLite, Identity, XLSX, archivos y consultas.
- `src/TravelControl.Api`: host same-origin, seguridad y Minimal APIs.
- `web`: React 19, TypeScript estricto, MUI, TanStack Query y PWA.
- `tests`: xUnit por capa, Vitest y Playwright desktop/mobile.

El frontend se compila dentro de `wwwroot`; producción es una sola imagen y un solo contenedor. SQLite, adjuntos y claves de protección viven bajo `/var/lib/travel-control`.

## Inicio rápido

Requisitos: .NET SDK 10 y Node.js 24. El flujo local recomendado ejecuta API y Vite por separado; el contenedor único se valida en CI.

La primera visita permite crear el primer administrador; no existen usuarios ni contraseñas predeterminadas.

## Desarrollo

Requisitos: .NET SDK 10 y Node.js 24.

```sh
dotnet tool restore
dotnet restore TravelControl.slnx
dotnet run --project src/TravelControl.Api
```

En otra terminal:

```sh
cd web
npm ci
npm run dev
```

El perfil Development usa SQLite en `.dev/` y cookies compatibles con HTTP local. Ningún dato privado debe entrar al repositorio.

## Bootstrap privado e importación

El workbook maestro local se coloca fuera de Git en `data/private/Control_viaje.xlsx`. En producción reside en `/opt/travel-control/private/Control_viaje.xlsx`, montado como solo lectura, y el preflight exige `BootstrapImport__Enabled=true` y `BootstrapImport__Required=true` mientras la base esté vacía.

Antes de confirmar, el importador ejecuta el mismo dry-run transaccional que la UI. El set esperado actual es 46 pasajeros y 25 habitaciones: 44/24 de Top Travel y 2/1 de Bespoke. Las columnas heredadas de responsable o transfer individual se ignoran e informan; no se crean filas vacías de vuelo, equipaje o seguimiento. El Dashboard del XLSX nunca es fuente autoritativa.

Importación administrativa por CLI:

```sh
dotnet run --project src/TravelControl.Api -- --import data/private/Control_viaje.xlsx --dry-run
dotnet run --project src/TravelControl.Api -- --import data/private/Control_viaje.xlsx
```

La UI administrativa también admite un manifiesto privado CSV/XLSX para completar identidad y asociar pasajeros existentes a reservas. Requiere vista previa, mismo SHA-256 y confirmación explícita; agrupa por PNR, no crea ni elimina pasajeros o habitaciones y nunca usa el PNR como número de ticket electrónico. Un ticket es efectivo con PNR, aerolínea y estado individual confirmado; el número electrónico y el itinerario detallado son opcionales.

## Verificación

```sh
dotnet test TravelControl.slnx -c Release
cd web
npm run lint
npm test
npm run build
npm run e2e
```

Playwright usa `E2E_BASE_URL` y prueba Chromium en 360×800, 390×844, 430×932, 768×1024 y 1440×900. Los fixtures de CI son exclusivamente ficticios.

## Operación

- Salud: `/health/live` y `/health/ready`.
- Producción: `/opt/travel-control`, runner existente `[self-hosted, printcost]`, puerto `TRAVELCONTROL_HOST_PORT` y hostname `APP_HOSTNAME`.
- Backup consistente: `scripts/backup-travel-control.sh` crea un snapshot SQLite online y conserva keys, adjuntos y workbook privado.
- Exportaciones: XLSX de control, XLSX de pendientes, CSV enmascarado y JSON administrativo.
- Migraciones: `dotnet ef migrations add Nombre --project src/TravelControl.Infrastructure --startup-project src/TravelControl.Api --output-dir Persistence/Migrations`.

Más detalle: [arquitectura](docs/ARCHITECTURE.md), [acceso público](docs/PUBLIC_READ_ACCESS.md), [auditoría responsive](docs/RESPONSIVE_AUDIT.md), [modelo](docs/DATA_MODEL.md), [reglas](docs/BUSINESS_RULES.md), [importación](docs/IMPORT_EXPORT.md), [seguridad](docs/SECURITY.md), [despliegue](docs/DEPLOYMENT.md) y [decisiones](docs/DECISIONS.md).

## Endurecimiento de coherencia

La clasificación documental autoritativa vive en `AttachmentLink.EvidenceType`: un único archivo deduplicado por hash puede actuar como ticket aéreo y comprobante de maleta para el mismo PNR. Los campos de destino y `DocumentType` de `Attachment` permanecen temporalmente como compatibilidad de rollback, pero el código nuevo no los usa para resolver evidencia. La ficha individual solo desvincula evidencia directa; los vínculos heredados se administran en su PNR, habitación o equipaje con una advertencia de alcance.

`TripReadinessService` alimenta dashboard público, dashboard privado y Excel. Un viaje no muestra 100 % si queda cualquier bloqueante global. El PNR también es derivado: su estado no se edita manualmente, y cada ticket individual usa `Version`, `UpdatedAt` y `UpdatedById` para rechazar sobrescrituras con 409.

“Exportación estructurada JSON” contiene datos estructurados y no sustituye el [backup completo del servidor](docs/BACKUP_RESTORE.md): no incluye archivos, claves ni configuración. El endpoint preferido es `/api/exports/structured.json`; `/api/exports/backup.json` es un alias obsoleto temporal.
