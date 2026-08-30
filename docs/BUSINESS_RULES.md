# Reglas de negocio

Cada pasajero tiene exactamente cinco categorías, de 20% cada una: pasaporte, documentación, habitación, vuelo y equipaje. `Confirmed` resuelve; `NotApplicable` solo resuelve con justificación; `NotIncluded` genera atención.

- Pasaporte: requiere número, nacionalidad, nacimiento y vencimiento; se alerta si vence antes del regreso o dentro del umbral preventivo.
- Documentación: es independiente del transfer. Confirmarla exige pasaporte revisado, ticket efectivo, habitación efectiva y evidencia o referencia de vuelo. Si una dependencia deja de ser válida, el dato histórico no se borra: el estado efectivo pasa a pendiente con “Confirmación documental desactualizada”.
- Habitación: exige operadora, fechas válidas, tipo y fuente. Superar capacidad requiere excepción documentada.
- Vuelo: confirmación exige aerolínea, PNR, ticket electrónico y segmentos mínimos válidos de ida y regreso, con número, aeropuertos, salida, llegada y secuencia consistente. La edición es diferencial por ID: conserva número, estado y notas del ticket; retirar un ticket confirmado exige confirmación explícita y `Version` evita sobrescrituras concurrentes.
- Equipaje: exige reserva asociada y ticket efectivo, una maleta o más, al menos 23 kg y cobertura de ida y regreso, salvo excepción justificada. La acción grupal confirma solo elegibles y devuelve omitidos con motivo.

`Ready` requiere cinco categorías efectivamente resueltas sin alertas críticas. Los placeholders de propiedad (“Por confirmar”, “Sin definir”, “Propiedad exacta pendiente” y equivalentes) no resuelven `SpecificPropertyPending`; la regla es compartida por importación y edición manual.

`NotApplicable` conserva su estado diferenciado y solo cuenta como resuelto con una razón no vacía: documentación usa `DocumentationExceptionReason`, habitación usa la justificación de capacidad u observaciones, vuelo usa notas y equipaje usa `ExceptionReason`.

La preparación global pondera pasajeros al 90% y el único transfer grupal al 10%. El viaje solo queda listo cuando todos los pasajeros están listos, el transfer global está confirmado y no hay alerta global crítica.

## Reglas derivadas y bloqueantes globales

El estado del PNR es de solo lectura. Es `Confirmed` únicamente con PNR, aerolínea, segmentos válidos, pasajeros y todos sus tickets efectivos confirmados; es `InProgress` cuando existe información real incompleta, y `ToVerify` cuando está esencialmente vacío. El backend ignora el estado enviado por clientes antiguos y recalcula al editar reserva, pasajeros o tickets.

El readiness global considera además transfer pendiente, reservas no resueltas, propiedades específicas pendientes, pasajeros en atención y ausencia anómala de pasajeros o habitaciones. Si el cálculo base llega a 100 pero el estado no es `Ready`, el progreso visible se limita a 99.

`BaggageProof` puede ser directo sobre `BaggageEntitlement` o compartido por `FlightBooking`. Se muestra como comprobante directo, compartido por PNR o sin evidencia. Sigue siendo informativo: la regla de franquicia se confirma por ticket efectivo, cantidad/peso y cobertura o excepción justificada.

La importación de identificación prioriza `Identificación`, `Pasaportes`, `Documentos`, `Datos pasajeros` y `Datos de pasajeros`; luego puntúa las cinco columnas. Un empate bloquea y exige `sheetName`. Fechas futuras, edad superior a 120 años, vencimiento anterior al nacimiento o fecha imposible bloquean. Vencido, anterior al regreso, dentro del umbral o formato ambiguo son advertencias preventivas y no declaraciones migratorias.
