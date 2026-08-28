# Arquitectura

## Contexto

La solución es un monorepo con tres límites claros: interfaz web, API y persistencia privada. El frontend nunca se conecta a PostgreSQL ni al volumen de comprobantes. Caddy termina HTTPS y enruta únicamente `/api` y `/health` a la API.

```text
Navegador / PWA
      │ HTTPS
      ▼
Caddy ─────────────► recursos estáticos React
      │ /api
      ▼
ASP.NET Core API ──► PostgreSQL
      │
      └────────────► volumen privado de comprobantes
```

## Backend

- Minimal APIs agrupadas por recurso con DTOs/proyecciones; no se serializan grafos EF.
- Servicios separados para reglas, dashboard, consultas, importación, exportación y almacenamiento.
- EF Core con migraciones versionadas, índices únicos por viaje y token de concurrencia optimista.
- Identity gestiona usuarios, roles, hashing, bloqueo e inicio de sesión mediante cookie.
- El cálculo de estado general y porcentaje vive en `BusinessRules`, no en la UI.
- El dashboard se agrega en `DashboardService` desde registros persistidos.

## Frontend

- React Router protege el área privada y separa setup/login de la aplicación.
- TanStack Query mantiene estado remoto con revalidación; no guarda datos sensibles en almacenamiento persistente.
- TanStack Table resuelve selección y visibilidad de columnas. Solo la preferencia de columnas se guarda en `localStorage`.
- Material UI implementa componentes accesibles y diseño responsive; en móvil la tabla cambia a tarjetas y aparece navegación inferior.
- El service worker precachea exclusivamente archivos estáticos y excluye `/api` y `/health`.

## Flujo de escritura

1. El navegador obtiene un token antifalsificación.
2. Envía cookie HttpOnly y `X-XSRF-TOKEN`.
3. La API autentica, autoriza rol, valida DTO y reglas de negocio.
4. EF aplica transacción/concurrencia y persiste.
5. Se agrega auditoría resumida sin pasaporte completo.
6. TanStack Query invalida las vistas afectadas.

## Observabilidad

Serilog emite logs estructurados a consola. Los comandos SQL se reducen en producción y los endpoints de salud separan proceso vivo de conectividad a base.

