# Despliegue

`docker compose up --build -d` ejecuta una sola imagen no-root. El host publica solo `127.0.0.1:${PORT:-8080}`; un reverse proxy externo aporta dominio y TLS. Con HTTPS, configurar `COOKIE_SECURE=true`.

El volumen `travel-control-data` contiene SQLite, adjuntos y claves de Data Protection. El bind `data/private` es solo lectura. La migración se aplica antes de servir tráfico y `/health/ready` comprueba conexión.

Antes de actualizar, crear un snapshot o backup consistente del volumen. Para restaurar, detener el contenedor, recuperar el volumen completo y volver a iniciar la misma imagen. No restaurar solo SQLite si cambió el conjunto de adjuntos.

CI compila, prueba, construye el contenedor y ejecuta Playwright real. En `main` publica SHA/latest en GHCR. El job de despliegue requiere un runner propio con etiqueta `travel-control`; ese runner conserva el volumen y ejecuta Compose con la imagen SHA.

## Cloudflare Tunnel

1. Crear un Tunnel en Cloudflare Zero Trust y asignarle un hostname HTTPS.
2. Instalar `cloudflared` en el host del runner, fuera del contenedor.
3. Apuntar el servicio del túnel a `http://127.0.0.1:8080`; Compose no expone la aplicación a la red pública.
4. Configurar `COOKIE_SECURE=true`, Access/SSO si corresponde y conservar la validación de origen/host en Cloudflare.
5. Ejecutar el túnel como servicio con el token guardado en el almacén seguro del host, nunca en Git.
6. Comprobar desde el hostname `/health/ready`, login, cookies Secure y encabezado `X-Forwarded-Proto: https`.

Prueba efímera: `cloudflared tunnel --url http://127.0.0.1:8080`. Producción debe usar un túnel nombrado y administrado.
