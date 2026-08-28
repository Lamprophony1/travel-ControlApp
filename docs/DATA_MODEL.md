# Modelo de datos

- `Trip` posee exactamente un `TripTransferStatus` global.
- `Passenger` conserva datos de viaje, operadora principal, habitación y próxima acción; no tiene responsable interno ni transfer individual.
- `RoomReservation` agrupa pasajeros bajo un código operativo estable.
- `FlightBooking` representa un PNR con segmentos; `PassengerFlight` conserva ticket y estado individual.
- `BaggageEntitlement` representa cantidad, peso y cobertura de ida/regreso.
- `Attachment`, `FollowUp`, `AuditLog` e `ImportRun` cubren evidencia, seguimiento, trazabilidad e importaciones.
- `AppUser` usa roles `Administrator`, `Editor` y `Viewer`.

Los GUID son estables. Pasajeros son únicos por viaje y nombre normalizado; pasaportes no vacíos también. Habitaciones son únicas por viaje y código. `Version` es un entero de concurrencia optimista incrementado en cada escritura, compatible con SQLite.

No se persisten noches, avance ni estado general: se derivan para impedir divergencias.
