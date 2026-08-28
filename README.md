# Control de Viaje — Boda Cielito & Ronaldo

Aplicación web privada para controlar pasajeros, pasaportes, documentación, habitaciones, vuelos, maletas de 23 kg, transfers, comprobantes y seguimientos del viaje a Riviera Maya en septiembre de 2026.

## Arquitectura

- Frontend: React 19, TypeScript estricto, Vite, Material UI, TanStack Query/Table, React Hook Form, Zod y PWA.
- Backend: ASP.NET Core 10, Entity Framework Core, PostgreSQL, Identity con cookies HttpOnly, FluentValidation, ClosedXML y OpenAPI.
- Despliegue: Docker Compose, PostgreSQL persistente, Caddy con HTTPS local, volumen privado para comprobantes y health checks.
- Pruebas: xUnit v3, Vitest/Testing Library y Playwright.

El repositorio no contiene datos reales. `data/private/` está ignorado por Git y debe mantenerse fuera de copias públicas.

## Inicio rápido con Docker

Requisitos: Docker Engine con Compose v2.

1. Copiar `.env.example` a `.env`.
2. Reemplazar `POSTGRES_PASSWORD` por un secreto largo y aleatorio.
3. Ejecutar:

```sh
docker compose up --build
```

4. Abrir `https://localhost`. Caddy genera una CA local; el navegador puede pedir confiar en ella la primera vez.
5. La primera visita redirige a `/setup`, donde se registra el primer administrador. No hay credenciales predeterminadas.

Las migraciones se aplican de forma automática al iniciar la API. Los servicios exponen health checks en `/health/live` y `/health/ready` a través del proxy.

## Variables de entorno

| Variable | Uso |
|---|---|
| `POSTGRES_DB` | Base PostgreSQL. |
| `POSTGRES_USER` | Usuario PostgreSQL. |
| `POSTGRES_PASSWORD` | Secreto obligatorio; nunca versionarlo. |
| `ALLOWED_ORIGIN` | Origen HTTPS permitido por CORS. |
| `ATTACHMENT_MAX_BYTES` | Tamaño máximo de cada comprobante. |

En desarrollo sin Docker, la API también acepta `ConnectionStrings__Database`, `Security__AllowedOrigins__0` y `Storage__Root` mediante variables o user-secrets.

## Desarrollo local

Requisitos: .NET SDK 10, Node.js 24 LTS y PostgreSQL 18.

```sh
dotnet tool restore
dotnet restore TravelControl.slnx
dotnet run --project apps/api/TravelControl.Api.csproj
```

En otra terminal:

```sh
cd apps/web
npm install
npm run dev
```

Vite publica la interfaz en `http://localhost:5173` y redirige `/api` a la API en `http://localhost:5090`. Para probar cookies `Secure` fuera de Docker, usá HTTPS o ajustá solo el perfil local de desarrollo; producción siempre exige HTTPS.

## Migraciones

```sh
dotnet ef migrations add NombreDeMigracion --project apps/api/TravelControl.Api.csproj --output-dir Data/Migrations
dotnet ef database update --project apps/api/TravelControl.Api.csproj
```

La migración inicial ya está incluida. Antes de una migración productiva, crear un backup de PostgreSQL.

## Importar el Excel maestro

Colocar el archivo únicamente en:

```text
data/private/Control_viaje_boda_Cielito_Ronaldo.xlsx
```

Desde la interfaz, ingresar como administrador, abrir **Importar / exportar**, elegir el XLSX, ejecutar **Vista previa**, revisar advertencias y confirmar. La confirmación vuelve a procesar el archivo dentro de una transacción.

También existe un comando administrativo:

```sh
dotnet run --project apps/api/TravelControl.Api.csproj -- --import data/private/Control_viaje_boda_Cielito_Ronaldo.xlsx --dry-run
dotnet run --project apps/api/TravelControl.Api.csproj -- --import data/private/Control_viaje_boda_Cielito_Ronaldo.xlsx
```

El importador busca hojas y columnas por nombre normalizado, conserva texto original, usa `Control pasajeros` y `Habitaciones` como fuentes autoritativas, no importa métricas del Dashboard y no duplica datos al repetir un archivo.

## Pruebas, lint y build

```sh
dotnet test TravelControl.slnx
cd apps/web
npm run lint
npm test
npm run build
```

E2E requiere el stack levantado y credenciales de un entorno efímero:

```sh
E2E_ADMIN_EMAIL=admin@example.test E2E_ADMIN_PASSWORD='...' npm run e2e
```

La prueba del workbook maestro se ejecuta si el archivo privado existe y se omite de forma segura cuando no está disponible en CI.

## Exportaciones

La pantalla administrativa genera:

- XLSX con Dashboard, Control pasajeros, Habitaciones y Fuentes y uso.
- CSV de pasajeros y XLSX operativo de pendientes.
- Respaldo JSON para administradores.

Los pasaportes se exportan enmascarados. El XLSX usa fechas `DD/MM/YYYY`, filtros, filas congeladas, anchos y estados en español.

## Comprobantes

Los PDF, PNG y JPEG se guardan en el volumen `attachment-data`, fuera del frontend y del repositorio. Solo se sirven mediante endpoints autenticados. Se valida tamaño, extensión derivada, MIME declarado, firma binaria, nombre seguro y SHA-256. Un hash repetido no vuelve a almacenar el archivo.

## Backups y restauración

- PostgreSQL: usar `pg_dump` contra el servicio `db` y cifrar el archivo resultante.
- Comprobantes: respaldar el volumen `attachment-data` junto con la base para mantener referencias consistentes.
- Configuración Caddy: respaldar `caddy-data` si se confía en su CA local.
- Probar periódicamente la restauración en un entorno aislado.

No almacenar backups con pasaportes o documentos en servicios públicos sin cifrado y control de acceso.

## Despliegue

Consultar [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md). Para producción real, reemplazar el host `localhost` y el certificado interno de Caddy por dominio y certificado válidos, rotar secretos, habilitar backups automáticos y centralizar logs sin datos sensibles.

## Documentación técnica

- [Arquitectura](docs/ARCHITECTURE.md)
- [Modelo de datos](docs/DATA_MODEL.md)
- [Reglas de negocio](docs/BUSINESS_RULES.md)
- [Importación y exportación](docs/IMPORT_EXPORT.md)
- [Seguridad](docs/SECURITY.md)
- [Despliegue](docs/DEPLOYMENT.md)
- [Decisiones](docs/DECISIONS.md)
