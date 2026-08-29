# Seguridad

Identity usa hashes, contraseña compleja de 12 caracteres, bloqueo tras cinco fallos y rate limit. La sesión es cookie HttpOnly, SameSite Strict y Secure en producción; cada escritura requiere antiforgery. No hay tokens en localStorage.

Pasaportes se enmascaran por defecto y solo Editor/Administrator pueden revelarlos. Las respuestas `/api` envían `no-store`. CSP, `nosniff`, denegación de framing, referrer y permissions policy se aplican globalmente.

La API pública está aislada bajo `/api/public`, admite únicamente GET, usa DTOs de lista blanca y aplica `public-read` (120 solicitudes cada 5 minutos por IP). Nunca incluye pasaporte —ni siquiera enmascarado—, nacimiento, contacto, PNR, ticket, referencias, notas, adjuntos, seguimientos, auditoría o identidad del usuario. La búsqueda anónima solo considera nombre y código interno. El máximo de página es 50.

Todas las respuestas llevan `X-Robots-Tag: noindex, nofollow, noarchive`; también existen `robots.txt` y meta robots. Esto reduce indexación accidental, pero no es un control de acceso: la seguridad real es la proyección explícita y reducida de los DTOs públicos. No hay CORS abierto, JSONP, Swagger productivo, archivos ni exportaciones anónimas. El service worker excluye `/api/` y `/health/`.

El token antiforgery se renueva después de login y logout porque queda vinculado a la identidad actual. El último administrador activo no puede desactivarse ni degradarse, y un usuario no puede autodesactivarse. Creaciones, cambios de rol/estado, resets sin contraseña e intentos bloqueados se auditan.

Adjuntos PDF/PNG/JPEG se validan por tamaño, MIME y firma binaria, se renombran por UUID, se deduplican con SHA-256 y quedan fuera de `wwwroot`. El service worker no cachea datos ni documentos.

El repositorio ignora `data/private`, `.env`, SQLite local, adjuntos, builds y reportes. Producción usa cookies Secure, Cloudflare TLS, puerto ligado solo a loopback y archivos persistentes bajo `/opt/travel-control`. El `.env` tiene modo 600 y el workbook se monta read-only. Los backups locales incluyen datos privados y deben copiarse a almacenamiento externo cifrado.
