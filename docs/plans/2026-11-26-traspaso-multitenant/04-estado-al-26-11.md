# Traspaso 19 · 04 · El estado al 26/11

Reemplaza al [05 del 18](../2026-11-23-traspaso-multitenant/05-estado-al-23-11.md). Está tomado de lo commiteado hasta el
mediodía del 26/11: backend `76bca76d` y front `45c5115f`.

## 1. El proyecto quedó cerrado

El 26/11, con la corrida completa en verde (12.096 tests del backend, con la integración en 8 hilos, y el front entero:
build, lint, 6.445 tests de Vitest y Playwright), el usuario dio el proyecto por terminado sin los pasos a mano. La auditoría de
cierre (back `3b4c110c`, front `45c5115f`) hizo esto:

- **Se retiraron los planes terminados:** quince del backend (la carga desde la app, elegir primero por un campo, sólo en la app,
  las carpetas, el Excel en una hoja, las etapas 3 a 6 de ARCA, la IA que arma formularios y su etapa 2, documentos por
  WhatsApp, roles, ordenar las tablas y la respuesta como formulario) y ocho del front (las acciones del encabezado, los
  diálogos con la lista de chequeo, el motivo de lo apagado, un dato por columna, la pieza única, Información del negocio, la
  falta de permiso y las tarjetas del chat). Sus decisiones están en las fuentes vigentes, y la tabla de cada plan con su regla
  y sus pendientes, en el [historial](../../history/README.md#cierre-del-2026-11-26).
- **Se condensaron a lo que sigue abierto** la hoja de ruta de ARCA, los casos del asistente y las reglas a Configuración.
- **Lo que queda a mano o con cuentas reales** pasó a [verificación y producción](../verificacion-y-produccion.md): ARCA en
  homologación, las pruebas a mano de Formularios, del front y de la IA. Quedaron registrados como desvíos la zona de respaldo
  del Excel (XL2), dos pruebas automáticas de «sólo en la app» y «Ordenar por» con la pieza.
- **Un código que faltaba declarar:** «La IA no propuso ningún formulario» (`Forms.ReceivedDocument.NoFormProposal`) era una
  constante suelta y pasó a `FormReceivedDocumentErrors`, con sus textos, como los demás códigos de falla de la propuesta
  (`334ba8b0`). El código que lee el front no cambió.
- **El auditor documental quedó sin avisos estructurales** (`9e115900`): los enlaces de los anexos del traspaso 2 a tu repo
  pasaron a texto, porque apuntaban a una carpeta que no existe en esta PC.

## 2. Cada desarrollo

| Desarrollo | Estado | Dónde |
| --- | --- | --- |
| Ordenar las tablas (OT1-C) | Back y front completos; el plan, retirado. | [01](01-lo-que-termino-de-programarse.md) §1 |
| La pieza única para elegir | Completa, con la regla del foco y sus excepciones; el plan del front, retirado. | [01](01-lo-que-termino-de-programarse.md) §2 |
| La respuesta como el formulario (VF1-A y VF2-A) | Completa, con O52 y O56; el plan, retirado. | [01](01-lo-que-termino-de-programarse.md) §3 |
| Formularios en una sola entrada (FU1-B y FU2-A) | Programada, back y front. | [01](01-lo-que-termino-de-programarse.md) §4 |
| Información del negocio (IN1-C, IN2 e IN3) | Programada y probada a mano; el plan del front, retirado. | [01](01-lo-que-termino-de-programarse.md) §5 |
| La falta de permiso (SP1-B) | Programada y probada a mano; el plan del front, retirado. | [01](01-lo-que-termino-de-programarse.md) §6 |
| Las tarjetas del chat (TC1-B a TC5-A) | Programadas, con sus escenas revisadas; el plan del front, retirado. | [01](01-lo-que-termino-de-programarse.md) §7 |
| Reglas a Configuración (TO1-A y RC1-A a RC7-B) | Programadas; el plan, condensado. Queda decidir RC5 en pantalla (los cupos de la IA por persona) y dos propuestas: las duraciones fijas del ingreso y la moneda de ARCA. | Traspaso 18, 01 |
| La prueba integral | Cerrada el 26/11 sin los pasos a mano. | [02](02-cierre-de-la-prueba-integral.md) |
| Las acciones del asistente (ADR 0034) y los procesos del campo (ADR 0033) | Sin programar. | Traspaso 16, 04 |
| Tickets en Jira y recorridos con Playwright | Planes escritos, sin programar. | Traspaso 15, 02 |

## 3. Dónde está cada cosa

- **En GitHub** están los dos `main`: el backend hasta `76bca76d` y el front hasta `45c5115f`, con todo lo que cita este
  traspaso. Este paquete se subirá sólo cuando el usuario lo pida.
- **Los planes que siguen vivos,** en el [índice de planes](../README.md); lo de a mano, en
  [verificación y producción](../verificacion-y-produccion.md).
- **Las reglas implementadas:** `docs/features/` (formularios, administración, identidad, IA, WhatsApp y ARCA) y, en el front,
  `docs/design/visual-baseline.md`.

## 4. Para el multitenant

Visto el 26/11 en la PC del usuario, tu `main` está en `b2334284` (20/11), igual que tu copia de GitHub, y tu front en
`c5172e65`. Incorporaste el traspaso 15 entero (tus decisiones 224 a 259, del 18 al 20/11) y estás en la segunda parte de la
base de la 12. **No incorporaste el 16, el 17 ni el 18.** El README y el 05 del 18 decían que seguías en `c339b58d`: era una
lectura vieja; el prompt de este traspaso parte de lo que tenés.
