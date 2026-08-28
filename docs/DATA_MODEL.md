# Modelo de datos

## Agregados principales

- `Trip`: viaje, fechas generales, zona horaria y umbral preventivo de pasaporte.
- `Operator`: agencia, operadora hotelera, aerolínea, transfer u otro proveedor.
- `Passenger`: datos personales, estado documental, responsable y siguiente acción.
- `RoomReservation`: reserva y código interno de grupo; varios pasajeros pueden compartirla.
- `FlightBooking`: PNR compartido y colección ordenada de `FlightSegment`.
- `PassengerFlight`: ticket electrónico y estado individual dentro del PNR.
- `BaggageEntitlement`: franquicia individual, opcionalmente asociada a una reserva aérea.
- `TransferBooking`: voucher grupal con cobertura llegada, salida o ambos.
- `PassengerTransfer`: relación muchos-a-muchos entre pasajero y transfer.
- `Attachment`: metadatos y ruta privada de un comprobante.
- `FollowUp`: tarea asociable a pasajero o habitación.
- `AuditLog`: cambio resumido, usuario y contexto.
- `ImportRun`: huella y resultado de una importación confirmada.

## Identidad e idempotencia

- Los IDs internos son UUID y permanecen estables después de la primera importación.
- Pasajero: único por `TripId + NormalizedName`; el pasaporte, cuando existe, también es único por viaje.
- Habitación: única por `TripId + InternalCode`.
- El PNR no es único porque distintas reservas o fuentes pueden reutilizar referencias; la asociación individual se resuelve en `PassengerFlight`.

La normalización elimina diacríticos solo para comparar, compacta espacios y usa mayúsculas invariantes. Los valores visibles conservan ortografía y acentos originales.

## Datos derivados

No se persisten noches, porcentaje o estado general. `Nights` se calcula entre check-in y check-out; pasaporte, requisitos, avance y `Ready/Pending/Attention` se calculan en backend para evitar divergencias.

## Concurrencia

Las entidades editables incluyen `Version`, mapeado como row-version por el proveedor PostgreSQL. Los PUT comparan versión y devuelven `409 Conflict` cuando otra persona guardó primero.

