# Acceso público de solo lectura

## Superficies separadas

Las rutas `/`, `/pasajeros` y `/pasajeros/:id` funcionan sin login y muestran el dashboard, la lista y el detalle operativo sanitizado. `/gestion` y todas sus subrutas requieren sesión. Las rutas privadas históricas redirigen a su equivalente bajo `/gestion`.

La API anónima es exclusivamente:

- `GET /api/public/dashboard`.
- `GET /api/public/passengers`.
- `GET /api/public/passengers/{id}`.

Los endpoints `/api/dashboard`, `/api/passengers`, `/api/rooms`, `/api/flights`, `/api/baggage`, `/api/attachments`, exportaciones y escrituras siguen protegidos. Un Viewer autenticado conserva lectura privada completa y no equivale a un visitante anónimo.

## Campos permitidos

Dashboard: nombre y destino del viaje, conteos, porcentaje, cinco categorías, resumen por operadora, faltantes, alertas generales sanitizadas, transfer booleano y última actualización sin usuario.

Pasajero: ID, nombre según `NameMode`, operadora, código interno, hotel/propiedad, tipo, check-in/check-out, estado general, porcentaje, los cinco estados, nombres genéricos de faltantes, alertas sanitizadas y transfer booleano.

Nunca se emiten las claves `passportNumber`, `maskedPassport`, `birthDate`, `nationality`, `passportExpiry`, `phone`, `email`, `dietaryRestrictions`, `notes`, `nextAction`, `nextActionDueDate`, `pnr`, `electronicTicketNumber`, `sourceReference`, `operatorContact`, `attachments`, `attachmentId`, `attachmentLinkId`, `linkId`, `evidenceType`, `sourceId`, `managePath`, `affectedPassengerCount`, `ticketVersion`, `updatedById`, `followUps`, `audit`, `updatedBy` o `userName`. Los tests recorren el JSON completo y fallan si aparece alguna.

La búsqueda pública solo usa nombre normalizado y código interno. Los filtros permitidos son estado general, operadora, requisito y estado del requisito. La página máxima es 50.

## Configuración

Valores predeterminados productivos:

```env
PublicRead__Enabled=true
PublicRead__NameMode=Full
```

`NameMode` admite `Full`, `FirstNameLastInitial` e `Initials`. Para deshabilitar la API pública, establecer `PublicRead__Enabled=false` en `/opt/travel-control/travel-control.env` y redeplegar el mismo servicio; los endpoints responden 404. No hace falta una variable GitHub obligatoria.

## Controles

- Consultas EF sin tracking con DTOs públicos específicos.
- Rate limit `public-read`: 120 solicitudes/5 minutos/IP.
- `Cache-Control: no-store`, `Pragma: no-cache` y `X-Robots-Tag`.
- `robots.txt` con `Disallow: /` y meta robots.
- Sin CORS abierto, JSONP, archivos, adjuntos, exportaciones ni Swagger anónimos.
- Service worker sin cache de `/api/`.

`noindex` no es seguridad. Solo evita indexación cooperativa; la confidencialidad proviene de no seleccionar ni serializar campos privados.

## Compartir y PWA

“Compartir enlace” usa Web Share cuando está disponible y copia una URL same-origin sin tokens como fallback. La PWA tiene `start_url: /`, por lo que abre el dashboard público. No se cachean respuestas operativas.

El banner usa la misma fotografía de readiness que gestión y Excel. Su acción prioriza casos en atención y requisitos individuales; cuando solo queda transfer o alojamiento global desplaza a `#transfer-status` o `#accommodation-status`. Ambas secciones son informativas y el transfer público nunca ofrece escritura.

## Rollback

El rollback cambia únicamente la imagen a un SHA anterior con el Compose existente. SQLite, adjuntos, claves y workbook permanecen en `/opt/travel-control`. Seguir `DEPLOYMENT.md`, comprobar health local/HTTPS y validar nuevamente conteos agregados.
