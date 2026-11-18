# Traspaso 15 · 01 · El orden de lectura

Los traspasos 4 a 14 se escribieron en el orden en que se programó, no en el de tus etapas. Este recorrido los lee por
etapa, en cinco tandas: primero lo que vale para todas las pantallas, después tus etapas de la E12 a la E25 y al final lo
que queda fuera del bloque. En cada fila, las secciones van en el orden en que se leen; si dos dicen distinto, manda la
última.

## Abreviaturas

Todas las rutas son de `ArquitecturaBase/docs/plans/`. «T13/01 §3» es la sección 3 del archivo 01 del traspaso 13.

| Sigla | Archivo |
| --- | --- |
| T4 | `2026-11-02-traspaso-multitenant-ajustes-de-la-prueba.md` |
| T5 | `2026-11-03-traspaso-multitenant-lecturas-y-filtros.md` |
| T6 | `2026-11-06-traspaso-multitenant-arca-conexion.md` |
| T7 | `2026-11-06-traspaso-multitenant-carga-etapas-2-a-5.md` |
| T8 | `2026-11-06-traspaso-multitenant-ia-arma-formularios.md` |
| T9 | `2026-11-06-traspaso-multitenant-ajustes-de-toda-la-app.md` |
| T10/0N | `2026-11-07-traspaso-multitenant/0N-*.md` |
| T11 | `2026-11-08-traspaso-multitenant-ia-pregunta-y-corrida.md` |
| T12 | `2026-11-11-traspaso-multitenant-renglones.md` |
| T13/0N | `2026-11-14-traspaso-multitenant/0N-*.md` |
| T14/0N | `2026-11-16-traspaso-multitenant/0N-*.md` |
| 02 | el [02 de este traspaso](02-estado-al-18-11.md) |

## Tanda 1 · Reglas de plataforma

Va primero porque las otras cuatro la dan por sabida.

| Tema | Leé, en orden | Dónde cae allá |
| --- | --- | --- |
| Forma de trabajo y reglas del usuario | T10/03 entero → T13/06 §3 y §5 → T14/01 «Lo que enseñó la forma de trabajo» | `AGENTS.md`, tus reglas de trabajo y tus skills |
| Diseño para toda la app | T4 §2 → T5 §2 a §5 → T9 §2 (el idioma, sólo cuando es el otro) → T13/05 entero | `docs/rules/`, `tema.md`, `formatos.md`, los tableros «Criterio» y el texto de diseño de cada etapa |
| Fechas, números, plazos y mensajes | T13/01 §10 → T14/06 §1 (los números con la región del idioma) | `formatos.md` y las fichas que formatean |
| Arquitectura, revisiones y versiones | T9 §6 → T10/01 §4 y §5 → T13/06 §1, §2, §4 y §6 → T14/01 → T14/06 §7 → 02, «La prueba de quitar Formularios» | tu `backend.md`, tus ADR, los tests de arquitectura y el despliegue |
| El núcleo | T9 §4 (Mi perfil) y §5 (el menú sin saltos) → T14/02 entero | identidad, usuarios, roles, correo, Redis, salud y retención |

## Tanda 2 · Catálogos y formularios: de la E12 a la E17

| Tu etapa | Leé, en orden | Queda viejo |
| --- | --- | --- |
| E12 · Catálogos | T4 §3 (las opciones en un modal, importar, exportar legible) → T13/01 §3 (catálogos que mantiene un módulo) y §9 («Ordenar por…») → T14/06 §6 | — |
| E13 · Campos de los catálogos | T4 §3 («Los campos de un catálogo») | — |
| E14 · Formularios | T4 §4 (el catálogo de una pregunta, publicar en WhatsApp, los límites del Flow) → T5 §9 (zonas plegadas) → T10/01 §2 (vista oficial, el estado una vez, el nombre del Flow, el mensaje) → T13/01 §5 («sólo en la app») y §7 (la pregunta «Ubicación») → T13/03 §4 (los perfiles del Flow) → T14/06 §4 (publicación) | — |
| E15 · Respuestas | T5 §3, §6 y §7 (tablas, filtros, el detalle) → T13/01 §6 (el Excel en una hoja, el diálogo de exportar) → T14/06 §6 → 02, «La marca de la planilla» | T14/06 §9 dice que la marca no existe: ya existe |
| E16 · Datos de las personas | T4 §3 («Datos de las personas») → T9 §2 (el aviso de opciones por persona) | — |
| E17 · Ciclo de vida y copiar | T10/01 §2 («el estado, una vez») | — |
| Carpetas, sin etapa allá | T13/01 §4 (CP1-A a CP5-A) | T5 §11 «Carpetas», que sólo estaba decidido; proponé en qué etapa entra |

