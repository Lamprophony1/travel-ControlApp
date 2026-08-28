# Seguridad

Identity usa hashes, contraseña compleja de 12 caracteres, bloqueo tras cinco fallos y rate limit. La sesión es cookie HttpOnly, SameSite Strict y Secure en producción; cada escritura requiere antiforgery. No hay tokens en localStorage.

Pasaportes se enmascaran por defecto y solo Editor/Administrator pueden revelarlos. Las respuestas `/api` envían `no-store`. CSP, `nosniff`, denegación de framing, referrer y permissions policy se aplican globalmente.

Adjuntos PDF/PNG/JPEG se validan por tamaño, MIME y firma binaria, se renombran por UUID, se deduplican con SHA-256 y quedan fuera de `wwwroot`. El service worker no cachea datos ni documentos.

El repositorio ignora `data/private`, `.env`, SQLite local, adjuntos, builds y reportes. Producción debe usar TLS externo, secretos del orquestador, backup cifrado, revisión de auditoría y un proxy que sobrescriba encabezados reenviados.
