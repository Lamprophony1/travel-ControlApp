# Importación y exportación

ClosedXML detecta hojas y encabezados por texto normalizado. `Control pasajeros` y `Habitaciones` son las únicas fuentes maestras; Dashboard y hojas informativas se ignoran. El último registro maestro gana ante duplicados y se informa el conflicto.

El dry-run no escribe, pero ejecuta parseo, comparación y validaciones completas. El commit repite el análisis dentro de una transacción y es idempotente. Se conservan tildes para mostrar y solo se normaliza para comparar. No se registran nombres, pasaportes ni filas del workbook en logs.

En una base vacía, el bootstrap privado es opt-in. Si se marca `Required`, un archivo ausente o conteos distintos de 46 pasajeros/25 habitaciones impiden el arranque. Enriquecimientos opcionales de invitados o identificación solo actualizan campos explícitos.

Las exportaciones incluyen dashboard calculado, pasajeros con pasaporte enmascarado, habitaciones, fuentes, pendientes y backup JSON administrativo. El transfer pendiente aparece una sola vez como asunto global.

La actualización privada de pasajeros y tickets usa `row`, `name`, `passport`, `birth_date`, `passport_expiry`, `nationality_code`, `pnr`, `airline_code`, `check_in` y `check_out`. Valida UTF-8, duplicados, contradicciones, propiedad de pasaporte y fechas imposibles. Solo completa campos vacíos salvo confirmación explícita de sobrescritura; nunca confirma documentación ni revisión de pasaporte. Para confirmar se exige repetir el mismo archivo, el mismo hash y los alias manuales revisados. Los PNR se agrupan sin copiarlos a `ElectronicTicketNumber`; una reserva diferente requiere aceptación explícita y se agrega sin borrar asociaciones existentes; `check_in` y `check_out` quedan fuera de la persistencia.

Los CSV/XLSX privados exportados muestran `Aerolínea`, `Nro. de reserva`, `Estado ticket` y `Número ticket electrónico`. Si el número electrónico no existe, permanece vacío; no se sustituye con el PNR.
