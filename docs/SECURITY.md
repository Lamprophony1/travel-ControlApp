# Seguridad

## Identidad y sesión

- Primer administrador mediante setup de un solo uso.
- Roles: `Administrator`, `Editor`, `Viewer`.
- Contraseñas hasheadas por ASP.NET Identity, mínimo 12 caracteres y política compleja.
- Bloqueo por cinco fallos durante 15 minutos y rate limit por IP en setup/login.
- Cookie de sesión `Secure`, `HttpOnly`, `SameSite=Strict`; no hay tokens en localStorage.
- CSRF mediante token de solicitud y cookie antiforgery.

## Datos sensibles

- Pasaporte enmascarado por defecto; revelación solo para Editor/Administrador.
- API y proxy envían `no-store` para respuestas privadas.
- Service worker excluye API, salud y cualquier respuesta dinámica.
- Auditoría resume el pasaporte enmascarado y los logs no deben incluir payloads completos.
- `data/private/`, `.env`, comprobantes, backups y resultados de pruebas están ignorados.

## Archivos

Se admiten PDF, PNG y JPEG. Se verifica límite, MIME permitido, firma binaria, hash y nombre. El nombre interno es UUID y la ruta se valida dentro del root configurado. Los archivos están fuera del directorio web y requieren autorización para descargar.

## Red

Caddy fuerza HTTPS local. CORS acepta solo orígenes configurados. Se aplican CSP, `nosniff`, denegación de framing, política de referrer y permisos restringidos. La API usa errores centralizados sin stack trace.

## Operación recomendada

- Certificado público válido y dominio dedicado.
- Secretos desde un vault o secrets del orquestador.
- PostgreSQL sin puerto público.
- Backups cifrados con pruebas de restauración.
- Retención y revisión periódica de auditoría.
- Escaneo de imágenes y dependencias en CI.
- Antivirus/sandbox documental si aumenta el volumen o se aceptan más formatos.

