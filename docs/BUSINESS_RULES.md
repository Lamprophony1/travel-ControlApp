# Reglas de negocio

## Estados comunes

`Confirmed`, `ToVerify`, `InProgress`, `NotIncluded` y `NotApplicable` se muestran como Confirmado, Por verificar, En gestión, No incluido y No aplica. `NotApplicable` solo resuelve una categoría si existe justificación. `NotIncluded` nunca resuelve.

## Pasaporte

Se considera incompleto si falta número, nacionalidad, nacimiento o vencimiento. Está vencido si vence antes del regreso individual. El umbral preventivo inicial es 180 días y se configura por viaje; no representa asesoría migratoria.

## Habitación

Confirmar exige asignación, operadora, fechas válidas, tipo y fuente. La ocupación no puede superar capacidad salvo excepción documentada. La propiedad exacta pendiente de Top Travel crea una alerta informativa y no revoca la confirmación.

## Vuelo

El ticket individual necesita aerolínea, PNR, número electrónico, segmento de ida, segmento de regreso y fecha de verificación. El importador degrada a Por verificar un “Confirmado” heredado que no tenga evidencia suficiente.

## Equipaje

Confirmar requiere ticket asociado confirmado, una o más maletas, 23 kg o más, ida y regreso o excepción, y fecha de verificación. Menos de 23 kg o tarifa sin equipaje se muestra como condición crítica.

## Transfer

Se resuelve con un voucher `Both` confirmado o dos vouchers confirmados que cubran llegada y salida. Una sola dirección permanece pendiente. Confirmar exige empresa, voucher, pasajeros y verificación.

## Estado general

El avance usa exactamente seis categorías: pasaporte, documentación, habitación, vuelo, equipaje y transfer.

- `Ready`: seis resueltas y sin alerta crítica.
- `Pending`: al menos una por verificar, en gestión, vacía o parcial.
- `Attention`: pasaporte vencido, requisito no incluido, falta de habitación, fechas/capacidad incompatibles u otra alerta crítica.

La alerta de propiedad específica de Top Travel es informativa y, por sí sola, no cambia el pasajero a Atención.