## Tanda 3 · Lo que se carga y lo que se decide: de la E18 a la E21

| Tu etapa | Leé, en orden | Queda viejo |
| --- | --- | --- |
| E18 · Fotos y documentos | T5 §6 (Foto y Documento salen de «Filtros») | — |
| E19 · Condiciones, cálculos y líneas | T12 entero (renglones) → T13/01 §1 (el motor para documentos: cálculos y redondeo) | T5 §11 «Renglones» y T10/01 §1: los reemplaza el 12 |
| E20 · Cargar desde la web | T5 §10 (dos anchos, mantener lo cargado) → T7 entero → T10/01 §3 → T13/01 §8 (elegir primero por un campo) → T14/06 §6 | T10/01 §3 lo daba en programación; T7 §6 |
| E21 · Reglas, aprobación, PDF y avisos | T4 §5 (adjuntar el PDF, confirmar antes de aprobar) → T5 §8 (reglas en pasos, historial, el mensaje con datos) → T9 §3 (reintentar el aviso a una persona) → T14/06 §2 y §3 (permisos, reglas y acciones) → 02, «La marca de la planilla» | — |

## Tanda 4 · Biblioteca, asistente, documentos e IA: de la E22 a la E25

| Tu etapa | Leé, en orden | Queda viejo |
| --- | --- | --- |
| E22 · Biblioteca | T13/01 §2 (la biblioteca de estándares, ADR 0029) → T13/01 §3 (los estándares y catálogos que aporta un módulo) | — |
| E23 · El asistente, con Casos | T13/03 §3 (los casos del asistente) → T14/05 entero (vale para todo lo que usa la IA) | — |
| E24 · Varios asistentes | 02, «Planeado y sin programar» (los agentes del asistente) | No hay nada programado |
| E25 · Documentos por WhatsApp y la IA que arma | T8 y T11, juntos (el 11 completa el 8) → T13/02 entero → T14/06 §5 (documentos recibidos) | T8 §6 y T11 §7 |

## Tanda 5 · Fuera del bloque: WhatsApp, ARCA e integraciones

| Tema | Leé, en orden | Dónde cae allá |
| --- | --- | --- |
| WhatsApp, el canal | T9 §6 («el agente de IA de Meta») → T13/03 §1 y §2 (la revisión y las etapas 1 a 5 de sus arreglos) → T14/03 entero (etapas 6 y 7) | tu canal de WhatsApp y la bandeja |
| ARCA | T6 → T13/04 → T14/04 | tu decisión 76 y el módulo `Integrations` |
| Tickets en Jira y otras integraciones | 02, «Planeado y sin programar» | tu decisión 76 |

## Qué saltear

- **Los prompts** de los traspasos 4 a 14: se usa el del README de este.
- **Las secciones de estado,** que reemplaza el 02: T6 §6, T7 §6, T8 §6, T9 §8 y §9, T11 §7, T12 §8, T10/02, T13/07 §1 a
  §3 y T14/07 §1, §2 y §4.
- **Lo decidido que después se programó distinto:** T5 §11 (renglones, ARCA y carpetas, sólo decididos), T10/01 §1
  (renglones a medias) y T10/01 §3 (elegir primero, en programación).
- **Las preguntas sueltas** de cada traspaso: están todas, sin repetir, en el [03](03-preguntas.md). Si una sección
  remite a «las preguntas del §N», buscala ahí.
- **«Dónde ver los diseños» del T10:** el lienzo provisorio es un Artifact de la cuenta de ArquitecturaBase, que otra cuenta
  no abre. Con tu 108 no hace falta: tus tableros salen de tu lienzo.
