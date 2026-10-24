# Formularios por WhatsApp: traspaso 2 desde ArquitecturaBase (2026-10-23)

Autor: el chat guía de ArquitecturaBase. Destino: ArquitecturaBaseMutitenant.

Este paquete continúa el [traspaso del 2026-10-17](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/tree/main/docs/plans/2026-10-17-formularios-traspaso), que contó las Etapas 1 a 4 y lo decidido de la 5, verificado en el back `9575269d` y el front `9261e3d`.

Del 17 al 23 de octubre en ArquitecturaBase pasó mucho:
- se cerró la Etapa 5;
- cambió la regla de idiomas, dos veces;
- se hicieron un piloto total con Meta, Azure y OpenAI reales, el menú y el detalle nuevos de formularios, y dar de baja, reactivar y eliminar;
- se arreglaron la latencia y el aislamiento del asistente;
- se programaron los catálogos E1 y E2;
- se decidieron, sin programarse, Casos del asistente y Documentos por WhatsApp;
- se investigaron las integraciones con ARCA, Mercado Libre, Mercado Pago, MCP, API pública y webhooks.

**Lo más importante.** El usuario probó la aplicación y concluyó que **Codex programó bastante mal**: los code reviews encontraron errores graves en casi todas las etapas, y el 2026-10-23 aparecieron reglas nuevas de interfaz y de idioma. Por eso este traspaso cuenta qué se construyó, qué estaba mal, cómo se corrige y qué reglas nuevas hay que aplicar desde el primer día.

**Qué tiene que hacer el multitenant con esto:** evaluar cómo construir técnicamente lo mismo, adaptado a su modelo de organizaciones, plataforma y personas. Este paquete no es código para copiar. La [decisión 18 de su hoja de ruta](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/plans/2026-10-12-formularios-dinamicos-hoja-de-ruta.md) ya lo fija: se copian las pantallas y las reglas de producto; el código sale del multitenant.

## Cómo leerlo

| Archivo | Qué cuenta |
| --- | --- |
| [01 · Qué pasó del 17 al 23](01-cronologia.md) | Día por día, qué se decidió, qué se construyó y qué se encontró. |
| [02 · Qué cambia del traspaso anterior](02-que-cambia.md) | Lo que el traspaso del 17/10 y la hoja de ruta del multitenant dicen y ya no vale, o que hay que volver a decidir. |
| [03 · El producto hoy](03-producto-hoy.md) | Qué existe y cómo funciona de punta a punta, área por área. |
| [04 · Reglas nuevas del usuario](04-reglas-del-usuario.md) | Interfaz, idioma, producto genérico y soluciones simples. Valen para todo lo que se construya. |
| [05 · Por qué falló la implementación y qué controlar](05-calidad.md) | El cambio de Codex a Claude, los patrones de error y la lista de control. |
| [06 · Forma de trabajo](06-forma-de-trabajo.md) | Los tres chats, los prompts completos, los reviews, la maqueta viva y las guías por carpeta. |
| [07 · Adaptación al multitenant, etapa por etapa](07-adaptacion.md) | Para cada etapa E12–E17, el asistente y lo de después: qué sumar a su spec, qué cambiar y qué evaluar. |

**Anexos.** Son el relevamiento exacto, verificado en el código y en los documentos de ArquitecturaBase al 2026-10-23:
- [A · Backend desde el 17/10](anexo-a-backend-desde-17-10.md): entidades, migraciones, servicios, rutas, permisos, errores, Meta e IA.
- [B · Front desde el 17/10](anexo-b-front-desde-17-10.md): pantallas zona por zona, estados, contratos, cohortes y pruebas.
- [C · Reglas y decisiones vigentes](anexo-c-reglas-vigentes.md): cada regla con su fuente y lo que reemplaza.
- [D · Tableros desde el 17/10](anexo-d-tableros-desde-17-10.md): lo elegido y lo descartado, con el texto de las notas.
- [E · Reviews, pilotos y lecciones](anexo-e-reviews-y-lecciones.md): cada hallazgo, su causa y su arreglo, y la lista «no repetir».
- [F · Planes decididos sin construir](anexo-f-planes-pendientes.md): Casos del asistente, Documentos, catálogos, integraciones y producción.

## Estado del código al escribir esto

- **Catálogos E2 en corrección.** Las corrige el chat «Codificador», de Claude, con los 39 puntos del [review](../../reviews/2026-10-23-code-review-catalogos-e2.md). Los anexos describen el código de ese día y marcan, con el número del punto, el comportamiento correcto que tiene que quedar.
- **Piloto único con Meta pendiente.** Junta el ciclo de vida, el menú y el detalle, los contactos simultáneos y los catálogos E1 y E2 ([lista](../verificacion-y-produccion.md#formularios-piloto-único-con-meta-después-de-e2)).
- **Dónde leer el código.** Está en GitHub: `ezeellena2/ArquitecturaBase` y `ezeellena2/ArquitecturaBaseFront`, rama `main`. Antes de leerlo, traé los cambios.

## Dónde ver los diseños desde otra PC

- **Lienzo** «Formularios por WhatsApp», con todos los tableros: https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ
- **Maqueta viva:** el front real en modo demostración, con un backend falso, un celular simulado y las propuestas marcadas. Está en https://claude.ai/artifact/CM56W2Yc4VVzwMmxCKFpdP, y en la barra, «Recorridos» abre los recorridos guiados.
- **Capturas y registro de lo elegido:** `ArquitecturaBaseFront/docs/design/`, en `formularios-whatsapp.md`, `catalogos-mejoras.md`, `catalogos-campos-extra.md`, `documentos.md` y `evidence/`.
- **Generadores:** los repos del generador del lienzo y de la maqueta (`lienzo-formularios`, `maqueta-formularios`) son locales, sin remoto. Desde otra PC se ven solo los Artifacts publicados.

## Fuentes canónicas en ArquitecturaBase

- **Reglas implementadas:** [formularios](../../features/formularios.md), [WhatsApp](../../features/whatsapp.md) e [inteligencia artificial](../../features/inteligencia-artificial.md).
- **Decisiones:** [ADR](../../decisions/README.md). Las nuevas desde el 17/10 son la 0021 (respuestas redactadas por el asistente) y la 0022 (lectura de documentos con IA).
- **Planes vigentes:** el [índice](../README.md), [verificación y producción](../verificacion-y-produccion.md), la [hoja de ruta de catálogos](../2026-10-21-catalogos-con-origenes-externos.md), [Casos del asistente](../2026-10-21-casos-del-asistente.md), [Documentos por WhatsApp](../2026-10-22-documentos-por-whatsapp.md) y el [estudio de integraciones](../integraciones/estudio-2026-10-22.md).
- **Code reviews:** [`docs/reviews/`](../../reviews/).
- **Reglas de diseño:** [visual-baseline del front](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), con las reglas del 2026-10-23.
