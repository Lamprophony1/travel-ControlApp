# Reglas de negocio

Cada pasajero tiene exactamente cinco categorías, de 20% cada una: pasaporte, documentación, habitación, vuelo y equipaje. `Confirmed` resuelve; `NotApplicable` solo resuelve con justificación; `NotIncluded` genera atención.

- Pasaporte: requiere número, nacionalidad, nacimiento y vencimiento; se alerta si vence antes del regreso o dentro del umbral preventivo.
- Documentación: es independiente del transfer. Confirmarla exige pasaporte, ticket, habitación y evidencia de vuelo/hotel suficientes.
- Habitación: exige operadora, fechas válidas, tipo y fuente. Superar capacidad requiere excepción documentada.
- Vuelo: confirmación exige aerolínea, PNR y segmentos de ida y regreso; el ticket se verifica por pasajero.
- Equipaje: una maleta o más, al menos 23 kg, ida y regreso. Puede confirmarse individualmente o para todo un PNR.

`Ready` requiere cinco categorías resueltas sin alertas críticas. La propiedad exacta pendiente de Top Travel es informativa y no invalida por sí sola una reserva confirmada.

La preparación global pondera pasajeros al 90% y el único transfer grupal al 10%. El viaje solo queda listo cuando todos los pasajeros están listos, el transfer global está confirmado y no hay alerta global crítica.
