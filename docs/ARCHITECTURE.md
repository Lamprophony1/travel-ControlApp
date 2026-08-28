# Arquitectura

La solución aplica dependencias hacia adentro: `Api → Infrastructure → Application → Domain`. Domain no conoce ASP.NET, EF ni ClosedXML. Application concentra contratos y reglas. Infrastructure implementa persistencia SQLite, Identity, importación/exportación y almacenamiento. Api solo compone el host y los endpoints.

El build de React se copia a `wwwroot`; navegador, estáticos y `/api` comparten origen. No hay CORS ni proxy interno. Un único contenedor no-root expone 8080 y usa un volumen para base, claves y adjuntos.

Las escrituras validan CSRF, rol, DTO, reglas y `Version`; un conflicto devuelve 409. Los estados de pasajeros, cinco categorías y preparación del viaje se calculan en backend. La UI invalida consultas después de guardar. Serilog evita payloads sensibles y los health checks separan proceso y base.

La PWA precachea únicamente shell estático. `/api` y `/health` no entran al cache ni se promete edición offline.
