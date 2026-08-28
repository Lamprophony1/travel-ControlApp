# Importación y exportación

## Importación

El pipeline carga el archivo en memoria, calcula SHA-256 y abre el workbook con ClosedXML. Busca hojas por nombre normalizado y detecta la fila de encabezados exigiendo varias columnas características, evitando confundir títulos o bandas agrupadoras.

Fuentes:

- `Control pasajeros`: pasajeros y asignación operativa.
- `Habitaciones`: reservas de alojamiento.
- `Dashboard`: ignorado como fuente; todas sus métricas se recalculan.
- `Fuentes y uso`: informativo.

La vista previa realiza el mismo parseo y comparación que la confirmación, pero no escribe. Informa altas, actualizaciones, sin cambios, errores y advertencias de conteos esperados. Los errores bloquean el commit; las advertencias permiten revisión administrativa.

La confirmación ejecuta una transacción. Las habitaciones se actualizan primero, luego los pasajeros y sus asociaciones. Repetir el archivo usa claves normalizadas existentes y no duplica registros.

## Caso Bespoke

Rafa y Clara se deduplican por nombre y se privilegia Bespoke frente a listas antiguas. La habitación Bespoke conserva White Sand, Junior Suite Garden View, All Inclusive, 06/09/2026–11/09/2026, capacidad 2 y contacto conocido. Las habitaciones Top Travel mantienen confirmación y marcan propiedad específica pendiente.

## Diferencia detectada en el workbook actual

El Dashboard del archivo muestra 22 tickets, y algunas filas dicen Confirmado sin aerolínea, PNR, ticket ni segmentos. Como el Dashboard no es fuente y esos registros no satisfacen la regla de confirmación, la previsualización advierte cada caso y los importa Por verificar. No se inventan detalles faltantes.

## Exportación

El XLSX se crea desde la base y contiene cuatro hojas compatibles: Dashboard, Control pasajeros, Habitaciones y Fuentes y uso. Aplica encabezados, filtros, filas congeladas, anchos acotados, formatos de fecha y estados en español. Los pasaportes se enmascaran.

El respaldo JSON está limitado a administradores y debe almacenarse cifrado porque contiene datos operativos.

También se generan un CSV de pasajeros con pasaportes enmascarados y un XLSX de pendientes ordenable para el trabajo diario.
