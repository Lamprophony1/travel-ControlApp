# Auditoría responsive y accesible

La interfaz mantiene TypeUI Clean, Material UI, grilla de 8 px, paleta limitada y texto base de 16 px. Los encabezados principales no superan 28 px. Cada estado combina texto, icono y color; ninguna acción depende de hover o tooltip.

## Puntos verificados

- 360×800, 390×844 y 430×932: tarjetas de pasajeros, nombre/estado visibles, etiquetas de los cinco requisitos, navegación inferior, safe areas y objetivos táctiles mínimos de 44×44 px.
- 768×1024: transición sin overflow entre navegación móvil y escritorio.
- 1440×900: tabla pública densa con encabezado fijo, filtros, orden y paginación.
- Detalle privado: selector de sección en móvil; las ocho pestañas horizontales quedan solo para escritorio.
- Foco visible, enlace “Saltar al contenido”, landmarks, botones con nombres accesibles y movimiento reducido.
- `documentElement.scrollWidth <= clientWidth` se prueba en Playwright para cada viewport.

El dashboard usa rojo mientras exista cualquier entregable obligatorio pendiente y verde únicamente con todos los pasajeros listos, transfer confirmado y sin alertas globales. Los estados individuales en gestión conservan amarillo/azul según contexto.

Playwright también comprueba navegación pública sin login, manifest PWA, búsqueda pública, detalle sanitizado, administración autenticada y respuestas 401 de endpoints privados sin cookie.
