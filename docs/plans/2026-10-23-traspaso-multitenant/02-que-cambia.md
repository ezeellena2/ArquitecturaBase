# 02 · Qué cambia del traspaso anterior y de la hoja de ruta

Este capítulo contrasta lo nuevo con dos documentos del multitenant:
- el [traspaso del 2026-10-17](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/tree/main/docs/plans/2026-10-17-formularios-traspaso);
- la [hoja de ruta de formularios](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/plans/2026-10-12-formularios-dinamicos-hoja-de-ruta.md), con sus decisiones 1 a 33.

Cada fila dice qué cambió en ArquitecturaBase y qué tiene que revisar el multitenant. **No reemplaza las decisiones del multitenant:** su condición sigue valiendo: si algo choca con una regla escrita del multitenant, manda la del multitenant. Lo que sigue son cosas que conviene volver a decidir con el usuario, con lo que se sabe ahora.

## Lo que ya no vale del traspaso del 17/10

| Tema | Decía el traspaso del 17/10 | Hoy en ArquitecturaBase | Fuente |
| --- | --- | --- | --- |
| Idioma de formularios | Regla bilingüe de E/P1 (anexo C) | Idioma original obligatorio y traducción opcional desde el 19/10. Desde el 23/10, además, se escribe en el idioma del menú, sin selector en pantalla, y la regla abarca catálogos, campos, opciones y datos de las personas | [formularios, idioma](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19), [04](04-reglas-del-usuario.md#2-idioma-del-contenido-2026-10-23) |
| Catálogos | A mano y con filtros (Usuarios y Roles), hasta 200 opciones | E1: tipo de valor, carga de una vez y elegir varias, con hasta 10.000. E2: campos extra, qué viaja a WhatsApp, datos de las personas y filtros por destinatario | [formularios E1](../../features/formularios.md#catálogos-base-de-la-etapa-e1--2026-10-21), [E2](../../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23) |
| Pantalla de formularios | Lista de formularios y planilla por formulario | MF1: «Formularios» con el bloque «Por aprobar», D2 como vista rápida y DC0/DC1 como pantalla completa | [formularios, menú y detalle](../../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21) |
| Ciclo de vida | Copiar, eliminar y deprecar Flows quedaban para después | Dar de baja, reactivar y eliminar programados, con deprecación durable en Meta | [formularios, ciclo de vida](../../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21), [WhatsApp](../../features/whatsapp.md#deprecación-durable-de-flows--2026-10-21) |
| Asistente | Etapa 5 decidida, sin cerrar | Etapa 5 cerrada y en piloto. Latencia de 6,6 s (antes 35 s) y aislamiento por conversación. Casos del asistente decididos (ADR 0021) | [WhatsApp](../../features/whatsapp.md#espera-del-asistente-y-avisos-locales--2026-10-21) |
| Guía visual | «El botón se deshabilita y explica el estado»; `status` con «Cambios sin guardar» | Botones sin explicaciones, sin estados de guardado ni revisiones, acciones a la derecha | [04](04-reglas-del-usuario.md#1-interfaz-botones-sin-explicaciones-2026-10-23) |
| Programador | Un chat de Codex | Un chat de Claude, el «Codificador». Ver [05](05-calidad.md) y [06](06-forma-de-trabajo.md) | — |
| Pantallas de referencia | Las del lienzo hasta el 17/10 | Varias pantallas elegidas todavía muestran textos de explicación, «Cambios sin guardar» o el selector de idioma. **No se copian así:** se dibujan con las reglas del [04](04-reglas-del-usuario.md) | [anexo D](anexo-d-tableros-desde-17-10.md) |

## Decisiones de la hoja de ruta del multitenant que conviene revisar

| Decisión del multitenant | Qué aprendió ArquitecturaBase | Qué revisar |
| --- | --- | --- |
| **17 · Idioma.** Original obligatorio, traducción opcional, y «el idioma de las pantallas del sistema no cambia» | Desde el 23/10 el contenido **se escribe en el idioma del menú** y para traducir se cambia el menú. No hay selector en pantalla | Si se adopta la regla del 23/10 y cómo convive con «el idioma de las pantallas no cambia». Cómo se traduce un contenido desde una pantalla en el otro idioma |
| **20 · Catálogo a mano en otro idioma:** solo sirven los del mismo idioma original que el formulario | En ArquitecturaBase cada opción sin traducción usa su original, sin restringir catálogos. Desde el 23/10 el catálogo guarda su idioma original | Si se mantiene la restricción o se adopta el respaldo por opción |
| **11 y 21 · Nombres de más de 30 caracteres:** se cortan con «…» y el nombre completo va al detalle | ArquitecturaBase no recorta: E2 deja elegir qué campos van al título y a la descripción, y frena el envío si el título pasa los 30 | Si la presentación de E2, con título y descripción configurables, reemplaza el recorte |
| **12 y 22 · Catálogo con filtros de más de 200:** se guarda con aviso y el bloqueo es al mandar | Igual en ArquitecturaBase. E2 suma avisos que no frenan al publicar: máximo por persona y personas sin el dato. «Elegir varias» frena con más de 20 | Sumar los avisos de E2 y el tope de 20 de «elegir varias» |
| **Después del bloque:** «Copiar y eliminar formularios, y deprecar Flows viejos» | Ya se programaron y revisaron: baja, reactivación, eliminación lógica y deprecación durable | Si entran al bloque, por ejemplo en la E12 o la E15 |
| **Después del bloque:** «Intercambio cifrado con Meta: búsqueda en vivo, listas dependientes y listas largas» | Sigue en la hoja de ruta de catálogos. E1 subió el tope a 10.000 y la lista de WhatsApp muestra 200 | Sin cambios; conviene leer la hoja de ruta |
| **Asistente:** «Lo que no es un formulario no se contesta: queda sin leer para un operador» | Casos del asistente (ADR 0021) cambia esto: perfil, conocimiento, casos y límites configurables por organización; respuestas redactadas con límites; «Cualquier otra cosa» deriva | Rediseñar el resumen del asistente con los Casos ([anexo F](anexo-f-planes-pendientes.md)) |
| **E14 · Fotos y documentos:** archivos adjuntos a una pregunta | Documentos por WhatsApp (ADR 0022) agrega leer el documento con IA y proponer el formulario, la bandeja «Documentos recibidos» y el tablero por formulario | Si el multitenant lo planifica como etapa propia, después de la E14 y de los catálogos con campos extra |
| **E17 · Biblioteca de plantillas de la plataforma** | Documentos propone una «biblioteca de formularios estándar» con «Cómo reconocerlo», mapeo a datos estándar y validaciones fijas (CBU, CUIT) | Unir las dos: una plantilla de la plataforma puede traer su sección «Lectura de documentos» |
| **§4 · «En la Argentina "125.500" puede leerse como 125,5»** | Volvió a pasar dos veces: en la importación de E1 y en el cambio de tipo de E2 | Llevarlo a la lista de control de cada pantalla que interprete números |
| **§4.2 · «El contrato es el código: DTO, OpenAPI y ejemplos exportados»** | El chat de Inicio estuvo roto desde el 09/10 porque su contrato no tenía ejemplos exportados: el back manda `pending` y el front espera `Pending` | Exigir ejemplos exportados en todo contrato que consuma el front, sin excepciones |

## Pendientes del traspaso del 17/10 que ya se resolvieron

- **Etapa 5:** cerrada, con su review, y en piloto real ([formularios](../../features/formularios.md#cierre-del-review-de-la-etapa-5--2026-10-19)).
- **El respaldo de idioma en todo el recorrido,** que quedaba pendiente del piloto del 18: se resolvió con la regla del 19/10 y el piloto del 20.
- **Copiar, eliminar y deprecar:** eliminar y deprecar ya existen. Copiar sigue pendiente.

## Lo que sigue igual

- **Los puertos neutrales entre formularios y WhatsApp,** la operación durable con lease, fence y «Sin confirmar», el presupuesto de 16 KiB y la sesión de 24 h.
- **La robustez proporcional** (ADR 0018).
- **El orden:** primero el tablero, después el código.
