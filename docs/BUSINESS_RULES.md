# Reglas de negocio

Cada pasajero tiene exactamente cinco categorías, de 20% cada una: pasaporte, documentación, habitación, vuelo y equipaje. `Confirmed` resuelve; `NotApplicable` solo resuelve con justificación; `NotIncluded` genera atención.

- Pasaporte: requiere número, nacionalidad, nacimiento y vencimiento; se alerta si vence antes del regreso o dentro del umbral preventivo.
- Documentación: significa exclusivamente acceso funcional al ticket en el sitio oficial. Todas las reservas aplicables deben tener `TicketAccessStatus=Verified` y URL válida. `Generated` queda en gestión; un acceso faltante o inválido queda por verificar. Pasaporte, hotel, adjuntos y `Passenger.DocumentationStatus` legacy no intervienen.
- Habitación: exige operadora, fechas válidas, tipo y fuente. Superar capacidad requiere excepción documentada.
- Vuelo/ticket: es efectivo cuando la reserva asociada tiene PNR y aerolínea no vacíos y el `PassengerFlight.TicketStatus` está `Confirmed`. El número electrónico y los segmentos son opcionales; un PNR nunca se copia al número electrónico. La edición es diferencial por ID: conserva número, estado y notas; retirar un ticket confirmado exige confirmación explícita y `Version` evita sobrescrituras concurrentes.
- Equipaje: pertenece a `FlightBooking`. Una escritura sobre el PNR afecta por cálculo a todos sus pasajeros. `Confirmed` exige maleta incluida, cantidad mínima 1, peso mínimo 23 kg, ida y regreso. Con varias reservas se aplica la precedencia `NotIncluded`, `ToVerify`, `InProgress` y finalmente todos `Confirmed`.

`Ready` requiere cinco categorías efectivamente resueltas sin alertas críticas. Los placeholders de propiedad (“Por confirmar”, “Sin definir”, “Propiedad exacta pendiente” y equivalentes) no resuelven `SpecificPropertyPending`; la regla es compartida por importación y edición manual.

`NotApplicable` se reserva para excepciones reales documentadas. No se utiliza para Documentación y no forma parte del flujo normal de equipaje.

La preparación global pondera pasajeros al 90% y el único transfer grupal al 10%. El viaje solo queda listo cuando todos los pasajeros están listos, el transfer global está confirmado y no hay alerta global crítica.

## Reglas derivadas y bloqueantes globales

El estado del PNR es de solo lectura. Es `Confirmed` con PNR, aerolínea, al menos un pasajero y todos sus estados individuales de ticket confirmados; no requiere itinerario detallado ni número electrónico. Es `InProgress` cuando existe información real incompleta, y `ToVerify` cuando está esencialmente vacío. El backend ignora el estado enviado por clientes antiguos y recalcula al editar reserva, pasajeros o tickets.

La importación privada de pasajeros y reservas acepta CSV UTF-8 separado por punto y coma o XLSX con encabezados equivalentes. Solo actualiza pasajeros existentes, agrupa reservas por PNR y exige vista previa, mismo SHA-256 y confirmación administrativa. Las coincidencias aproximadas son sugerencias enmascaradas y nunca se aplican sin selección manual. No crea ni elimina personas, habitaciones o itinerarios; no altera documentación, revisión de pasaporte, seguimientos ni adjuntos. `check_in` y `check_out` se reconocen pero se ignoran.

El readiness global considera además transfer pendiente, reservas no resueltas, propiedades específicas pendientes, pasajeros en atención y ausencia anómala de pasajeros o habitaciones. Si el cálculo base llega a 100 pero el estado no es `Ready`, el progreso visible se limita a 99.

Todo `BaggageProof` nuevo se vincula directamente a `FlightBooking`. Los vínculos existentes sobre `BaggageEntitlement` se conservan como evidencia legacy, pero no se crean nuevos. Un mismo archivo puede tener vínculos `AirTicket` y `BaggageProof` sobre el PNR.

La importación de identificación prioriza `Identificación`, `Pasaportes`, `Documentos`, `Datos pasajeros` y `Datos de pasajeros`; luego puntúa las cinco columnas. Un empate bloquea y exige `sheetName`. Fechas futuras, edad superior a 120 años, vencimiento anterior al nacimiento o fecha imposible bloquean. Vencido, anterior al regreso, dentro del umbral o formato ambiguo son advertencias preventivas y no declaraciones migratorias.
