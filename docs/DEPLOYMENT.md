# Despliegue

## Docker Compose

`docker-compose.yml` crea PostgreSQL, API y web/proxy. Los datos viven en volúmenes nombrados. La API corre como usuario no root, con filesystem read-only y `/tmp` temporal; Caddy termina TLS y publica puertos 80/443.

```sh
cp .env.example .env
# editar .env
docker compose up --build -d
docker compose ps
docker compose logs -f api
```

La API espera a PostgreSQL saludable; web espera a API saludable. La migración se ejecuta al inicio antes de aceptar tráfico.

## Producción

1. Cambiar `https://localhost` por un dominio real en `infra/Caddyfile` y `ALLOWED_ORIGIN`.
2. Permitir que Caddy obtenga ACME o montar certificados administrados.
3. Usar secretos externos; no hornear `.env` en imágenes.
4. Limitar acceso al host, no publicar PostgreSQL y habilitar firewall.
5. Configurar backup de `postgres-data` y `attachment-data` con la misma ventana de consistencia.
6. Enviar logs a un destino con acceso restringido y política de retención.
7. Supervisar `/health/ready`, espacio de volúmenes, vencimiento TLS y fallos de login.

## Actualización

```sh
docker compose build --pull
docker compose up -d
docker compose ps
```

Respaldar antes de migraciones. Si una actualización falla, conservar imagen anterior y restaurar base/volumen como conjunto; no revertir solo la base después de que documentos o referencias hayan cambiado.

