# Formularios: menú y detalle completo

Estado: en desarrollo. Es la etapa siguiente a baja, reactivación y eliminación; requiere su propio code review y piloto. MF1 y DC0/DC1 fueron elegidos el 2026-10-20, con los ajustes de la versión 96 del lienzo. Fuente visual: [diseño del front](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros).

## Alcance decidido

- Una entrada «Formularios» dentro de Gestión de formularios reúne la tabla de todos los formularios y su cantidad por aprobar, en lugar de Respuestas y Por aprobar del panel. Diseñar formularios, Catálogos y Formularios que faltan conservan sus tareas actuales.
- El segmentado dice «Activos (N) · Dados de baja (N)». Los dados de baja tienen su aviso; se indica si alguno espera una decisión. En el celular sigue siendo una tabla con desplazamiento contenido.
- Quien sólo aprueba ve sus formularios y cuántas esperan, sin cantidad de respuestas ni última respuesta. Quien sólo lee respuestas no ve Por aprobar ni su cantidad.
- D2 queda como vista rápida y suma Abrir en pantalla completa. En el celular se abre directamente la pantalla completa, sin vista rápida. Quien sólo aprueba no accede a la pantalla completa. La pantalla completa usa DC1 ajustado: respuesta y actividad a todo el ancho, plegables, con la actividad contada como historia.
- Desde Por aprobar, ↑ ↓ recorren lo pendiente de más viejo a más nuevo; desde la planilla recorren esa planilla. Un enlace directo a pantalla completa no incluye filtros privados.

## Orden de implementación ya evaluado

Primero en el front: separar el contenido del detalle de su panel FormResponseDetailPage; autorizar el menú y las rutas con leer o aprobar; reutilizar una sección plegable común y un único componente de historia en Actividad y pantalla completa.

Después en el backend: guardar qué acción generó cada documento para ubicar el PDF en Al aprobarla; agregar un lector para la tabla, con cantidad desde LastRecordNumber y un índice de tareas abiertas por formulario. Retirar api/forms/navigation y approvals/options junto con sus consumidores.

La bandeja global se borra al final. La baja y la reactivación siguen las [reglas vigentes](../../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21); esta etapa no vuelve a decidirlas. Copiar formularios, la retención de respuestas y la herramienta de desarrollo para limpiar datos de prueba siguen fuera de este alcance. La limpieza de prueba se resuelve aparte, fuera del producto; no permite borrar respuestas desde el formulario.

## Avance — 2026-10-21

- Parte A previa: reserva de interpretación Manual por conversación, con cuotas y replay conservados (`efe3b3e8`); build y pruebas rápidas aprobados. La integración de tres chats en Assistant y Manual quedó preparada y compilada (`c1f139be`) para el cierre conjunto con Docker.
- Parte B: en curso el primer corte del front, sin cambio visible; las rutas nuevas y MF1 se integran después de su contrato backend.
