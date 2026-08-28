# Decisiones técnicas

## ADR-001 · .NET 10 y Node 24

El repositorio estaba vacío y el entorno disponible tenía .NET 10. Se eligieron versiones estables compatibles en agosto de 2026, sin introducir una versión antigua solo por plantilla.

## ADR-002 · PostgreSQL como única persistencia productiva

Se mantiene PostgreSQL en desarrollo y producción Docker. InMemory se usa únicamente para pruebas unitarias del importador, nunca como modo operativo.

## ADR-003 · Cookies sobre tokens de navegador

La aplicación es privada y same-origin. Una cookie HttpOnly/Secure con CSRF evita exponer credenciales a JavaScript y simplifica revocación/bloqueo.

## ADR-004 · Cálculos en backend

Estados, alertas y porcentaje no se editan ni se confían al frontend. Un único servicio de reglas alimenta listas, detalle, dashboard y exportación.

## ADR-005 · Importación conservadora

Se conservan texto y acentos, pero una marca “Confirmado” sin campos obligatorios no pasa silenciosamente. Se registra advertencia y queda Por verificar. La prioridad es no producir una falsa sensación de viaje resuelto.

## ADR-006 · Top Travel pendiente sin revocar confirmación

La propiedad exacta del complejo es información pendiente, no ausencia de reserva. Se representa como alerta informativa separada del estado confirmado.

## ADR-007 · PWA solo para shell estático

No se promete edición offline. Cachear datos personales o documentos sería un riesgo y generaría conflictos. El service worker almacena únicamente la interfaz y muestra una pantalla offline explícita.

## ADR-008 · HTTPS local con Caddy

Las cookies Secure requieren HTTPS incluso en el stack Docker. Caddy reduce configuración y genera una CA interna para `localhost`; un despliegue real debe usar certificado público.
