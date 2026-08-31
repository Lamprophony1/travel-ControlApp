# Modelo de datos

- `Trip` posee exactamente un `TripTransferStatus` global.
- `Passenger` conserva datos de viaje, operadora principal, habitación y próxima acción; no tiene responsable interno ni transfer individual.
- `RoomReservation` agrupa pasajeros bajo un código operativo estable.
- `FlightBooking` representa un PNR compartido con aerolínea, segmentos opcionales y una única política de equipaje aplicable a todos sus pasajeros.
- `PassengerFlight` conserva el estado individual del ticket, un número electrónico opcional y los datos privados del acceso oficial (`BookingLookupLastName`, `AirlineOrderId`, URL, estado y token público opaco).
- `BaggageEntitlement` es una estructura legacy conservada únicamente para rollback y auditoría. El código nuevo no debe escribirla ni usarla como fuente de verdad.
- `Attachment`, `FollowUp`, `AuditLog` e `ImportRun` cubren evidencia, seguimiento, trazabilidad e importaciones.
- `AppUser` usa roles `Administrator`, `Editor` y `Viewer`.

Los contratos públicos (`PublicDashboardDto`, `PublicPassengerDto`, `PublicRequirementDto` y auxiliares) no son entidades ni reutilizan contratos privados. Contienen únicamente el estado operativo permitido y se construyen con consultas sin tracking y proyección explícita de salida.

Los GUID son estables. Pasajeros son únicos por viaje y nombre normalizado; pasaportes no vacíos también. Habitaciones son únicas por viaje y código. `Version` es un entero de concurrencia optimista incrementado en cada escritura, compatible con SQLite.

No se persisten noches, avance, estado general ni estados efectivos: se derivan para impedir divergencias. Un enum almacenado como `Confirmed` puede resultar pendiente si la estructura subyacente ya no satisface la regla; el histórico permanece intacto.

## Evidencia y tickets

`Attachment` representa el objeto físico: hash SHA-256, nombres, MIME, tamaño, ruta segura, descripción, autor y fecha. `AttachmentLink` representa una función documental sobre exactamente un destino y su `EvidenceType`. Sus índices únicos incluyen `AttachmentId + destino + EvidenceType`, por lo que un binario puede tener más de una función sin duplicarse.

`Attachment.DocumentType` y sus asociaciones directas legadas permanecen en el esquema para que una imagen anterior pueda abrir la migración aditiva. Son datos históricos de primera carga, no clasificación autoritativa; su retiro requerirá una migración futura y ventana de rollback cerrada.

`PassengerFlight` conserva la clave compuesta `(PassengerId, FlightBookingId)` y usa `Version` como token de concurrencia. `PublicTicketAccessToken` contiene 32 bytes aleatorios, es único y no codifica IDs, PNR, apellido ni `orderId`. La API pública solo entrega una ruta opaca; la URL real queda en el ámbito privado.
