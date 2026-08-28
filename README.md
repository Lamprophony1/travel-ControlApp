# Control de Viaje

Aplicación privada, mobile-first y PWA para administrar pasajeros, pasaportes, documentación, habitaciones, vuelos, tickets, equipaje de 23 kg, seguimiento y un único estado global de transfer.

## Stack y arquitectura

- `src/TravelControl.Domain`: entidades y enums sin dependencias de infraestructura.
- `src/TravelControl.Application`: contratos, normalización y reglas calculadas.
- `src/TravelControl.Infrastructure`: EF Core/SQLite, Identity, XLSX, archivos y consultas.
- `src/TravelControl.Api`: host same-origin, seguridad y Minimal APIs.
- `web`: React 19, TypeScript estricto, MUI, TanStack Query y PWA.
- `tests`: xUnit por capa, Vitest y Playwright desktop/mobile.

El frontend se compila dentro de `wwwroot`; producción es una sola imagen y un solo contenedor. SQLite, adjuntos y claves de protección viven bajo `/var/lib/travel-control`.

## Inicio rápido

Requisitos: Docker Engine y Compose v2.

```sh
docker compose up --build -d
```

Abrir `http://127.0.0.1:8080`. La primera visita permite crear el primer administrador; no existen usuarios ni contraseñas predeterminadas. En producción, publicar detrás de un proxy HTTPS y usar `COOKIE_SECURE=true`.

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

El workbook maestro se coloca fuera de Git en `data/private/Control_viaje.xlsx`. Para inicializar una base vacía desde Docker:

```sh
BOOTSTRAP_IMPORT_ENABLED=true BOOTSTRAP_IMPORT_REQUIRED=true docker compose up --build
```

Antes de confirmar, el importador ejecuta el mismo dry-run transaccional que la UI. El set esperado actual es 46 pasajeros y 25 habitaciones: 44/24 de Top Travel y 2/1 de Bespoke. Las columnas heredadas de responsable o transfer individual se ignoran e informan; no se crean filas vacías de vuelo, equipaje o seguimiento. El Dashboard del XLSX nunca es fuente autoritativa.

Importación administrativa por CLI:

```sh
dotnet run --project src/TravelControl.Api -- --import data/private/Control_viaje.xlsx --dry-run
dotnet run --project src/TravelControl.Api -- --import data/private/Control_viaje.xlsx
```

## Verificación

```sh
dotnet test TravelControl.slnx -c Release
cd web
npm run lint
npm test
npm run build
npm run e2e
```

Playwright usa `E2E_BASE_URL` y prueba Chromium en desktop y Pixel 7. La prueba privada completa solo corre si se entrega `E2E_WORKBOOK_PATH` desde un almacén seguro.

## Operación

- Salud: `/health/live` y `/health/ready`.
- Backup consistente: detener escrituras y respaldar el volumen `travel-control-data` completo.
- Exportaciones: XLSX de control, XLSX de pendientes, CSV enmascarado y JSON administrativo.
- Migraciones: `dotnet ef migrations add Nombre --project src/TravelControl.Infrastructure --startup-project src/TravelControl.Api --output-dir Persistence/Migrations`.

Más detalle: [arquitectura](docs/ARCHITECTURE.md), [modelo](docs/DATA_MODEL.md), [reglas](docs/BUSINESS_RULES.md), [importación](docs/IMPORT_EXPORT.md), [seguridad](docs/SECURITY.md), [despliegue](docs/DEPLOYMENT.md) y [decisiones](docs/DECISIONS.md).
