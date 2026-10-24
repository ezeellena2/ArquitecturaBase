# Anexo F · Planes decididos sin construir

Traspaso 2 del 2026-10-23 · [índice del paquete](README.md)

Este anexo junta **todo lo planeado y decidido en ArquitecturaBase que todavía no se construyó**, para que el multitenant lo evalúe técnicamente desde el principio y no lo descubra a mitad de una etapa. No es código para copiar ni un plan de etapa: es el insumo para las specs del multitenant.

**Fuentes.** Estado commiteado, leído con `git show HEAD:<ruta>`:
- back `ArquitecturaBase` en `6023f7d7` (2026-10-23);
- front `ArquitecturaBaseFront` en `e133d93`;
- para «Qué cambia en multitenant», la copia local de `ArquitecturaBaseMutitenant` en `7d452748`. Los enlaces al multitenant apuntan a `main` en GitHub, que puede estar adelante o atrás de esa copia.

Los cambios sin commitear de los árboles (las correcciones de Catálogos E2 en curso) no se miraron.

## Cómo leerlo

Cada plan tiene las mismas secciones: problema y objetivo, qué se construye, decisiones tomadas, decisiones abiertas, diseño técnico, tableros, dependencias, cómo se cierra y qué cambia en multitenant.

| Marca | Qué significa |
| --- | --- |
| **Decidido** | Lo decidió el usuario, con fecha. No se reabre. |
| **Fijado al dibujar** | El plan no lo decía; se resolvió con la opción más simple y quedó confirmado al elegir el tablero. Tampoco se reabre. |
| **Abierto** | La fuente lo deja sin decidir o dice que lo decide el usuario. |
| **Propuesta del plan** | Está en la fuente como recomendación técnica; la confirma la spec. |
| **Propuesta del anexo** | No está en las fuentes. Es una sugerencia para evaluar, armada con lo que sí dicen. |
| **Para evaluar** | Análisis de este anexo con las piezas del multitenant. No lo decidió nadie. |
| **[sin verificar]** | No se pudo comprobar en las fuentes; no hay que darlo por cierto. |

Los nombres de clases, tablas o rutas son de ArquitecturaBase. El multitenant los traduce a sus piezas: por ejemplo, `BusinessForms` pasa a `Forms` y los puertos van en `Application/Interfaces/Integrations/StructuredData/`, según su [hoja de ruta](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/plans/2026-10-12-formularios-dinamicos-hoja-de-ruta.md#6-qué-tomar-de-arquitecturabase).

## 1. Reglas del usuario del 2026-10-23 que valen para todo

Son posteriores a los tableros de Casos (AS, 2026-10-21) y de Documentos (DOC, 2026-10-22) y **mandan sobre ellos**: así se aplicaron en el [review de E2](../../reviews/2026-10-23-code-review-catalogos-e2.md), puntos 35, 38 y 39. El detalle está en [04 · Reglas nuevas del usuario](04-reglas-del-usuario.md) y en el [visual-baseline del front](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md).

- **Idioma.** Lo que se crea en la web se escribe en el idioma elegido en el menú: es el original y el único obligatorio. La traducción es opcional y se carga cambiando el idioma del menú y editando lo mismo; no hay selector de idioma en las pantallas. Con el menú en el otro idioma se muestra la traducción si existe y, si no, el original como referencia, sin copiarlo.
- **Botones.** Un botón está habilitado o deshabilitado, sin textos al lado ni debajo, y no cambia de lugar ni de forma con cada acción. Si el motivo no es obvio, va como tooltip. No se muestran «Cambios sin guardar» ni números de revisión; se conserva el aviso al salir con cambios. Las acciones van al borde derecho del encabezado, a la altura del título. Se valida al intentar guardar.
- **Producto genérico.** Nada específico de un cliente. OCHA, Macrosistemas y La Clementina aparecen en las fuentes solo como ejemplo del primer caso real.

**Dónde chocan con lo elegido.** Se corrige al construir, sin volver a preguntar. Es la aplicación directa de la regla; ninguno de estos tableros tiene una decisión propia que lo diga.

| Tablero o fuente | Qué dice | Cómo queda con la regla |
| --- | --- | --- |
| AS2 | «Un texto exacto (español y, opcional, inglés)» | Se escribe en el idioma del menú, con traducción opcional. A la persona le llega en su idioma si está traducido y, si no, en el original, como los formularios. |
| AS3 | El panel Probar «avisa si hay cambios sin guardar» | Sin ese aviso visible. Probar usa lo guardado; si hace falta explicar por qué un botón está apagado, va como tooltip. |
| AS1-A | «Un solo Guardar … con revisión y 409» | La revisión y el 409 siguen en el servidor; la pantalla no muestra el número. |
| E2 implementado y la hoja de ruta de catálogos | Nombres de catálogos, campos y datos de las personas con «es obligatorio, en opcional», o bilingües | Idioma del menú como original (punto 35). Vale también para todo catálogo, campo o dato que traigan las fases siguientes y los orígenes externos. |
| Hoja de ruta de catálogos | «Primer cliente: OCHA S.A.», «conector para Macrosistemas» | Se lee como «primer sistema externo conocido». El mecanismo es la plantilla de origen como datos, para cualquier sistema. |

## 2. Mapa: estado, orden y dependencias

| Plan | Estado al 2026-10-23 | Tableros | Depende de | Falta antes de programar |
| --- | --- | --- | --- | --- |
| [F1 · Casos del asistente](#f1--casos-del-asistente) | Decidido el 2026-10-21; [ADR 0021](../../decisions/0021-respuestas-redactadas-por-el-asistente.md) aceptado; sin empezar | AS1-A y AS2–AS6 elegidos | Etapa 5 (motor y asistente); arreglo de la espera (hecho el 2026-10-21); deuda de Información del negocio | No hay spec técnica en el repo: el plan pasa de los tableros a construir |
| [F2 · Agentes](#f2--agentes-la-tercera-etapa-del-asistente) | Alcance descripto; sin decisiones de detalle | Ninguno | Casos (el agente principal) | Decisiones, tableros y spec |
| [F3 · Documentos por WhatsApp](#f3--documentos-por-whatsapp) | Decidido el 2026-10-22; [ADR 0022](../../decisions/0022-lectura-de-documentos-con-ia.md) aceptado; sin empezar | DOC1–DOC4, DOC5-A, DOC6 y DOC7-A elegidos | Catálogos E2 cerrado (cruce del CUIT); ráfaga, tope de 3 acciones y derivar de Casos | Evaluación de impacto y spec técnica |
| [F4 · Catálogos después de E2](#f4--catálogos-después-de-e2) | Hoja de ruta: «no se programan hasta que el usuario las defina» | Ninguno | E1 y E2 cerrados; intercambio cifrado con Meta (fase 3); conexión a bases (fases 5 y 6) | Definición del usuario, tableros y un ADR para bases externas |
| [F5 · Conexión a bases de clientes y tableros cruzados](#f5--conexión-a-bases-de-clientes-y-tableros-cruzados) | Pendiente de decisión del usuario desde el 2026-10-22 | Ninguno | Red virtual en Azure; ADR que amplíe el 0019 | La decisión |
| [F6 · Integraciones, partes 2 y 3](#f6--integraciones-partes-2-y-3) | Investigación con fuentes del 2026-10-22; 13 decisiones pendientes | Ninguno | Documentos (la biblioteca) y el motor de acciones de la Etapa 4 | Las 13 decisiones, cuatro ADR y los tableros |
| [F7 · Otros pendientes](#f7--otros-pendientes) | Cada uno con su estado | Solo los que se indican | — | Tablero y decisión de cada uno |
| [F8 · Verificación y producción](#f8--verificación-y-producción) | 39 casillas abiertas; piloto único pendiente | — | Review de E2 corregido | — |

**Orden entre planes.**
- El plan de Casos pone a Documentos como «etapa siguiente» y a Agentes como tercera. El plan de Documentos, posterior, dice que su orden frente a Casos «lo define el usuario». **Abierto.** La recomendación de hacer Casos primero, porque Documentos usa su ráfaga y su tope de acciones, la dio el chat guía y no está escrita en el repo **[sin verificar en el repo]**.
- Documentos va **después de Catálogos E2**, porque el cruce con catálogos usa sus campos extra. E2 está implementado y se corrige con los 39 puntos del [review](../../reviews/2026-10-23-code-review-catalogos-e2.md); después viene el piloto único con Meta.
- Integraciones: el usuario dejó sus decisiones para después de la parte 1, que es Documentos ([índice de planes](../README.md)).

```
Catálogos E1/E2 (hechos, en corrección) ──► piloto único con Meta
        │
        └──► Documentos ◄── Casos del asistente (ráfaga 4 s / 15 s, hasta 3 acciones,
                │                 tipo de entrada «documento», derivar)
                │                     │
                │                     └──► Agentes (más filas de agente, ruteo y pases)
                └──► Integraciones parte 2 (biblioteca de formularios conectados)

Conexión a bases de clientes ──► Catálogos fases 5 y 6, tableros cruzados
Intercambio cifrado con Meta ──► búsqueda en vivo, listas dependientes y largas
Plantillas de Meta ──► API pública (mandar fuera de las 24 h) y avisos fuera de la ventana
```

---

## F1 · Casos del asistente

Fuentes: [plan](../2026-10-21-casos-del-asistente.md), [ADR 0021](../../decisions/0021-respuestas-redactadas-por-el-asistente.md), [tableros elegidos](../../../../ArquitecturaBaseFront/docs/design/asistente-casos.md), [ADR 0020](../../decisions/0020-motor-unico-de-ia-con-capacidades.md) y lo implementado de la [Etapa 5](../../features/whatsapp.md#ofrecimientos-automáticos--etapa-5).

### Problema y objetivo

Hoy el asistente de WhatsApp, implementado en la Etapa 5, hace bien una cosa: ofrecer formularios. Lo demás falla:
- **Respuestas fijas en el código.** El menú vive en `WhatsAppAssistantAccessPolicy` y las reglas finas en las descripciones de cada capacidad; agregar un tipo de respuesta exige programar.
- **Silencio ante cualquier otra cosa.** Un saludo, «¿qué hora es?» o «¿hacen envíos?» terminan en «no responder»: las consultas y la charla «quedan para atención», los acuses son textos fijos y «no se envía texto libre del modelo».
- **Sin contexto.** El modelo ve el último mensaje, sin los anteriores; solo recibe las ofertas de las últimas 24 horas (hasta 20).
- **Ráfagas.** Se interpreta solo el último mensaje y el más nuevo cancela el pedido anterior: con varios mensajes seguidos se pierde lo dicho antes.
- **Ciego a los archivos.** Las fotos y los documentos no le llegan.
- **Costo.** Unas 3,6 consultas al modelo por turno.

**Objetivo:** que responda con sentido a cualquier mensaje, según cada negocio, sin respuestas escritas en el código. Los formularios siguen siendo el fuerte. Meta de costo: 1 o 2 consultas por turno.

### Qué se construye

Una pantalla **Asistente** con la configuración guardada en la base. El plan dice que en el multitenant va una por organización. Se guarda con revisión y 409, como el resto de la configuración interna ([ADR 0018](../../decisions/0018-robustez-proporcional-al-efecto.md)).

| Pieza | Qué guarda |
| --- | --- |
| **Perfil** | Nombre, presentación, tono, qué hace y qué no, qué hacer cuando no sabe y el mensaje fijo al derivar. |
| **Conocimiento** | La Información del negocio que ya existe (siete campos y `details`, permisos `ai.business.*`, pantalla `/configuracion/negocio`), que hoy usa solo Inicio. Se ve de solo lectura. Antes de usarla se corrige su deuda: hoy presenta preguntas sin respuesta y textos anteriores como hechos. |
| **Casos** | Por caso: cuándo (texto libre), tipo de entrada (cualquiera, texto o audio; documentos en la etapa siguiente), qué hacer, orden y si está activo. |
| **Límites** | Temas que no responde, lo que no puede prometer, largo máximo de una respuesta, tope propio de respuestas por conversación y día, y las acciones que puede usar el agente. |

**El menú cerrado de «qué hacer»** es lo único que vive en el código:
1. mandar el formulario X;
2. responder con un texto exacto, escrito por el negocio;
3. **responder con la información del negocio:** la IA redacta, con los límites del ADR 0021;
4. preguntar con botones (hasta 3, cada uno con su destino);
5. derivar a una persona, con un motivo;
6. no responder.

Un caso nuevo es configuración. Una acción nueva es una opción más del menú, con su capacidad, su texto y sus casos de evaluación.

**Lo que no es un caso propio:**
- **«Cualquier otra cosa»** siempre existe, va al final y no se borra, no se pausa ni se mueve. Ahí caen el saludo, la hora o una consulta general. Ejemplo del plan: «Respondé breve con la información del negocio; si no la tenés, decí que lo pasás a una persona y derivá».
- **Los formularios** conservan su «Cuándo lo ofrece», guardado en el formulario. La lista de casos los muestra en un bloque fijo como «Mandar formulario», sin duplicarlos, y se editan en el formulario.

**Probar el asistente:** se escribe un mensaje y se ve qué caso elige, por qué y qué respondería, con lo guardado y sin mandar nada. Un mensaje por renglón; varios renglones son una ráfaga.

**En el chat:**
- cada acción sale como «Asistente», con su porqué: «Por qué respondió esto» (el caso, lo que usó y el texto, guardado tal como salió) o «Por qué lo mandó» para un formulario;
- derivar manda el mensaje fijo del perfil, pasa la conversación a Atención manual y la marca «Necesita una persona», con el motivo, en la cabecera, en una franja y en la lista de chats, con el filtro «Necesita una persona (N)». La marca se quita cuando una persona responde;
- en Atención manual, la respuesta redactada se le ofrece al operador como borrador, junto a la sugerencia de formulario de hoy (H2);
- sin avisos fuera del sistema.

### Decisiones tomadas

Todas del **2026-10-21**. Las de ráfagas y acciones se tomaron con lo recomendado y se confirmaron al elegir los tableros, ese mismo día.

1. **El asistente redacta respuestas con la información del negocio, con límites.** Lo fija el [ADR 0021](../../decisions/0021-respuestas-redactadas-por-el-asistente.md), aceptado el 2026-10-21, que cambia la regla «el modelo nunca escribe lo que sale»:
   - **fuentes:** solo lo que el negocio cargó (perfil, conocimiento y casos), el historial de esa conversación y los datos del sistema que el canal le da, como la hora y la zona del negocio. Lo leído es dato, nunca instrucción;
   - **forma:** corta, con el largo máximo de los límites y en el idioma de la conversación;
   - **contenido:** sin enlaces, teléfonos, correos, precios ni compromisos que no estén en el conocimiento cargado;
   - **si no sabe,** deriva con el acuse fijo del perfil y no inventa;
   - **control del servidor:** la respuesta llega como propuesta tipada de la capacidad dueña. WhatsApp la valida (largo, idioma, enlaces y datos de contacto) **antes de encolarla**; si no pasa, no sale y deriva. El texto nunca se manda directo desde el modelo;
   - **trazabilidad:** se guardan el texto, el caso y lo que usó, visibles en «Por qué respondió esto». Los logs no llevan el contenido;
   - **configurable:** cada negocio puede quedarse solo con textos exactos, apagando «Responder con la información del negocio» en las acciones permitidas;
   - **calificación:** un perfil de modelo responde texto solo si está calificado con casos que incluyan consultas con y sin información cargada, fuera de tema, saludo y hora, derivación e inyección de instrucciones dentro del mensaje o del conocimiento;
   - **respuestas y ofrecimientos van por separado:** las respuestas tienen su propio tope por conversación y día y no consumen el cupo de ofrecimientos.
2. **Historial:** los mensajes de las últimas 24 horas **de esa misma conversación, nunca de otra**, con un tope (el plan da como ejemplo los últimos 10, contando texto y transcripciones), siempre como datos. Lleva un test con dos personas escribiendo a la vez.
3. **Historial con lo hecho:** suma lo que el asistente ya hizo y cómo quedó, por ejemplo «ayer se mandó Registrar visita y lo completó» o «hoy se mandó Pedir repuesto, sin abrir». Así no repite lo de ayer y entiende cuándo le piden uno nuevo.
4. **Derivar:** aviso en el chat y en la lista, más el mensaje fijo a la persona. Sin correo ni avisos fuera del sistema.
5. **Dónde vive la configuración:** en nuestra base, por organización. No va en el panel del proveedor de IA: quedaría atada a una cuenta y a un proveedor, el negocio no podría editarla, versionarla ni probarla, y no ahorra tokens, porque el proveedor la vuelve a mandar en cada llamada.
6. **Genérico:** ninguna respuesta queda escrita en el código.
7. **Fotos y documentos** van en una etapa aparte, la siguiente ([F3](#f3--documentos-por-whatsapp)).
8. **Ráfagas:** el turno espera **4 s sin mensajes nuevos, con un tope de 15 s**, y procesa la ráfaga entera. Cancelar lo anterior solo descarta un borrador, porque el turno nuevo incluye todo el historial.
9. **Varias acciones por respuesta:** hasta **3, en orden**, cada una validada por separado; por ejemplo, un texto y un formulario, o dos formularios. El tope diario de ofrecimientos sigue valiendo: hoy son 2 por conversación y día calendario, en la zona del negocio.
10. **El asistente es un agente:** se modela desde ya como el **agente principal**, una fila con su perfil, conocimiento, casos, límites y acciones permitidas, y no como una configuración única. La etapa Agentes suma filas y ruteo sin rehacer nada.
11. **Tableros:** AS1-A y AS2–AS6 de la versión 98 del lienzo. Con eso se confirman las ráfagas, las 3 acciones por respuesta y el borrador para el operador en Atención manual (AS4).

### Fijado al dibujar (2026-10-21)

- La pantalla va en el panel de WhatsApp, junto a Chat, y pide el permiso «Configurar el asistente». Sin él, la entrada no aparece y por la dirección dice «No tenés permiso».
- El conocimiento se ve ahí y se edita en Información del negocio, con «Administrar información del negocio».
- Un solo Guardar para toda la pantalla, con revisión y 409. Probar usa lo guardado.
- Los formularios van en un bloque fijo entre los casos propios y «Cualquier otra cosa», y no se reordenan.
- Derivar pasa la conversación a Atención manual, y la marca se quita con la primera respuesta de una persona.
- Sin perfil, «Cualquier otra cosa» empieza en «No responder»: hasta configurarlo, el asistente hace lo de hoy. Para un negocio que empieza, la pantalla abre en Perfil con ese aviso.
- El sistema vuelve a revisar el largo máximo (lo que se pasa no sale y deriva), y el tope del día también deriva.
- Las acciones que puede usar son del agente, para que cada agente tenga las suyas.
- En el celular, las pestañas se desplazan de costado y Guardar queda abajo.

### Decisiones abiertas

- **Forma técnica de la salida de texto:** una capacidad de propuesta de texto o `assistant_answer` adaptado. El ADR 0021 lo deja para la implementación, «con la opción más simple que cumpla los límites». Hoy el motor admite como salidas finales `assistant_proposal` y `assistant_answer`, y el canal de WhatsApp no habilita la segunda.
- **Tope del historial:** «por ejemplo, los últimos 10» es un ejemplo; el valor lo fija la spec.
- **Valores por defecto de los límites:** el largo máximo y el tope diario de respuestas no tienen valor en las fuentes.
- **Código del permiso «Configurar el asistente»:** el tablero lo nombra; el código sigue la guía de permiso nuevo.
- **Casos de calificación concretos:** el plan da las categorías; los casos se escriben.
- **Orden frente a Documentos:** abierto (ver el [mapa](#2-mapa-estado-orden-y-dependencias)).
- **Cómo se persiste:** el plan fija «una fila» por agente y un solo Guardar con revisión; si los casos son tablas hijas o un documento con la misma revisión no está decidido.
- **Si Probar consume cuotas o presupuesto de IA:** las fuentes no lo dicen.
- **Relación con el plan de [sugerencias de texto con revisión humana](../2026-10-03-sugerencias-ia-whatsapp.md):** AS4 eligió el borrador para el operador en Atención manual «con el mismo motor», como pedía el plan de Casos. El plan de sugerencias sigue en el índice con sus pendientes propios: permiso de uso (`whatsapp.ai.use` u otro), presupuesto del canal y casos de evaluación. Falta unificarlos.

### Diseño técnico propuesto

**Lo que fija la fuente.**
- Casos y acciones son **capacidades** del motor único, de lectura y de propuesta; el sistema ejecuta y vuelve a validar ([ADR 0020](../../decisions/0020-motor-unico-de-ia-con-capacidades.md)).
- Perfil, casos, formularios candidatos e historial van **precargados al principio del pedido, en un orden estable**, para aprovechar la caché del proveedor.
- El canal de WhatsApp habilita la propuesta de respuesta, con su **validador** (largo, idioma, enlaces y datos de contacto), su forma de verse en el chat y su **desenlace en la cola de salida**.
- Desenlaces nuevos: texto exacto, texto redactado y derivar.
- La simulación de Probar no manda nada.
- Al cerrar, la regla de [WhatsApp](../../features/whatsapp.md#ofrecimientos-automáticos--etapa-5) pasa de «no se envía texto libre del modelo» a «solo texto redactado con estos límites, o fijo».

**Modelo.** Propuesta del anexo, armada sobre lo que fijan el plan y los tableros:

| Pieza | Contenido | De dónde sale |
| --- | --- | --- |
| Agente | Nombre, «es el principal», perfil, límites, acciones permitidas y revisión. En esta etapa, uno por organización | Plan: «una fila con su perfil, conocimiento, casos, límites y acciones permitidas» |
| Caso | Agente, cuándo, tipo de entrada, acción con sus parámetros, orden y activo. «Cualquier otra cosa» es un caso de sistema | Plan y AS2 |
| Parámetros de cada acción | Formulario (por id); texto exacto (original y traducción opcional); instrucciones para redactar; pregunta con hasta 3 botones y el destino de cada uno; motivo de derivación | AS2 |
| Resultado de cada acción del turno | Caso, acción, lo que usó, texto tal como salió y estado de la salida | ADR 0021, punto 6, y AS4 |
| Marca de la conversación | «Necesita una persona», con motivo y desde cuándo | AS4 |
| Cupo de respuestas | Por conversación y día del negocio, aparte del cupo de 2 ofrecimientos | ADR 0021, consecuencias |

**Un turno, de punta a punta:**
1. Llega un mensaje de texto o un audio ya transcripto, de una cuenta vinculada y activa, en modo Asistente y dentro de las 24 horas (como hoy, AS6).
2. Espera la ráfaga: 4 s sin mensajes nuevos, con un tope de 15 s. Un mensaje nuevo descarta el borrador anterior.
3. Arma el pedido, en orden estable: perfil, casos, formularios candidatos (los publicados, activos y completos que esa cuenta puede completar, con su «Cuándo lo ofrece»), historial de 24 h de esa conversación y lo que ya hizo.
4. El modelo elige el caso y propone hasta 3 acciones, en orden.
5. El sistema valida cada acción por separado: permisos, cupo de ofrecimientos, cupo de respuestas, y largo, idioma, enlaces y datos de contacto de un texto. Un texto que no pasa no sale y deriva.
6. Encola en la salida durable, que hoy es una única FIFO, y guarda el porqué de cada acción.
7. Si deriva: mensaje fijo del perfil, Atención manual y la marca «Necesita una persona».
8. En Atención manual, la respuesta redactada queda como borrador para el operador.

**Calificación antes de habilitar el perfil:** consultas con y sin información, fuera de tema, saludo, hora, derivación e inyección de instrucciones.

### Tableros

Elegidos el 2026-10-21 de la versión 98 del lienzo. Detalle completo en el [diseño del front](../../../../ArquitecturaBaseFront/docs/design/asistente-casos.md#los-tableros). AS1-B (página guiada) y AS1-C (lista con detalle al costado) quedan en el lienzo como las alternativas que no se eligieron.

| Tablero | Qué define |
| --- | --- |
| [AS1-A · Pestañas](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as1-a-pestanas.png) | «Asistente» en el panel de WhatsApp. Cabecera «Agente principal · Atiende todas las conversaciones en modo Asistente» y cuatro pestañas: Perfil, Conocimiento, Casos (con su cantidad) y Límites; se entra por Casos. «Probar» abre un panel al costado. Un solo Guardar. Cuando haya varios agentes, cada uno abre esta misma pantalla desde una lista. |
| [AS2 · Los casos](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as2-casos.png) | Los casos en el orden en que se eligen: los propios (se arrastran y se pausan), el bloque fijo de formularios y «Cualquier otra cosa». Cada caso dice cuándo, el tipo de entrada (en una lista que deja lugar para fotos y documentos) y qué hace. El editor se abre al costado, con Listo, y se guarda con la pantalla. |
| [AS3 · Probar el asistente](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as3-probar.png) | Prueba como si escribiera una cuenta vinculada. Muestra las acciones en orden, hasta 3, cada una con su caso, por qué, lo que usó y cómo la vería la persona, y «No se mandó nada». Ejemplos: «hola», «¿qué hora es?», «¿hacen envíos a Córdoba?» con y sin la información, «se rompió la heladera en la sucursal Centro», «quiero hablar con alguien» y una ráfaga de tres mensajes que responde con un texto y dos formularios. |
| [AS4 · El chat](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as4-chat.png) | El porqué debajo de cada acción, derivar y la marca «Necesita una persona», el texto exacto, los botones, «Sin respuesta del asistente», el tope del día (deriva) y, en Atención manual, la respuesta redactada como borrador para el operador. |
| [AS5 · Límites y conocimiento](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as5-limites-y-conocimiento.png) | Las reglas fijas de toda respuesta redactada, los límites configurables y las acciones permitidas. El conocimiento, sección por sección con su estado: «Cargado», «Sin cargar» o «Texto anterior, sin revisar». Las preguntas sin respuesta y los textos anteriores sin revisar no se usan. |
| [AS6 · El circuito](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as6-circuito.png) | Los ejemplos en el celular y en el chat del operador, con lo que hacen la persona, el sistema y la IA, más la ráfaga con tres acciones. |

### Dependencias

- **Etapa 5 implementada:** motor único con canales, ofrecimientos, aclaraciones, audio, sugerencias y Formularios que faltan.
- **Prerrequisitos del plan:** la tanda del review de dar de baja y el arreglo de la espera del asistente. La espera se arregló el 2026-10-21 ([WhatsApp](../../features/whatsapp.md#espera-del-asistente-y-avisos-locales--2026-10-21)).
- **Deuda de `business.information`:** hoy incluye preguntas sin respuesta y textos anteriores como si fueran hechos ([índice](../README.md), deuda del backend).
- **La usan después:** Documentos (el tipo de entrada «documento», la ráfaga, las 3 acciones y derivar) y Agentes (el agente principal).

### Cómo se cierra

1. Checks por corte y al cerrar, según [validar un cambio](../../guides/validar-un-cambio.md), con TDD de configuración, límites, historial y desenlaces.
2. Integración con Docker y front en los dos idiomas, a 390 y 320 px.
3. Calificación aprobada antes de habilitar el perfil.
4. Piloto real con Meta:
   - «hola» y «¿qué hora es?» reciben una respuesta con sentido;
   - una consulta con la información cargada se responde, y una sin información deriva;
   - «se rompió la heladera» sigue mandando el formulario;
   - «quiero hablar con alguien» deriva y aparece en la lista.

### Qué cambia en multitenant

Fuentes del multitenant: [resumen del asistente en WhatsApp](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/plans/formularios/asistente.md), [ADR 0038](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/decisions/0038-whatsapp-de-atencion.md), [ADR 0048](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/decisions/0048-asistente.md), [ADR 0051](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/decisions/0051-precision-tecnica-de-costos-ia.md) y la [hoja de ruta](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/plans/2026-10-12-formularios-dinamicos-hoja-de-ruta.md).

- **Choca con lo que el multitenant tiene escrito.** Su resumen del asistente deja **fuera** «responder consultas generales» y dice que «lo que no es un formulario no se contesta: queda sin leer para un operador». Casos cambia exactamente eso. **Para evaluar:** el multitenant necesita su equivalente del ADR 0021 antes de planificar el asistente, y actualizar ese resumen.
- **Prerrequisitos que el multitenant ya marcó:** el asistente con acciones con confirmación (ADR 0048, D8) y la IA en WhatsApp, que la ADR 0038 deja para después. Casos se suma a esos dos.
- **Por organización:** agente, casos, límites, la marca «Necesita una persona» y el resultado de cada acción son datos privados de la organización (`ITenantOwned`, RLS). Los workers entran con el alcance de la organización. Lo mismo vale para el historial: «nunca de otra conversación» también es «nunca de otra organización», con su test de aislamiento.
- **Conocimiento:** no encontré en los documentos del multitenant un equivalente de «Información del negocio» (búsqueda sin resultados en su `docs/`). **Para evaluar:** definir dónde vive el conocimiento de cada organización y quién lo edita, antes de construir la pestaña Conocimiento.
- **Costos:** el costo del modelo y de la transcripción entra en el **tope mensual en dólares por organización** (ADR 0048, D10, y ADR 0051). Probar también llama al modelo. **Para evaluar:** con el tope agotado, el resumen del multitenant ya prevé «asistente no disponible (sigue en atención manual)»; Casos podría seguir esa misma regla.
- **Credencial de IA:** una organización puede usar su propia clave o la de la plataforma (ADR 0048, D23). La configuración del asistente sigue en la base y nunca en el panel del proveedor, con cualquiera de las dos claves.
- **Números de WhatsApp:** el asistente actúa en el número de la organización (E11) y, por la decisión 29 del multitenant, solo en la bandeja Internos. El número de la plataforma no manda formularios (decisión 4).
- **Personas B2C:** hoy quedan afuera por la decisión 29. En la bandeja Clientes ya existe el bot de la E11: respuestas automáticas y un menú de hasta 10 opciones que pasa a un equipo o contesta un texto. **Para evaluar:** si los casos del asistente reemplazan, extienden o conviven con ese bot para los clientes. Los límites del ADR 0021 (sin precios ni compromisos que no estén cargados) pesan más con clientes que con empleados.
- **Permisos:** «Configurar el asistente» es un permiso de organización en el catálogo del núcleo. En el multitenant, pasar a modo Asistente ya pide «Administrar WhatsApp» (decisión 28).
- **Retención de «Por qué respondió esto»:** en ArquitecturaBase el cuerpo de los mensajes se vacía a los 90 días. En el multitenant manda la D4 de la E11: el texto de los chats se guarda mientras exista el contacto. **Para evaluar:** que el texto redactado y su porqué sigan la misma regla que el mensaje.

---

## F2 · Agentes, la tercera etapa del asistente

Fuente: [plan de Casos](../2026-10-21-casos-del-asistente.md#tercera-etapa-agentes).

### Problema y objetivo

Un solo asistente con muchos casos empieza a confundirse cuando el público o el propósito cambian mucho: empleados frente a clientes, o números distintos. Un **agente** es un asistente especializado: perfil e instrucciones propias, solo el conocimiento que le corresponde, sus casos, sus acciones permitidas y su tono. Ejemplos del plan: ventas (catálogo, precios y pedidos), soporte (reclamos y el formulario de visita) y personal interno (licencias y recibos).

### Qué se construye

1. **Crear agentes** desde un menú «Agentes», con su prompt de «qué hace», casos, conocimiento, acciones y límites, y «Probar el agente».
2. **Ruteo:** a qué agente va una conversación, de una de tres formas:
   - por número de WhatsApp o canal;
   - por tipo de contacto (empleados o clientes, según su cuenta o su rol);
   - por un clasificador que lee el primer mensaje y elige. El clasificador es lo último y lleva su propia calificación.
3. **Pases:** un agente pasa la conversación a otro o a una persona, con el motivo visible en el chat.
4. **Un solo motor:** cada agente es una configuración de canal con su conjunto de capacidades, sin otro motor ([ADR 0020](../../decisions/0020-motor-unico-de-ia-con-capacidades.md)).

### Decisiones tomadas

- **2026-10-21:** el asistente se modela desde Casos como el agente principal, una fila más, para que esta etapa sume filas y ruteo sin rehacer nada. Las acciones permitidas ya son del agente.
- El plan de Casos la ubica como tercera etapa, después de Documentos. El orden entre Casos y Documentos sigue abierto ([mapa](#2-mapa-estado-orden-y-dependencias)).

### Decisiones abiertas

Todo lo demás: qué formas de ruteo entran primero, cómo se ve el pase, cómo se reparte el conocimiento entre agentes y qué pasa con una conversación ya empezada cuando cambia el ruteo. El plan dice cuándo conviene («mientras tanto, alcanza un agente con casos»), no cuándo se hace.

### Diseño técnico propuesto

Solo lo que dice el plan: más filas de agente, una regla de ruteo y un registro del pase con motivo. La [hoja de ruta de catálogos](../2026-10-21-catalogos-con-origenes-externos.md#conectividad-el-servidor-del-cliente-o-azure) ubica en esta etapa el uso de **MCP** para que un agente consulte datos en vivo, como el stock o el saldo.

### Tableros, dependencias y cierre

Sin tableros ni criterio de cierre. Depende de Casos. El cierre seguiría el de Casos, con la calificación propia del clasificador.

### Qué cambia en multitenant

- **Para evaluar:** las dos primeras formas de ruteo coinciden con piezas que el multitenant ya tiene. En la ADR 0038, cada número de una organización dice a quién atiende (toda la organización o una empresa), y el contacto se reconoce por la cuenta: un miembro activo va a Internos y los demás a Clientes. Rutear por número y por tipo de contacto puede apoyarse en eso sin piezas nuevas.
- **Para evaluar:** si un agente puede pertenecer a una empresa de la organización, como los números.
- Los agentes son datos privados de la organización, y su costo entra en el tope mensual de IA de esa organización.

---

## F3 · Documentos por WhatsApp

Fuentes: [plan](../2026-10-22-documentos-por-whatsapp.md), [ADR 0022](../../decisions/0022-lectura-de-documentos-con-ia.md), [tableros elegidos](../../../../ArquitecturaBaseFront/docs/design/documentos.md) y la [maqueta viva](https://claude.ai/artifact/CM56W2Yc4VVzwMmxCKFpdP), con el circuito DOC en «Recorridos».

### Problema y objetivo

Hoy, cuando alguien manda por WhatsApp una foto o un PDF, se guarda como «archivo» sin contenido y la IA no lo ve. El producto tiene que poder recibir **cualquier tipo de documento** (comprobantes de transferencia, facturas, recibos, remitos, liquidaciones, DTE y constancias), reconocer qué es, sacar sus datos y proponer el formulario que corresponde ya completo, para que la persona lo valide. **Tiene que ser genérico:** ningún tipo de documento queda escrito en el código.

Es la etapa «fotos y documentos» que el usuario marcó como **prioridad**, y la **parte 1 de tres proyectos**: la parte 2 conecta formularios con sistemas externos y la parte 3 deja que otros sistemas reciban nuestros datos ([F6](#f6--integraciones-partes-2-y-3)).

### Qué se construye

**1. La biblioteca de formularios estándar.**
- El producto trae formularios estándar para los documentos comunes, como **datos de inicio** de cada versión, no como código. En una instalación de una empresa aparecen en la pantalla «Biblioteca»; en el multitenant, la misma biblioteca la comparten todas las organizaciones.
- Cada estándar trae sus preguntas con sus tipos, **«Cómo reconocerlo»** (la descripción para la IA), sus **validaciones fijas**, que no dependen de la IA, y una pregunta de archivo para el documento original.
- **«Agregar desde la biblioteca»** crea un formulario propio, editable como cualquier otro (nombre, preguntas, reglas y aprobaciones). Recuerda de qué estándar viene, con qué versión, y qué pregunta corresponde a cada dato del estándar.
- Un formulario propio también puede activar la lectura de documentos escribiendo su «Cómo reconocerlo». Usa el mismo mecanismo.
- **Primer estándar: Comprobante de transferencia.**
  - Preguntas: banco o billetera de origen y de destino; CBU, CVU o alias de origen y de destino; titulares y CUIT/CUIL; monto y moneda; fecha y hora; número de operación; concepto; el comprobante, como archivo.
  - Validaciones fijas: el CBU y el CVU tienen dígitos verificadores, y sus primeros dígitos dicen el banco o la billetera, según la tabla de entidades del BCRA, que se carga como datos; el CUIT y el CUIL tienen dígito verificador; montos y fechas en formato argentino.
- **Estándares siguientes,** en etapas posteriores: factura (con el QR de AFIP), nota de crédito o débito, recibo, remito, ticket de gasto, orden de compra, presupuesto, cheque o eCheq, constancias, liquidaciones, carta de porte y DTE.

**2. Qué puede llegar.** Se leen **solo los mensajes de personas con cuenta vinculada y activa** y con el permiso de cargar respuestas. Lo que manda un número desconocido queda en el chat para un operador, sin leerse.

| Lo que llega | Cómo se lee | Qué completa |
| --- | --- | --- |
| Texto | Como hoy: lo interpreta el asistente | Propone el formulario prellenado |
| Audio | Como hoy: se transcribe y se trata como texto | Igual |
| Foto o captura (JPEG, PNG) | La lee la IA | Tipo de documento y datos; propone el formulario con la foto adjunta |
| PDF | La IA lee todas sus páginas, con un tope | Igual que la foto |
| Word (`.docx`) | El servidor extrae el texto y las tablas, y la IA los lee | Igual, con el archivo adjunto |
| Excel (`.xlsx`) | El servidor lee las hojas, con los topes de la importación de catálogos, y la IA las lee | Igual. Una planilla con varias filas del mismo tipo propone hasta 3 cargas por mensaje (el tope de acciones); las filas que pasan el tope quedan propuestas en la bandeja |
| Ubicación | Sin IA: llega con latitud, longitud y, a veces, nombre y dirección | La pregunta de ubicación o dirección del formulario abierto, elegida en el editor |
| Contacto (tarjeta) | Sin IA: llega con nombre y teléfonos | Las preguntas de nombre y teléfono, elegidas en el editor |
| Video o sticker | No se lee | — |

- Los formatos viejos (`.doc`, `.xls`), los archivos con macros y los protegidos con contraseña no se leen: se pide un PDF o una versión sin macros ni contraseña.
- El tipo se verifica **por el contenido**, nunca por la extensión, como los archivos de la [etapa 2B](../../features/formularios.md#fotos-y-documentos--implementación-local-2b-2026-10-15).
- Topes de tamaño, páginas, hojas y celdas, contando también las celdas vacías con formato que encontró el [review de catálogos E1](../../reviews/2026-10-22-code-review-catalogos-e1.md).
- Una foto con un audio que la explica («es el pago de Pérez») se lee junta solo con la espera de la ráfaga de Casos. Sin eso, cada mensaje se trata por separado.

**3. El circuito.**
1. **Recibir:** el archivo se descarga y se guarda como **copia privada**, como los audios. Se usa también el texto que venga junto, y un PDF de varias páginas se lee entero.
2. **Clasificar** entre los formularios con lectura de documentos que **esa persona puede completar**. No hay permisos por formulario: son todos los publicados, si su cuenta tiene el permiso de cargar respuestas, como en el envío desde el chat.
   - **Seguro:** propone ese formulario.
   - **Duda:** pregunta «¿Qué es este documento?» con botones para 2 o 3 formularios posibles; con dos, el tercero es «Otro».
   - **No lo reconoce o no hay formulario:** ofrece la lista que esa persona puede completar, más «Otro». «Otro» lo deja en la bandeja «Sin clasificar» y lo anota en Formularios que faltan.
3. **Extraer** los datos según las preguntas del formulario elegido, con su nivel de seguridad por dato.
4. **Validar sin IA:** las validaciones fijas, el cruce con catálogos (el CUIT en Proveedores o Clientes, como campo extra de E2) y los duplicados (el mismo archivo, o la misma operación, monto y fecha).
5. **Proponer** el formulario prellenado por WhatsApp, **con el documento adjunto** en su pregunta de archivo. Un dato que no se pudo leer queda vacío, y uno dudoso, marcado.
6. **Confirmar o corregir** lo hace la persona. **En esta etapa, la IA nunca carga sola.**
7. **Organizar:** queda como una respuesta más, con sus reglas, aprobación y PDF.

**4. Los casos a contemplar.**

| Situación | Qué hace |
| --- | --- |
| Foto movida, oscura o cortada | No adivina: pide otra, diciendo qué dato falta |
| PDF con contraseña | No lo abre ni pide la contraseña: pide uno sin contraseña o una captura |
| Documento repetido | Avisa que ya se cargó, con cuándo y en qué respuesta, sin IA |
| Datos que no están | Quedan vacíos y los completa la persona |
| Escrito a mano | Lo intenta y marca los datos dudosos |
| Otra moneda u otro idioma | Lo extrae igual |
| Documento sin texto | Funciona igual: el documento es el mensaje |
| Contacto sin cuenta vinculada | No se lee ni se le contesta; queda en el chat para un operador, con una línea que lo dice, y no entra en la bandeja |
| Cuenta sin permiso de cargar respuestas | No se lee, porque no tiene formularios posibles: lo dice y deriva |
| El formulario reconocido no se le puede mandar (por ejemplo, le falta el dato con el que se filtra un catálogo de E2) | Se lee y se reconoce; lo dice, deriva y queda propuesto en la bandeja. Nunca va la lista entera |
| Varias fotos de un mismo documento, o varios documentos en un mensaje | Después de esta etapa: necesita la ráfaga y las varias acciones de Casos |

**5. La bandeja «Documentos recibidos»** (DOC5-A).
- Una planilla como la de respuestas. El documento se abre en un panel al costado, junto a sus datos, como la vista rápida de una respuesta.
- Filtros: formulario, quién lo mandó, cuándo llegó y estado (propuesto, sin clasificar, confirmado o descartado, con cuántos hay). Todo vive en la dirección, con la página y el documento abierto.
- Búsqueda por los datos: monto (con o sin puntos), CUIT u operación, también en lo que no se cargó.
- **Para revisar acá:** los propuestos que la persona no confirmó antes de que venciera el envío, los que no se le pudieron mandar y las filas de una planilla que pasaron el tope. Alguien con permiso los pasa al formulario, y la respuesta dice de qué documento salió y quién la revisó.
- Un descartado o sin clasificar se borra a los 90 días, con su lectura, y su fila muestra la fecha.
- Dos permisos nuevos: «Ver documentos recibidos» y «Revisar documentos» (corregir, pasar al formulario y descartar).

**6. Cómo mejora con el uso.** Cada corrección queda registrada (el dato propuesto, el confirmado y quién lo corrigió) y se suma como ejemplo a «Cómo reconocerlo». **No se entrena ningún modelo.** Se mide la precisión por formulario y por campo: es la base de la carga automática.

**7. Formulario nuevo armado por la IA** (DOC6). Para un documento que no encaja en ningún formulario:
1. queda en la bandeja como «Sin clasificar»;
2. alguien con permiso toca «Armar formulario con la IA», y la IA propone el nombre, el prefijo, el texto del botón, «Cómo reconocerlo» y una pregunta por cada dato que se repite, con su tipo; si se parece a un estándar de la biblioteca, lo sugiere antes;
3. la persona revisa, ajusta y publica como cualquier formulario. La IA nunca publica ni aprueba ([ADR 0019](../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md));
4. el documento que lo originó se carga en el formulario nuevo, y desde ahí los parecidos se reconocen solos.

La bandeja agrupa los sin clasificar que se parecen y avisa, con tres o más: «Llegaron 5 documentos parecidos. ¿Armamos un formulario?». Retoma el pendiente «borrador de formulario armado por la IA» ([formularios](../formularios/README.md#después-de-la-etapa-5)).

**8. El tablero por formulario** (DOC7-A). Cada formulario tiene una pestaña «Tablero», armada sola a partir de sus preguntas y ajustable:

| Si el formulario tiene… | El tablero muestra… |
| --- | --- |
| Cualquier pregunta | Cuántas respuestas llegaron por día, semana o mes |
| Un número o un monto | Total, promedio, mínimo y máximo por período |
| Elegir una opción o un catálogo | El reparto por opción, por ejemplo transferencias por banco |
| Persona y estado | Quién cargó cuánto; aprobadas, rechazadas y pendientes |
| Fecha | La evolución en el tiempo |

- Se ajusta sumando o quitando gráficos y filtrando con la misma gramática de la planilla. Los ajustes son de cada persona, como las columnas de la planilla.
- No hay tableros en el código: salen de los tipos de las preguntas y de los valores con tipo que ya guardan las respuestas.
- Quien no puede leer respuestas no ve el tablero.
- Los tableros que cruzan formularios o suman datos externos vienen después ([F5](#f5--conexión-a-bases-de-clientes-y-tableros-cruzados)).

**9. La carga automática, en una etapa posterior.** Un modo por formulario, «Cargar solo si es seguro», que carga sin preguntar cuando se cumplen **las tres señales**:
1. el tipo es claro;
2. las validaciones fijas de los campos elegidos dan bien y no es un duplicado;
3. el formulario tiene un historial suficiente de cargas sin correcciones en esos campos.

La persona recibe «Cargué tu transferencia de $150.000 (REC-000123). Si algo está mal, avisá», y en la bandeja queda «Cargada automáticamente». **Nunca se usa la seguridad que declara el modelo.**

### Decisiones tomadas

Todas del **2026-10-22**, con el ADR 0022 aceptado ese día.

1. **Biblioteca:** formularios estándar como datos de inicio del producto. «Agregar» crea una copia propia y editable que recuerda de qué estándar viene. Sirve para una instalación de una empresa y para el multitenant.
2. **Primer estándar:** Comprobante de transferencia.
3. **La bandeja «Documentos recibidos»** entra en la primera etapa.
4. **La carga automática viene después, con datos medidos:** la primera etapa siempre propone y una persona confirma.
5. **Genérico:** ningún tipo de documento en el código. Un estándar nuevo es configuración.
6. **Formulario armado por la IA:** nace en la bandeja, sobre un documento sin clasificar, con el aviso de parecidos. La IA propone y una persona publica.
7. **Tableros:** uno por formulario, automático y ajustable. Los cruzados o con datos externos, después.
8. **Quién se lee:** solo personas con cuenta vinculada y activa (y, por la corrección al elegir los tableros, con el permiso de cargar respuestas).
9. **Retención:** un documento que termina en una respuesta se queda con ella. Uno descartado o sin clasificar se borra a los **90 días**, con su lectura.
10. **Tipos en la primera etapa:** foto, PDF, Word (`.docx`), Excel (`.xlsx`), ubicación y contacto.
11. **Tableros elegidos:** DOC5-A y DOC7-A; DOC1–DOC4 y DOC6 tenían un solo concepto.

Del **ADR 0022**, además:
- **Qué se manda al proveedor de IA:** el documento preparado y las preguntas del formulario candidato, **nada más** de la persona ni de otras respuestas. La cuenta del proveedor usa las opciones de no retener contenido que ofrezca, y se registra qué se mandó (hash y tamaño), sin el contenido.
- **Preparación antes de la IA:** tipo por contenido; topes; Word y Excel convertidos en el servidor a texto y tablas, **sin ejecutar nada**; ubicación y contacto no pasan por la IA.
- **Lo leído es un dato, nunca una instrucción.** Un texto dentro del documento que intente dar órdenes no cambia nada.
- **Validaciones fijas del lado del servidor:** dígitos verificadores (CBU, CVU, CUIT y CUIL), la entidad que sale del CBU o el CVU, formatos y duplicados. Un dato que no las pasa no se propone como válido.
- **Costos:** topes de documentos leídos por persona y por día, y en total, configurables. Al llegar al tope, el documento queda en la bandeja sin leerse.
- **Calificación** con casos ficticios antes de habilitar el perfil: comprobantes de varios bancos, fotos movidas, documentos que no son ningún tipo, inyección de instrucciones dentro del documento, Word y Excel.
- **Descartados:** Azure AI Document Intelligence como único lector (queda como opción por tipo, si un cliente tiene mucho volumen de un mismo diseño); cargar sin confirmación desde el principio; leer lo que mandan números desconocidos (queda para después).

### Fijado al dibujar y corregido al elegir (2026-10-22)

- Un nombre de formulario de más de 20 caracteres, lo que admite un botón de WhatsApp, hace que la pregunta de la duda vaya como **lista**.
- La lista de «no lo reconoce» entra en las 10 filas de una lista de WhatsApp, con «Otro» al final.
- De una planilla se proponen hasta 3 filas por mensaje, el tope de acciones del asistente.
- La ubicación y el contacto vuelven a mandar el formulario abierto, completado, y dicen qué preguntas completaron. Sin una pregunta elegida en el editor, quedan en el chat, como hoy.
- Un documento que no se lee deja una línea en el chat del operador y no entra en la bandeja.
- El aviso de parecidos aparece con tres o más sin clasificar que se parecen.
- En el tablero, un monto con moneda se cuenta por moneda («Total de Monto (pesos)»).
- La biblioteca es una pantalla nueva en Gestión de formularios, junto a Diseñar formularios, y pide «Diseñar formularios». «Agregar» pide nombre y prefijo, crea una copia en borrador y se puede repetir: dos copias del mismo estándar se distinguen por su «Cómo reconocerlo».
- En el editor, la sección «Lectura de documentos»: cambiarle el nombre a una pregunta no corta la lectura, un dato sin pregunta no se lee, las validaciones del estándar van con candado y prender o cambiar la lectura se guarda en el borrador y sale al publicar.
- Al confirmar el formulario prellenado se vuelven a revisar el CBU, el CVU, el CUIT y los duplicados con lo corregido; lo que no pasa no se guarda y dice por qué.
- Armar un formulario con la IA pide «Revisar documentos» y «Diseñar formularios»; con uno solo, el botón no aparece. Si falla, no queda nada a medias.
- La pestaña Tablero va junto a Respuestas, con el permiso de leer respuestas: quien solo aprueba no la ve.

### Decisiones abiertas

- **La evaluación de impacto contra el código y la spec técnica** (modelo de datos, contratos y árbol de archivos): son los pasos 3 y 4 del plan, sin hacer.
- **Orden frente a Casos:** lo define el usuario.
- **Retención de las respuestas:** el documento que termina en una respuesta sigue la retención de la respuesta, que es una decisión pendiente de producción.
- **El prellenado tiene que poder proponer un archivo:** hoy no lo hace. Cómo se implementa no está escrito. **[sin verificar]** Las fuentes no muestran que los Flows de Meta permitan precargar un archivo en una pregunta de documento; si no lo permiten, hay que resolver de otra forma «el comprobante, ya adjunto en su pregunta de documento». Es el riesgo técnico más grande del plan.
- **Valores de los topes:** de costo (por persona y día, y total), de páginas y de tamaño. El de las planillas remite a los de la importación de catálogos.
- **Cómo se mide que dos documentos sin clasificar «se parecen»:** no está definido.
- **Validaciones de un formulario propio sin estándar:** DOC1 dice que usa el mismo mecanismo «sin candados». Si puede elegir validaciones fijas, y cuáles, no está escrito.
- **Bibliotecas para pasar `.docx`, `.xlsx` y PDF a texto y tablas:** no elegidas.
- **Códigos de los dos permisos nuevos:** se fijan con la guía de permiso nuevo.
- **«Historial suficiente» de la carga automática:** se decide en esa etapa, con datos.

### Diseño técnico propuesto

**Cómo encaja, según el plan.**
- **Motor de IA ([ADR 0020](../../decisions/0020-motor-unico-de-ia-con-capacidades.md)):** el documento se vuelve legible para el modelo con un propósito propio, «leer documento», con su perfil calificado, como hoy la transcripción del audio. Clasificar y proponer los datos son **capacidades de BusinessForms**, de lectura y de propuesta. Ejecuta el módulo dueño.
- **Casos del asistente:** «llega un documento» es un tipo de entrada de los casos, que ya quedó previsto. Sin Casos, el circuito arranca igual, desde la recepción del documento.
- **Archivos privados de la 2B:** la copia, la verificación por contenido, los topes y la retención.
- **El prellenado** suma la propuesta de un archivo.

**Modelo.** Propuesta del anexo, armada sobre el plan y los tableros:

| Pieza | Contenido | De dónde sale |
| --- | --- | --- |
| Estándar de la biblioteca | Clave, nombre, versión, estado (disponible o próximamente), preguntas con tipos, «Cómo reconocerlo», validaciones fijas y pregunta de archivo | Plan y DOC1 |
| Lectura del formulario | Prendida, estándar y versión de origen, «Cómo reconocerlo», ejemplos, dato → pregunta por identidad, validaciones (con candado si vienen del estándar), cruce con catálogos y regla de duplicados. Vive en el borrador y se sella al publicar | DOC1 |
| Documento recibido | Mensaje y persona de origen, copia privada, tipo verificado, hash, estado, formulario reconocido y su seguridad, datos leídos con su seguridad, resultado de las validaciones, duplicado de, respuesta resultante, quién revisó y fecha de borrado | Plan y DOC5-A |
| Corrección | Dato propuesto, dato confirmado, quién y cuándo | Plan, «Cómo mejora con el uso» |
| Precisión | Por formulario y por campo | Plan |
| Entidades del BCRA | Prefijo → banco o billetera, cargado como datos | Plan |
| Topes de lectura | Por persona y día, y en total, configurables | ADR 0022, punto 9 |
| Ajustes del tablero | Por persona, como las columnas de la planilla | DOC7-A |

Para buscar por monto, CUIT u operación hace falta guardar esos valores de forma consultable. **Propuesta del anexo:** el mismo patrón de `FieldValuesJson` de E2, en jsonb con índice GIN.

**Puertos e integraciones:**
- **WhatsApp:** descarga del medio y copia privada, como el audio; los mensajes con botones o lista; la línea en el chat del operador; «Por qué lo propuso» o «Por qué preguntó» debajo del documento, como en AS4.
- **IA:** el propósito «leer documento» con su perfil; las capacidades de clasificar y de proponer datos; el registro de hash y tamaño de lo mandado.
- **BusinessForms:** validadores fijos, duplicados, prellenado con archivo, la bandeja, el tablero y la respuesta con su origen.
- **Archivos:** el almacenamiento privado de la 2B (Azure Blob en ArquitecturaBase).
- **Validaciones fijas:** las fuentes no dicen dónde vive el algoritmo de cada dígito verificador. **Propuesta del anexo:** como el menú de acciones de Casos, el código trae un conjunto cerrado de validadores y cada estándar elige cuáles usa; la tabla del BCRA es un dato.

### Tableros

Elegidos el 2026-10-22: la versión 116 del lienzo, marcados en la 117, con DOC5-A agrandado en la 118 y DOC2 y DOC4 ajustados al plan en la 119. Detalle en el [diseño del front](../../../../ArquitecturaBaseFront/docs/design/documentos.md#los-tableros). DOC5-B (la cola de revisión) y DOC7-B (un informe por pregunta) no se eligieron; se sacaron del lienzo y siguen en el repo del generador.

| Tablero | Qué define |
| --- | --- |
| [DOC1 · La biblioteca y la lectura](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc1-la-biblioteca-y-la-lectura.png) | La pantalla Biblioteca, con los estándares, su versión y en qué formularios propios está, y los que vienen como «Próximamente», sin acción. «Agregar» y la sección «Lectura de documentos» del editor, con la precisión que viene teniendo por formulario y por dato. |
| [DOC2 · Los tres casos y los otros formatos](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc2-los-tres-casos-y-los-otros-formatos.png) | Seguro («Recibí un comprobante de transferencia de $150.000 del Galicia. ¿Lo cargo?»), duda y no reconocido. PDF, Word, Excel con varias filas, ubicación y contacto. Los mensajes van en el idioma de la persona. |
| [DOC3 · El formulario prellenado](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc3-el-formulario-prellenado.png) | El Flow completo con lo leído: lo dudoso con «Revisá», lo no leído vacío con «No se leyó» (si es obligatorio, no deja confirmar) y el comprobante adjunto con «Cambiar documento». La revalidación al confirmar y la lectura en el detalle de la respuesta. |
| [DOC4 · Los casos difíciles](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc4-los-casos-dificiles.png) | Foto movida, PDF con contraseña, repetido, CBU mal leído, formulario que no se le puede mandar, formatos viejos y número sin cuenta vinculada. |
| [DOC5-A · La planilla con panel](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc5-a-la-planilla-con-panel.png) | La bandeja: columnas, búsqueda, filtros, panel con el documento (acercar, girar, páginas y descargar), cambiar el formulario reconocido (vuelve a leer), «Descartar» con confirmación, «Pasar al formulario», ↑ ↓, celular y los dos permisos. |
| [DOC6 · Armar formulario con la IA](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc6-armar-formulario-con-la-ia.png) | Desde el aviso de parecidos o desde un sin clasificar; la propuesta completa con los documentos a la vista; «Crear borrador» con la lectura prendida. |
| [DOC7-A · Tarjetas armadas solas](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc7-a-tarjetas-armadas-solas.png) | Arriba, los números contra el período anterior; abajo, una tarjeta por gráfico. «Agregar gráfico», «Volver al automático» y el menú de cada tarjeta (cambiar la cuenta, ver como tabla o quitarla). |

### Dependencias

- **Catálogos E2 cerrado:** el cruce del CUIT con Proveedores o Clientes usa sus campos extra, y «el formulario no se le puede mandar» viene de sus filtros por persona (CE4-A).
- **Casos del asistente:** el tipo de entrada «documento», la ráfaga (foto con audio), las 3 acciones (filas de una planilla) y derivar como en AS4. Varias fotos de un mismo documento quedan para después de esta etapa por la misma razón.
- **Etapa 2B:** copia privada, verificación por contenido y topes.
- **Etapa 5:** motor con perfiles calificados, como la transcripción, y Formularios que faltan.
- **Producción:** la retención de las respuestas.
- **La usa después:** Integraciones parte 2, con la biblioteca de formularios ya conectados.

### Cómo se cierra

Piloto real con Meta, con comprobantes reales de varios bancos y billeteras (Galicia, Santander, Nación, Mercado Pago, Brubank y otros):
- reconocimiento seguro, con duda y sin reconocer;
- un CBU con el dígito verificador mal leído, que no pasa;
- un duplicado;
- una foto movida;
- la bandeja y las correcciones registradas.

Antes, la calificación del perfil de lectura con los casos ficticios del ADR 0022.

### Qué cambia en multitenant

- **La biblioteca es de la plataforma, y el multitenant ya tiene una:** la E17, [biblioteca de plantillas](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/plans/formularios/e17-biblioteca.md), global en el esquema `platform`, con copia independiente. **Para evaluar:** unir los dos conceptos en la E17. Las diferencias que hay que resolver:
  - **quién la llena:** acá los estándares vienen como datos de inicio de cada versión del producto; allá «la biblioteca nace vacía y la llenan los operadores»;
  - **qué recuerda la copia:** acá recuerda estándar, versión y qué pregunta recibe cada dato, porque de eso dependen la lectura y los candados; allá la copia es independiente y conserva la versión de la plantilla solo para las reglas de ejemplo;
  - **qué trae:** la E17 no tiene «Cómo reconocerlo», validaciones fijas ni el mapeo de datos; trae catálogos a mano, reglas de ejemplo y el diseño del PDF;
  - **coinciden** en que nunca se publica sola y en que no se avisa una versión nueva.
- **Validaciones fijas y datos de referencia:** el multitenant ya tiene `platform.TaxIdTypes` con `ValidatorKey` (por ejemplo `ar-cuit-mod11`) y la regla «los algoritmos sí son código; la tabla dice qué tipos existen y cuál valida cada uno» ([datos de referencia](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/architecture/datos-de-referencia.md)). **Para evaluar:** reusar ese validador para CUIT y CUIL, sumar el de CBU y CVU, y cargar la tabla de entidades del BCRA como dato de referencia global.
- **Por organización:** documentos, lecturas, correcciones, precisión, ajustes del tablero y topes configurados son datos privados (`ITenantOwned`, RLS). Los workers bajan el medio con el token de la organización, como ya prevé la E14 del multitenant.
- **Quién se lee:** «cuenta vinculada y activa con permiso de cargar respuestas» equivale, en el multitenant, a un miembro activo con teléfono verificado y `formrecords.create`, en la bandeja Internos. Es coherente con su decisión 29.
- **Personas B2C:** un cliente que manda un comprobante a una empresa no se lee, por el ADR 0022 («leer lo que mandan números desconocidos queda para después»). **Para evaluar:** si se habilita para la bandeja Clientes, la respuesta sería un dato compartido persona↔organización (`IConsumerBusinessShared`), que el multitenant deja para después del bloque y que depende de su backlog #51.
- **Costos:** el costo de cada lectura entra en el tope mensual de IA de la organización (ADR 0051). Los topes por persona y día, y en total, pasan a ser por organización.
- **No retener contenido en el proveedor:** con clave propia de la organización o con la de la plataforma (ADR 0048, D23), cambia qué cuenta tiene que estar configurada así. **Para evaluar** quién lo garantiza en cada caso.
- **Retención:** el documento en una respuesta sigue a la respuesta, que en el multitenant se guarda mientras exista la organización (decisión 23). Los 90 días de los descartados son una regla aparte y se mantienen. **Para evaluar:** su relación con el participante de baja de cuenta.
- **Archivos:** `IFileStore` y su adaptador de nube (E10), y el antivirus antes de producción que ya pide la E14.
- **Empresas:** la respuesta guarda las empresas de quien respondió (decisiones 1 y 9). **Para evaluar:** un filtro por empresa en la bandeja y en el tablero, como en la planilla.
- **Permisos:** los dos nuevos de organización en el catálogo del núcleo; la biblioteca la mantiene la plataforma con su permiso (`platform.formtemplates.manage` en la E17).

---

## F4 · Catálogos después de E2

Fuente: [hoja de ruta de catálogos](../2026-10-21-catalogos-con-origenes-externos.md), actualizada el 2026-10-23. E1 y E2 están implementados ([reglas](../../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23)); lo que sigue son las fases 3 a 6.

### Problema y objetivo

Traer las listas de un sistema externo (proveedores, artículos, sucursales, clientes) a los catálogos, con catálogos grandes de hasta 10.000 opciones, sin escribir nada fijo en el código y permitiendo que **cualquier sistema** se conecte. WhatsApp muestra hasta 200 opciones por pregunta en un desplegable y hasta 20 en «elegir varias».

### Lo que definió el usuario

No se vuelve a preguntar.
- **Primer caso real:** un cliente con una base de un ERP conocido (en la fuente, OCHA S.A. con Macrosistemas, el mismo esquema que La Clementina). Hace falta traer sus listas y, más adelante, un conector a esa base.
- **Primer paso: Excel con una plantilla definida por el catálogo.** Hecho en E1-A.
- **Tipos de datos validados al cargar.** Hechos en E1-B y E2.
- **Nada fijo en el código:** el catálogo, sus campos y sus tipos son configuración. En el código viven solo el conjunto de tipos y el mecanismo de importación y validación.
- **Catálogos grandes:** hasta 10.000 opciones.
- **Quién configura los orígenes:** las dos posibilidades, nosotros en la implementación y el cliente.
- **El asistente ayuda:** arma un catálogo a pedido y devuelve su plantilla; lee el Excel que sube la persona, propone a qué catálogo va y cómo se mapea, y lo importa cuando la persona confirma; en la sección Catálogos hay una entrada o una guía para usarlo.
- **El sistema vive en Azure.** Lo que se diseñe tiene que permitir que cualquier sistema se conecte.

### La idea central

Cualquiera sea el origen (Excel, una API o una base), las opciones se guardan en una **copia local** y **los formularios siempre leen esa copia**. Nunca se consulta el sistema del cliente al mandar un formulario. Así, si ese sistema se cae, los formularios siguen andando; lo que deja de venir queda inactivo, no se borra; y cambiar de origen no toca formularios, WhatsApp ni el asistente.

### Qué se construye, por fase

| Fase | Qué incluye | Estado |
| --- | --- | --- |
| 1 | Carga de una vez, tipo de valor y elegir varias | Implementada; review cerrado; piloto único pendiente |
| 2 | Campos extra, qué viaja a WhatsApp y filtrar por persona | Implementada; review en corrección |
| **3** | **Búsqueda en vivo con Meta** | Por diseñar (necesita el intercambio cifrado) |
| **4** | **El asistente arma catálogos y mapea el Excel** | Por diseñar |
| **5** | **Recepción por API, conector y plantillas de origen** | Por diseñar |
| **6** | **Lectura de APIs; conexión directa solo si hace falta** | Por diseñar |

**Fase 3 · Búsqueda en vivo.** La persona escribe y WhatsApp consulta nuestras opciones. Necesita el intercambio cifrado con Meta, pendiente desde la Etapa 5, que también sirve para listas dependientes y listas largas. La hoja de ruta dice que sube de prioridad con las listas grandes del primer cliente.

**Fase 4 · El asistente arma catálogos e importa.** La IA propone, una persona aprueba y el sistema ejecuta ([ADR 0019](../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md), [ADR 0020](../../decisions/0020-motor-unico-de-ia-con-capacidades.md)):
- **crear a pedido:** «Creame un catálogo de proveedores con CUIT, razón social, rubro y zona». El asistente propone los campos y sus tipos, la persona los aprueba, el catálogo se crea inactivo y el asistente ofrece la plantilla;
- **importar un Excel que ya tiene la persona:** el servidor le pasa al modelo **solo los encabezados y unas pocas filas de muestra**, nunca el archivo entero; el modelo propone el catálogo y el mapeo; la persona confirma; la importación la hace el sistema por el recorrido de E1-A;
- **desde la sección Catálogos:** una entrada como «Pasale tu Excel al asistente».

Hoy existe solo la propuesta de catálogos Filters desde el chat administrativo (H4).

**Fases 5 y 6 · Los orígenes externos.**

| Origen | Cómo funciona | Riesgo |
| --- | --- | --- |
| 1. Excel o CSV | Implementado en E1-A | Ninguno: no se toca el sistema del cliente |
| 2. Recepción por API | El sistema del cliente manda la lista con una clave que solo sirve para ese catálogo | Bajo: no se guardan credenciales ajenas |
| 3. Conector en su red | Un programa chico, junto a su base, lee la consulta configurada y manda la lista por el origen 2. Solo hace conexiones de salida: sin VPN ni puertos abiertos | Medio: hay que distribuirlo y mantenerlo |
| 4. Lectura de su API | Se configuran dirección, autenticación, paginación y mapeo, y el sistema la lee cada cierto tiempo | Medio: se guarda una credencial suya |
| 5. Conexión directa a su base | Solo si la app corre en su misma red, con una consulta de solo lectura validada | Alto: necesita un ADR que amplíe el 0019 |

**Plantillas de origen:** para sistemas conocidos se guarda la consulta (por ejemplo, la de proveedores) y su mapeo, listos para reusar con otro cliente del mismo sistema. Son datos, no código.

**Campos que completan otras preguntas:** completar otras preguntas a partir de un dato de la opción, como el CUIT, sigue siendo una propuesta posterior. El origen externo alimenta estos mismos catálogos y no cambia el contrato del envío.

### Conectividad

- **El servidor del cliente (IIS)** sirve para salir rápido, con dos condiciones sin resolver: WhatsApp necesita llegar al sistema desde internet (el webhook de Meta es una dirección pública con HTTPS), y todo lo demás tiene que correr ahí (PostgreSQL, Redis, archivos y la salida a OpenAI). No hay guía de despliegue para IIS.
- **Azure con el conector** escala a cualquier cliente: la app recibe a WhatsApp sin problemas y el conector manda los datos hacia afuera por HTTPS, sin VPN, túnel ni puertos abiertos del lado del cliente.
- **Si un cliente no acepta el conector:** una VPN de sitio a sitio con Azure (cara y con trabajo de red de los dos lados) o un relay de Azure.
- **MCP** sirve para que un asistente consulte datos en vivo, como el stock o el saldo, en la etapa de Agentes. Para sincronizar catálogos no conviene: hace falta una carga controlada, validada y repetible.

**Inconsistencia entre las fuentes.** La hoja de ruta recomienda el conector y deja la VPN como alternativa. El índice de planes, en la entrada del 2026-10-22 sobre la conexión a bases de clientes, dice lo contrario: el conector propio y Azure Data Factory se descartaron por complejos, y la opción propuesta es la VPN ([F5](#f5--conexión-a-bases-de-clientes-y-tableros-cruzados)). Las dos siguen sin decisión del usuario.

### Sincronización y seguridad (propuesta del plan)

- Leer → validar → diferencias → aplicar → reporte, con las mismas protecciones que la importación de Excel.
- Cada lectura externa es una **operación durable** con lease, fence y reintentos con espera creciente ([ADR 0018](../../decisions/0018-robustez-proporcional-al-efecto.md)). El estado se ve: «Actualizado hace 2 h · 1.240 opciones» o «Falló la última actualización: …».
- Credenciales cifradas, que nunca vuelven al front ni a los logs, y rotables.
- Claves de recepción y del conector que se ven una sola vez, se guardan como hash, se revocan y tienen alcance mínimo.
- Un permiso propio para configurar orígenes externos.
- Lectura de APIs: solo HTTPS, sin redirecciones, con lista de hosts permitidos y topes de tiempo, tamaño y filas.
- Lo que llega son datos: se valida y nada se ejecuta.

**Esquema propuesto:**

| Tabla | Qué guarda |
| --- | --- |
| `FormCatalogSources` | Origen: tipo, configuración no secreta, secreto cifrado, frecuencia, estado, lease y fence |
| `FormCatalogSourceTemplates` | Plantillas de origen |
| `FormCatalogSyncRuns` | Una fila por sincronización: disparador, estado, contadores, hash del lote y motivo seguro |
| `FormCatalogSyncIssues` | Filas con errores (fila, columna y motivo), con retención limitada |
| `FormCatalogSourceKeys` | Claves de recepción y del conector, guardadas como hash |

### Decisiones abiertas

Las cuatro preguntas de la hoja de ruta, más la elección de conectividad:
1. Del primer sistema externo: qué motor y versión de base usa (SQL Server, según la fuente, con signo de pregunta) y qué listas hacen falta primero, con cuántas filas cada una.
2. Primera instalación del primer cliente: servidor del cliente o Azure; si es su servidor, cómo llega WhatsApp y quién mantiene PostgreSQL y Redis.
3. Opciones que vienen de afuera: si se pueden ocultar o renombrar localmente. Recomendado: solo ocultar.
4. Si al elegir una opción se completan otras preguntas con sus campos extra.
5. Conector o VPN ([F5](#f5--conexión-a-bases-de-clientes-y-tableros-cruzados)).

### Tableros, dependencias y cierre

- **Tableros:** ninguno para las fases 3 a 6.
- **Dependencias:** E1 y E2 cerrados (review y piloto único); el intercambio cifrado con Meta para la fase 3; la decisión de conectividad para las fases 5 y 6; un ADR propio para conectarse a la base del cliente (el 0019 sigue vigente solo para «Consulta» sobre nuestra base). La conexión de la fase 6 comparte modelo de credenciales y protección SSRF con las conexiones de Integraciones ([F6](#f6--integraciones-partes-2-y-3)).
- **Cierre:** no está escrito para estas fases.

### Qué cambia en multitenant

- **Por organización, según la propia hoja de ruta:** «Multitenant: todo va por organización». Orígenes, sincronizaciones, problemas y claves son datos privados de la organización. Cada clave de recepción o de conector identifica a la organización y al catálogo.
- **Plantillas de origen:** **para evaluar** si son datos globales de la plataforma, porque sirven para cualquier organización que use el mismo sistema.
- **Credenciales por organización:** el estudio de integraciones recomienda que las credenciales con efecto económico usen el anillo externo del [ADR 0014](../../decisions/0014-credenciales-de-ia-fuera-de-la-base.md), no Data Protection con claves en la base. Si el multitenant tiene un equivalente: **[sin verificar]**.
- **Fase 4:** el resumen del asistente del multitenant prevé solo catálogos con filtros propuestos por la IA (H4). Armar catálogos a mano con campos y mapear un Excel es nuevo para él.
- **Fase 3:** el multitenant ya puso el intercambio cifrado en «después del bloque». **[sin verificar]** Si las claves del intercambio cifrado de Meta van por número de WhatsApp, cada número de cada organización necesita las suyas.
- **Conectividad:** en una plataforma compartida, una VPN por cliente es infraestructura de la plataforma con efecto en todas las organizaciones. **Para evaluar** antes de prometerla: aislamiento de red, rangos de direcciones que se pisan entre clientes, costo por organización y quién la opera. El conector de salida no tiene esos problemas.
- **Idioma:** los nombres de catálogos, campos y opciones que lleguen de afuera siguen la regla del idioma original ([sección 1](#1-reglas-del-usuario-del-2026-10-23-que-valen-para-todo)).

---

## F5 · Conexión a bases de clientes y tableros cruzados

Fuentes: [índice de planes](../README.md) (entradas «Conexión a bases de datos de clientes» y «Tableros con datos propios y de sistemas externos») y la [hoja de ruta de catálogos](../2026-10-21-catalogos-con-origenes-externos.md#conectividad-el-servidor-del-cliente-o-azure).

### Problema y objetivo

Traer datos de un sistema externo del cliente (el ejemplo de la fuente es el ERP del primer cliente) para los catálogos y, a futuro, para tableros que crucen formularios o sumen datos externos.

### Estado y decisiones

- **Pendiente de decisión del usuario** desde el 2026-10-22.
- **Descartados por complejos:** un conector propio y Azure Data Factory.
- **Opción propuesta:**
  - una VPN de sitio a sitio de Azure a la red del cliente, con un usuario de solo lectura;
  - «Conexiones externas» en Administración;
  - un catálogo «Desde una base externa» que elige tabla o vista y mapea columnas, **sin escribir SQL**.
- **Requisitos:** un ADR que amplíe el 0019 a bases externas, y que el ambiente de Azure corra dentro de una red virtual.
- **Tableros:** el automático por formulario ya está decidido en Documentos. Los cruzados y los que suman datos externos dependen de esta conexión. Los datos externos **se copian a una base de análisis** y no se consultan en vivo; los tableros pueden ser propios o en Power BI.

### Decisiones abiertas

Todo: la conexión misma, la base de análisis, propios o Power BI, y cómo se resuelve la inconsistencia con la hoja de ruta (conector frente a VPN, ver [F4](#f4--catálogos-después-de-e2)).

### Tableros, dependencias y cierre

Sin tableros ni cierre escritos. Depende de la red de Azure y del ADR.

### Qué cambia en multitenant

- **Para evaluar:** una red virtual con VPN a cada cliente es una decisión de plataforma, no de una organización (ver [F4](#f4--catálogos-después-de-e2)).
- **Para evaluar:** la base de análisis guarda datos de varias organizaciones. Necesita el mismo aislamiento que la base principal, o una por organización; Power BI necesita su propio modelo de acceso por organización.
- **Para evaluar:** el «catálogo desde una base externa» sin SQL encaja con la regla del multitenant de catálogos con filtros sobre fuentes definidas en el código, y no con SQL configurable, que su E13 deja fuera.

---

## F6 · Integraciones, partes 2 y 3

Fuente: [estudio con fuentes del 2026-10-22](../integraciones/estudio-2026-10-22.md), entero. Cada dato del estudio lleva su URL; acá va lo necesario para evaluar y el enlace a la sección.

### Problema y objetivo

- **Parte 2:** formularios conectados a sistemas externos: emitir una factura en ARCA, publicar en Mercado Libre, cobrar con Mercado Pago o llamar a cualquier API.
- **Parte 3:** que otros sistemas reciban nuestros datos: webhooks salientes, una API pública (incluido mandar formularios por nuestro WhatsApp) y un servidor MCP.

### Estado y decisiones tomadas

- **Investigación hecha el 2026-10-22.** El estudio separa los datos con fuente oficial de lo «sin confirmar» y de sus propias «inferencias».
- **Decisión tomada:** solo el orden: las 13 decisiones de la sección E.4 quedan para después de la parte 1 (Documentos). No hay ninguna otra decisión.

### Resumen de la investigación

1. **Facturar por chat** (Facturitas, Tramitito, el bot del CPCE de Córdoba): la persona **delega el web service «Facturación Electrónica» en la CUIT del proveedor** desde el Administrador de Relaciones de ARCA y crea un punto de venta de web service; después factura con texto o audio, **confirma un resumen** y recibe el PDF con CAE y QR. Si ARCA no responde, queda en cola. Para copiar: la delegación (no guardar la clave fiscal), el resumen con confirmación, la cola durable, el PDF en el mismo chat, «cobraste, facturado» y el aviso del tope del monotributo.
2. **ARCA tiene dos piezas:** el WSAA (ticket de acceso de 12 horas por servicio, que hay que compartir entre hosts) y el WSFEv1 (autoriza comprobantes).
3. **Emitir una sola vez** se apoya en el número de comprobante, consultando antes de reintentar; exige serializar por CUIT, punto de venta y tipo.
4. **Plazos normativos:** la condición frente al IVA del receptor (RG 5616) es obligatoria y, según la prensa, ARCA rechaza desde el **1/12/2026** las solicitudes que no la informen. El manual de WSFEv1 tuvo seis versiones en 2026.
5. **Mercado Libre** renueva con un refresh token de un solo uso: con varios hosts, la renovación tiene que ser serializada. No hay sandbox.
6. **Mercado Pago** cobra con una preferencia (`init_point`) y avisa con una notificación firmada.
7. **Zapier, Make y n8n** coinciden en el modelo: conexiones con prueba, disparadores, mapeo con datos de muestra, reintentos e historial de ejecuciones.
8. **Para que la plataforma sea el centro** hacen falta webhooks firmados, una API pública con un JSON Schema por formulario y versión, y un servidor MCP, todos sobre los mismos casos de uso y permisos.
9. **Fuera de la ventana de 24 horas** solo se mandan plantillas aprobadas; desde el 1/10/2026 Meta cobra las plantillas de utilidad aunque la ventana esté abierta.
10. **MCP** (especificación del 28/7/2026) autoriza con OAuth 2.1 a los servidores remotos; hay SDK oficial de C#.
11. **Recomendación:** un módulo opcional `Integrations` con conexiones por organización y una acción de regla «Ejecutar en un sistema externo» que reusa el motor de acciones durable.

### Arquitectura recomendada (propuesta del plan, [E.1](../integraciones/estudio-2026-10-22.md#e1-arquitectura))

1. **Módulo opcional `Integrations`**, bajo `Modules/Integrations` en cada capa, que el núcleo no nombra.
   - **Puerto neutral** en `Application/Interfaces/Channels/`, con versión apagada en `Application/Channels/`, por ejemplo `IExternalOperationChannel`. Es el patrón de `IStructuredRecordActionChannel`, el canal de avisos que WhatsApp implementa sin que BusinessForms lo referencie.
   - Expone un **catálogo de operaciones** (clave, nombre, conexión, esquemas de entrada y salida, y si es idempotente o verificable), **ejecutar** (operación, entrada y clave de idempotencia → Confirmado, Falló o Sin confirmar) y **reconciliar** (clave → Confirmado, «No se ejecutó» o Desconocido).
2. **Conexiones por organización.**
   - Guardan tipo de conector (ARCA, Mercado Libre, Mercado Pago o HTTP), **ambiente** (prueba o producción), configuración no secreta (CUIT, punto de venta, URL base, hosts permitidos), secreto cifrado, estado (sin verificar, activa, requiere volver a conectar, revocada) y revisión, con 409.
   - Los secretos con efecto legal o económico usan el **anillo externo del [ADR 0014](../../decisions/0014-credenciales-de-ia-fuera-de-la-base.md)**, para que el backup de la base no alcance para descifrarlos. El certificado propio de la plataforma va en Key Vault.
   - Prueba de la conexión: `FEDummy` y un ticket para ARCA; una lectura simple para Mercado Libre y Mercado Pago (el endpoint, sin confirmar); un GET configurable para el genérico.
   - La **misma conexión** sirve para el origen 4 de catálogos, «lectura de su API»: un solo modelo de credenciales y de SSRF.
3. **La acción de regla «Ejecutar en un sistema externo».**
   - Configuración: conexión, operación, mapeo de preguntas (por identidad) a la entrada, mapeo de la salida a «resultados externos» y qué hacer si falla (detener la regla o seguir).
   - Ejecución con **la misma maquinaria del motor actual**: run con clave respuesta/evento/regla/acción, reserva con lease y fence, efecto fuera de la transacción, confirmación bajo el fence vigente y estado incierto que se consulta antes de repetir.
   - Cada conector aporta su reconciliación: `FECompConsultar` para ARCA, la búsqueda por `seller_sku` para Mercado Libre, `external_reference` para Mercado Pago y la consulta de verificación configurada para el HTTP. Sin reconciliación posible, va a **revisión humana**, con «No se ejecutó (reintentar)» y «Se ejecutó (cargar referencia)», como la revisión actual de avisos inciertos.
   - El resultado **se guarda en la respuesta**, validado con el `outputSchema`, y las acciones siguientes y el PDF lo usan como placeholders (`{{factura.numero}}`, `{{factura.cae}}`). El detalle muestra «Resultados externos».
   - **Cambio en el motor:** hoy «generar PDF» existe solo al aprobar y con diseño libre. La factura necesita un **PDF fiscal** con QR y datos obligatorios, que pueda salir también **al recibir**.
4. **Conectores nativos frente al genérico.** Nativos, en código, para lo que tiene protocolo propio: ARCA, Mercado Libre y Mercado Pago. Genérico, por configuración: el HTTP, los webhooks salientes y, después, importar OpenAPI. Las plantillas de conexión para sistemas conocidos son datos.
5. **Una biblioteca de formularios ya conectados,** sobre la biblioteca de Documentos: un estándar conectado trae además la conexión requerida, el mapeo y las reglas.
   - **«Emitir factura»:** cliente (del catálogo Clientes, con CUIT o DNI y condición frente al IVA, o «Consumidor final»), concepto, detalle, importe o cantidad por precio, alícuota (solo responsable inscripto), período y vencimiento del pago (si es servicio), fecha (hoy por defecto) y «mandar copia al cliente». Reglas: aprobación opcional por monto, emitir en ARCA, generar el PDF fiscal y avisar con el PDF. Validaciones fijas: dígito de la CUIT, clase compatible con la condición frente al IVA, fechas dentro de la ventana e importe mayor que cero.
   - **«Anular factura (nota de crédito)»:** referencia una respuesta con factura emitida; una sola nota de crédito por factura y motivo.
   - **«Cobrar con link de Mercado Pago»** y **«Publicar en Mercado Libre»**.
6. **La IA** transcribe el audio («facturale 80 lucas a Marta…»), busca a Marta en Clientes y **propone «Emitir factura» prellenado**. La persona confirma en el Flow y recién entonces corren las reglas. **La IA nunca elige la conexión ni llama a ARCA**, y la autorización no depende del prompt.

### ARCA: factura electrónica ([A.2](../integraciones/estudio-2026-10-22.md#a2-arca-factura-electrónica-por-web-service))

- **Pasos previos:** certificado digital (homologación en WSASS; en producción se genera, se asocia al servicio y se delega), cliente del WSAA, cliente del servicio de negocio, empadronamiento si la norma lo exige y alta del punto de venta.
- **WSAA:**
  - El pedido (TRA) es un XML `loginTicketRequest` con `uniqueId`, `generationTime`, `expirationTime` y `service` (`wsfe`), sin `source` ni `destination`, con unos minutos restados a `generationTime` y el reloj sincronizado por NTP.
  - Se firma en CMS (PKCS#7) con el certificado; las clases de .NET (`SignedCms`) alcanzan. El algoritmo de hash exigido: sin confirmar.
  - El ticket (TA) trae `token`, `sign` y `expirationTime`, vale **para un servicio** y dura **12 horas**.
  - **Reutilizarlo es obligatorio en la práctica:** pedir otro TA para el mismo servicio dentro de **2 minutos en producción (10 en homologación)** da «El CEE ya posee un TA valido». Un certificado no se puede usar en dos equipos a la vez. El TA vive **en un lugar central** (base o Redis, cifrado) y se renueva con candado. Que varios hosts compartan token y sign es la práctica de las bibliotecas, sin frase oficial (inferencia).
  - El token lista las CUIT representadas; el WSFEv1 tiene el error 601, «CUIT representada no incluida en token». Inferencia: cuando un cliente nuevo delega, hay que pedir un TA nuevo, respetando el lapso.
  - Endpoints configurados como dato: los documentos oficiales usan `afip.gov.ar` y `arca.gov.ar`, y el 22/10/2026 `wswhomo.arca.gov.ar` no resolvió.
  - Homologación y producción no se mezclan: un certificado de un ambiente usado en el otro falla.
- **Delegación, el modelo de Facturitas y Tramitito:** el administrador de relaciones del cliente elige como representante la CUIT del proveedor (constancia F3283/E); el proveedor acepta la designación y autoriza uno de sus computadores fiscales para ese cliente y servicio; el cliente puede revocarla. Inferencia: el segundo paso se repite por cada cliente, y ARCA no publica una API del Administrador de Relaciones.
- **WSFEv1:**
  - `FECAESolicitar` autoriza; `FECompUltimoAutorizado` da el último número por CUIT, punto de venta y tipo; `FECompConsultar` devuelve un comprobante con su CAE; `FEParamGet*` dan los parámetros, incluido `FEParamGetCondicionIvaReceptor`; `FEDummy` verifica el servicio.
  - Respuesta A, R o P, con CAE de 14 dígitos, vencimiento y observaciones. Errores 500, 501 y 502 («Transacción Activa»), 600, 601 y 602.
  - Lotes de 1 a 9998 (el tope real lo da `FECompTotXRequest`); la factura de crédito MiPyME va de a una; un rechazo en un lote deja sin procesar los siguientes.
  - Redondeo «Round Half Even», como `MidpointRounding.ToEven`.
- **Punto de venta** del tipo RECE. Recomendación: **un punto de venta exclusivo de la plataforma**, que nadie más use, para no pelear la numeración.
- **CAE frente a CAEA:** CAE en línea por comprobante, con cola y reintentos. El CAEA es por quincena, solo para A y B, y con la RG 5782 pasó a ser contingencia.
- **Qué comprobante corresponde:** monotributo o exento emite **C** (11 factura, 12 nota de débito, 13 nota de crédito, 15 recibo); responsable inscripto emite **A** a responsable inscripto o monotributo (exige CUIT) y **B** a exento, consumidor final y otros; exportación **E**, por otro servicio (WSFEXv1).
- **Campos de `FECAESolicitar`:** concepto (productos, servicios o ambos), documento del receptor (80 CUIT, 86 CUIL, 96 DNI, 99 sin identificar), número siguiente al último, fecha dentro de ±5 días (productos) o ±10 (servicios), importes que suman, fechas de servicio y vencimiento obligatorias con servicios, moneda y cotización, **`CondicionIVAReceptorId` obligatorio (RG 5616)** y el array de IVA, que no va en C.
- **QR obligatorio (RG 4892/2020):** `https://www.arca.gob.ar/fe/qr/?p=` + JSON en Base64 con versión, fecha, CUIT, punto de venta, tipo, número, importe, moneda, cotización, documento del receptor, tipo de código (E para CAE) y el código. Por web service, el QR lo arma quien emite. Responsable inscripto a consumidor final suma el apartado de la RG 5614. Los requisitos completos del PDF (RG 1415): sin confirmar.
- **Anular** es emitir una nota de crédito de la misma clase que referencia el original en `CbtesAsoc`.
- **Emitir una sola vez** ([algoritmo](../integraciones/estudio-2026-10-22.md#cómo-evitar-emitir-dos-veces)):
  1. reservar la operación con lease y fence;
  2. tomar el candado de numeración por CUIT, punto de venta y tipo;
  3. si la operación ya tenía número de un intento anterior, saltar a reconciliar;
  4. pedir el último autorizado N y guardar número N+1 y el hash de los datos, en estado «Enviando», **confirmando esa escritura antes de llamar**;
  5. pedir el CAE: aprobado → Confirmado; rechazo por validación → Falló, con el número libre; error 10016, timeout, caída o 500/501 → Sin confirmar; 502 → esperar y reconciliar;
  6. reconciliar con `FECompConsultar`: si existe y coincide, Confirmado; si existe y no coincide, conflicto para revisión humana; si no existe y el último es menor, reintentar **con el mismo número**;
  7. liberar el candado.

  Si ARCA está caído, la cola sigue con espera creciente; hay que vigilar la ventana de la fecha.
- **Bibliotecas .NET:** recomienda **un cliente propio** (dos servicios SOAP, firma con `SignedCms`), con ElRoso.ARCA como referencia de lectura y **arcasim** (simulador de WSAA y WSFEv1 con fallas a pedido) para probar. **No usar Afip SDK**, que manda certificado y clave a su nube.

### Mercado Libre ([B](../integraciones/estudio-2026-10-22.md#b-mercado-libre))

- **OAuth del vendedor:** Authorization Code, con PKCE opcional (obligatorio si se activa), `redirect_uri` exacto y estático, y `state` para los datos variables. El access token dura lo que diga `expires_in` (el texto dice 6 horas y el ejemplo 3). El refresh token es **de un solo uso**, solo vale el último y vence a los 6 meses. Quien autoriza tiene que ser administrador. Invalidan los tokens el cambio de contraseña o de secreto, la revocación, **4 meses sin llamadas** y revocaciones internas. La renovación va **serializada entre hosts**, con el refresh nuevo guardado en la misma escritura, y la conexión necesita el estado «Requiere volver a conectar».
- **Publicar:** `POST /items`, con el atributo `ITEM_CONDITION` y el modelo User Products (sin título) para desarrollos nuevos; la descripción va después; un responsable inscripto manda `VALUE_ADDED_TAX` e `IMPORT_DUTY`. Predictor de categoría (`domain_discovery`), atributos obligatorios por categoría con sus *tags* y `POST /items/validate` antes de publicar. Fotos JPG o PNG de hasta 10 MB, que conviene **subir** y no exponer como URL.
- **Después:** estados, pausa por stock 0, cambio de precio que falla con la automatización activa (desde el 18/3/2026) y notificaciones finas que hay que contestar con **200 en 500 ms**; los reintentos duran una hora y los tópicos se desactivan si la app no responde.
- **Pruebas y límites:** no hay sandbox, solo hasta 10 usuarios de test. El 429 se aplica por client ID y por endpoint. **Un exceso de 4xx puede bloquear la aplicación, y el bloqueo frena todas las APIs de Mercado Libre y Mercado Pago.**
- **Idempotencia:** no encontró clave para `POST /items` (sin confirmar). Hay que guardar el id de la operación en `SELLER_SKU` y buscar por SKU antes de reintentar.

### Mercado Pago ([C](../integraciones/estudio-2026-10-22.md#c-mercado-pago))

- **Link de pago:** `POST /checkout/preferences` con ítems, pagador, `notification_url`, `external_reference` y vencimiento; responde `init_point`.
- **Notificación:** encabezado `x-signature: ts=…,v1=…`; se firma `id:[data.id];request-id:[x-request-id];ts:[ts];` con HMAC-SHA256 en hexadecimal; hay que responder 200 o 201 en **menos de 22 s** y después consultar `GET /v1/payments/{id}`.
- **Idempotencia:** `X-Idempotency-Key` es obligatoria en Payments y Refunds; en preferencias no figura.
- **OAuth para operar en nombre de vendedores:** código de 10 minutos y de un solo uso, refresh de 6 meses **reutilizable**; la duración del access token, sin confirmar.
- **Para nosotros:** acción «Crear link de pago» con `external_reference` igual a la clave de la operación, webhook entrante que dispara el evento «Pago aprobado» y el caso «cobraste, facturado».

### Plantillas de Meta y la ventana de 24 horas ([F.3](../integraciones/estudio-2026-10-22.md#f3-la-ventana-de-24-horas-de-whatsapp))

- Cada mensaje de la persona abre o renueva una ventana de 24 horas; fuera de ella, solo plantillas aprobadas.
- Un Flow fuera de la ventana va como **plantilla con botón FLOW** aprobada, con `flow_token` y los datos de la primera pantalla para prellenar. Inferencia: cada formulario necesitaría su propia plantilla; si una plantilla puede apuntar a distintos Flows, sin confirmar.
- **Costos desde el 1/10/2026:** las plantillas de utilidad dentro de la ventana dejan de ser gratis; hay **1.000 mensajes de servicio gratis por mes y por número**; sin medio de pago, Meta deja de entregar los mensajes de servicio que pasan a cobrarse.
- **Hoy:** el canal de avisos comprueba la ventana y fuera de ella deja un fallo visible. Para que un sistema externo mande formularios a cualquiera hace falta **una etapa de plantillas**: alta, aprobación, categoría, costo y opt-in. El plan de [WhatsApp después del chat](../2026-10-01-bandeja-whatsapp.md) ya tenía la gestión de plantillas como propuesta (`POST /{WABA_ID}/message_templates`, permiso propuesto `whatsapp.templates.manage`).

### Webhooks salientes ([F.1](../integraciones/estudio-2026-10-22.md#f1-webhooks-salientes-por-formulario) y [D.2](../integraciones/estudio-2026-10-22.md#d2-una-acción-http-genérica))

- **Eventos:** `form.response.received`, `form.response.approved` y `form.response.rejected`; después, eventos de integraciones como factura emitida o pago aprobado.
- **Firma con Standard Webhooks:** `webhook-id` (estable entre reintentos), `webhook-timestamp` y `webhook-signature` = `v1,` + HMAC-SHA256 en Base64 de `id.timestamp.cuerpo`. El secreto empieza con `whsec_`, se muestra una sola vez y se rota mandando dos firmas mientras conviven.
- **Reintentos** con espera creciente y jitter durante horas o días; un 410 corta; una falla sostenida avisa y desactiva. Registro por intento (código, duración y motivo seguro) y reenvío manual.
- **Cuerpo fino por defecto:** identificadores, el formulario (id, clave y **versión**) y el estado; los valores se piden por la API con permiso. Opcionalmente, cuerpo completo **con los valores por clave estable de la pregunta**. Los archivos nunca viajan dentro: van como enlace a la API.
- **Cambios del formulario:** el cuerpo lleva `formVersion` y las preguntas se identifican por clave estable, nunca por etiqueta ni posición. El JSON Schema de esa versión se puede pedir.
- **Cómo se crean:** el repo no tiene eventos de dominio ([ADR 0003](../../decisions/0003-sin-eventos-de-dominio.md)), así que la entrega se crea **explícitamente en el mismo commit** que confirma la captura o la decisión, como hoy los runs.

### API pública ([F.2](../integraciones/estudio-2026-10-22.md#f2-api-pública-para-que-otro-sistema-use-nuestro-whatsapp))

| Operación propuesta | Detalle |
| --- | --- |
| `POST /api/public/v1/forms/{formKey}/sends` | Manda el formulario a un teléfono E.164, con datos prellenados por clave de pregunta y `Idempotency-Key`. Responde 202, con el envío y si salió como mensaje interactivo o como plantilla |
| `GET /api/public/v1/forms/{formKey}/versions/{n}/schema` | JSON Schema 2020-12 generado de las preguntas, con tipos, obligatorios, opciones o referencia al catálogo, límites y la etiqueta en el idioma original |
| `GET /api/public/v1/forms/{formKey}/responses?since=…&cursor=…` | Respuestas paginadas, con valores por clave estable, versión y estado |
| `GET /api/public/v1/responses/{id}` y `…/files/{fileId}` | Detalle y archivos por streaming, con el permiso revalidado |
| `POST /api/public/v1/webhooks` | Suscripciones a eventos (opcional, decisión 6) |

- **Autenticación:** primero, **claves de API por organización**, con permisos del catálogo existente y restricción opcional por formulario; se ven una sola vez, se guardan como hash, tienen prefijo visible, se revocan y registran su último uso. Después, OAuth 2.0 client credentials.
- **Límites:** el rate limiting de ASP.NET Core vive en memoria; con varios hosts hace falta **una cuota distribuida en Redis** (inferencia). Además, un tope de envíos de WhatsApp por clave y por día, por costo y por calidad del número.
- **Auditoría** de cada llamada que escribe, sin teléfonos completos ni valores.
- **Versionado:** un ADR nuevo que **acote el [ADR 0005](../../decisions/0005-sin-versionado-de-api-por-ahora.md) al front propio** y cree la superficie pública versionada desde el día uno con `Asp.Versioning`. La versión de la API cambia poco; la versión del formulario cambia en cada publicación y viaja como dato.

### MCP ([F.4](../integraciones/estudio-2026-10-22.md#f4-mcp))

- **Especificación del 28/7/2026:** con transporte HTTP, el servidor es un **resource server de OAuth 2.1**, publica *Protected Resource Metadata* (RFC 9728), exige PKCE y el parámetro `resource` (RFC 8707), valida que el token fue emitido para él y no acepta otros. El registro de clientes prefiere *Client ID Metadata Documents*; el registro dinámico queda deprecado.
- **Herramientas:** `inputSchema` y `outputSchema` en JSON Schema 2020-12, `structuredContent`, errores de negocio con `isError`, lista que varía según la autorización, sin sesión de protocolo y *annotations* no confiables. La especificación pide una persona en el circuito; los servidores validan entradas, controlan el acceso, limitan la tasa y sanean las salidas.
- **Cómo lo hacen otros:** Stripe (servidor remoto, OAuth y claves para agentes, **confirmación humana** para escrituras sensibles con una aprobación que vence a las 24 horas) y Jotform (servidor remoto con OAuth y herramientas para formularios y envíos).
- **En .NET:** `ModelContextProtocol.AspNetCore` 2.0.0 (28/7/2026) y 2.3.0 (20/10/2026), con `AddMcpServer().WithHttpTransport()`, `MapMcp()` y herramientas `[McpServerTool]` que reciben el `ClaimsPrincipal`. `MapMcp` es un endpoint fuera de MVC, y el repo prohíbe las Minimal APIs de negocio: necesita **una excepción por ADR**.
- **Cuándo cada uno:** MCP para asistentes que usan herramientas con el consentimiento de una persona; REST con webhooks para integraciones de sistema a sistema. Son dos puertas al mismo caso de uso. El ADR 0017 dejó afuera consumir un «MCP externo»; **exponer uno propio es una decisión nueva**.

### Conector HTTP genérico ([D.2](../integraciones/estudio-2026-10-22.md#d2-una-acción-http-genérica))

- Autenticación: ninguna, API key, Bearer, Basic y OAuth2 client credentials. El OAuth con autorización del usuario queda para los nativos.
- **Cuerpo con un mapeo declarativo, sin lenguaje de expresiones con código:** cada campo apunta a una ruta JSON, con constantes y formatos fijos. La respuesta se mapea de JSON Pointer a resultados tipados, y define qué cuenta como éxito.
- Importar OpenAPI para armar el mapeo (OpenAPI.NET 2.x lee 3.1); importar no resuelve todo: hay que editar y probar.
- **Idempotencia:** `Idempotency-Key` = hash de respuesta/evento/regla/acción, si el destino lo soporta. Si no, la operación es «no idempotente» y un resultado incierto va a revisión humana, salvo que la conexión defina una consulta de verificación.
- **SSRF:** hosts permitidos por conexión, solo HTTPS, sin seguir redirecciones, bloqueo de localhost, redes privadas, link-local y `169.254.169.254`, validación de las IP resueltas (DNS rebinding) y topes de tiempo y tamaño.
- Reintentos solo ante errores transitorios y si la operación es idempotente o verificable. Registro por intento con hashes; cuerpos redactados o cifrados con retención; nunca secretos. Un botón «Probar» cuya respuesta alimenta el mapeo.

### Cómo se combinan las tres piezas ([F.5](../integraciones/estudio-2026-10-22.md#f5-cómo-combinar-las-tres-piezas-sin-duplicar-lógica))

Un caso de uso de Application por operación, con la autorización hecha en Application para un **principal** (persona, clave de API o cliente OAuth), y adaptadores finos: el front MVC, la API pública MVC y las herramientas MCP llaman a los mismos servicios (mandar formulario, esquema, respuestas, archivos). Mismos permisos, misma semántica de idempotencia, **un solo generador del JSON Schema** por versión (para `/schema`, los webhooks, el `inputSchema` de MCP y el mapeo del genérico) y un único registro de operaciones de integración, con actor, canal, destino y resultado.

### Riesgos y cómo cubrirlos ([E.2](../integraciones/estudio-2026-10-22.md#e2-riesgos-y-cómo-cubrirlos))

| Riesgo | Cobertura propuesta |
| --- | --- |
| Efecto legal o económico (una factura no se borra) | Confirmación con resumen, aprobación opcional por monto, homologación antes de producción y una acción explícita de nota de crédito |
| Emitir dos veces | Número de ARCA como clave, candado, consulta antes de reintentar y punto de venta exclusivo; `SELLER_SKU` en Mercado Libre; `external_reference` e idempotencia en Mercado Pago |
| Certificados y tokens | Anillo externo y Key Vault; almacén central con renovación serializada; «Requiere volver a conectar»; rotación; nunca la clave privada a un tercero |
| Homologación y producción mezcladas | Ambiente por conexión, certificados por ambiente, endpoints como dato y marca de «prueba» en los PDF de homologación |
| Cambios normativos y de API | RG 5616, 5614 y 5782, versiones del manual, User Products y precios de Meta: una persona responsable y pruebas en homologación con cada cambio |
| Permisos | Nuevos permisos para administrar conexiones y para usarlas en reglas (por ejemplo `integrations.manage` e `integrations.use`) |
| Auditoría | Quién configuró, quién confirmó y qué se mandó (hash y referencia), la respuesta del proveedor y cada intento, sin datos sensibles en logs |
| SSRF | Las reglas de OWASP del conector genérico |
| Datos personales hacia terceros | Cuerpo fino por defecto, retención limitada y documentación de qué sale hacia dónde |
| Bloqueos y costos de terceros | Topes por clave y por día, y un tablero de errores por conexión |
| ARCA caído | Cola durable, aviso a la persona y control de la ventana de fecha |

### Orden sugerido ([E.3](../integraciones/estudio-2026-10-22.md#e3-orden-sugerido))

1. **Base sin efectos legales:** módulo, conexiones, la acción «Ejecutar en un sistema externo» con reconciliación y revisión humana, y el **webhook saliente firmado** como primera operación.
2. **ARCA en homologación:** solo **factura C**, numeración serializada, reconciliación, PDF fiscal con QR, nota de crédito C y el estándar «Emitir factura C». Pruebas con arcasim, homologación real y varios hosts.
3. **ARCA en producción:** piloto con una CUIT y responsable inscripto (A y B, IVA por alícuota, RG 5614 y condición frente al IVA).
4. **Conector HTTP genérico;** después, importar OpenAPI.
5. **Mercado Pago:** link de pago y webhook entrante con el evento «Pago aprobado».
6. **API pública,** con el ADR de versionado y las **plantillas de Meta** para mandar fuera de la ventana.
7. **Servidor MCP:** primero lectura y «proponer» con confirmación humana.
8. **Mercado Libre,** el más variable.

Alternativa: si la prioridad comercial es facturar, invertir 1 y 2. La base sale probada, pero el primer conector es el de mayor riesgo legal.

### Las 13 decisiones abiertas ([E.4](../integraciones/estudio-2026-10-22.md#e4-decisiones-que-tendría-que-tomar-el-usuario))

| # | Decisión | Opciones | Recomendación del estudio |
| --- | --- | --- | --- |
| 1 | Acceso a ARCA | (a) delegación a la CUIT de la plataforma; (b) certificado propio de cada empresa; (c) ambos según la instalación | (c): (b) para una instalación de una empresa y **(a) para el multitenant** |
| 2 | Alcance fiscal inicial | C; también A y B; exportación (E) | C primero, después A y B |
| 3 | Quién confirma la emisión | La persona al enviar el Flow; aprobación por rol; umbral de monto | Confirmación siempre, más aprobación opcional por monto |
| 4 | Punto de venta y ARCA caído | Punto de venta exclusivo; cola con aviso o CAEA de contingencia | Punto de venta exclusivo y cola |
| 5 | PDF fiscal | Diseño fijo de factura, aparte del diseño libre, y PDF al recibir | (El estudio lo plantea como necesidad, sin alternativas) |
| 6 | Webhooks | Como acción de regla o también como suscripciones por la API; cuerpo fino o completo | (Sin recomendación explícita; propone fino por defecto en F.1) |
| 7 | Atributos de Mercado Libre | Un formulario por categoría, o el asistente propone los obligatorios y se validan con `/items/validate` | (Sin recomendación explícita) |
| 8 | API pública | Claves con permisos por formulario (MVP) u OAuth client credentials; `/api/public/v1` con `Asp.Versioning`; topes por clave y día | (Propone claves primero en F.2) |
| 9 | Plantillas de Meta | Una por formulario o una genérica; categoría; quién paga; registro del opt-in | (Sin recomendación explícita) |
| 10 | MCP | Si se expone y cuándo; acciones con efecto legal por MCP, o solo lectura y «proponer» con confirmación | Lo segundo, como Stripe |
| 11 | Eventos externos como disparadores de reglas | «Pago aprobado», «Factura emitida», además de Recibida, Aprobada y Rechazada | (Sin recomendación explícita) |
| 12 | Retención de cuerpos y respuestas de integraciones | Tienen datos personales y fiscales | (Sin recomendación explícita) |
| 13 | ADR nuevos | Módulo `Integrations` y su puerto; API pública versionada; excepción de `MapMcp`; credenciales de integraciones que extienden el 0014 | Los cuatro |

### Lo que el estudio no pudo confirmar ([lista completa](../integraciones/estudio-2026-10-22.md#lo-que-no-pude-confirmar))

- **ARCA:** vigencia del certificado, tamaño de clave y algoritmo de firma; que una delegación nueva obligue a pedir otro TA; que varios hosts compartan token y sign; los límites de tasa; el nombre del punto de venta para responsable inscripto; si aceptar cada delegación se puede automatizar; los requisitos completos del PDF, el alcance de la RG 5614 a la factura C y los umbrales de la RG 5700; el dominio definitivo de los endpoints.
- **Mercado Libre:** la duración exacta del access token; si `POST /items` acepta clave de idempotencia.
- **Mercado Pago:** la duración del access token; si `X-Idempotency-Key` aplica a preferencias.
- **Meta y MCP:** si una plantilla con botón FLOW puede apuntar a otro Flow al enviarla; la alineación del SDK de C# con la especificación (salió de un pull request de terceros) y su soporte de autorización, visto solo por nombres de archivos.

### Tableros, dependencias y cierre

- **Tableros:** ninguno.
- **Dependencias:** la biblioteca de Documentos; el motor de acciones de la Etapa 4; la etapa de plantillas de Meta para la API pública; los cuatro ADR; el catálogo Clientes con condición frente al IVA (campos extra de E2).
- **Cierre:** el estudio no define criterios por fase. Lo único escrito es la fase 2 de E.3: arcasim, homologación real y varios hosts. Por el [ADR 0018](../../decisions/0018-robustez-proporcional-al-efecto.md), cada efecto fuera del sistema lleva recuperación durable, estado incierto y pruebas de varios hosts.

### Qué cambia en multitenant

- **ARCA por organización, con la recomendación (a):** la plataforma recibe la delegación de cada organización y responde como su representante. **Para evaluar:**
  - aceptar la designación y autorizar el computador fiscal se repite por organización, y puede ser manual (inferencia del estudio);
  - un solo TA por servicio con el certificado de la plataforma, compartido por todas las organizaciones, que lista las CUIT representadas; cada delegación nueva pide un TA nuevo, respetando los 2 minutos (inferencia del estudio);
  - el candado de numeración por CUIT, punto de venta y tipo ya separa a las organizaciones; el punto de venta exclusivo es de cada organización;
  - la responsabilidad de la plataforma como representante es una cuestión legal que el estudio menciona y no resuelve.
- **Credenciales por organización:** conexiones, tokens OAuth y claves de terceros son de cada organización; el certificado de la plataforma, de la plataforma. Si el multitenant tiene un anillo externo como el ADR 0014: **[sin verificar]**.
- **Mercado Libre y Mercado Pago:** la aplicación (`client_id`) es de la plataforma y los tokens, de cada organización. **Para evaluar:** el 429 se cuenta **por client ID**, así que el volumen de una organización afecta a todas, y **un bloqueo de la aplicación por exceso de 4xx frena Mercado Libre y Mercado Pago para todas las organizaciones**. Hacen falta topes por organización y cuidar los errores de cada una.
- **Plantillas de Meta:** el multitenant ya reconoce que no tiene plantillas del sistema en la WABA de cada organización (decisión 26 y «después del bloque» de su hoja de ruta); la ADR 0038 dice que las plantillas se crean desde la plataforma y Meta las aprueba. Cada organización paga su WABA. **Para evaluar:** el opt-in por contacto, que la ADR 0038 ya tiene para promociones.
- **API pública:** las claves son por organización, y la organización sale de la clave, como hoy sale del claim `tenant_id`. Cuotas distribuidas por clave y por organización.
- **Webhooks:** endpoints y secretos por organización; las entregas durables se crean y despachan con el alcance de la organización.
- **MCP:** el multitenant tiene un solo servidor OIDC con issuer fijo. **Para evaluar:** si hace de servidor de autorización de MCP, con la organización en el token. El multitenant también prohíbe las Minimal APIs de negocio, así que `MapMcp` necesita la misma excepción.
- **Costos por organización:** Meta, a cargo de cada organización (ADR 0038); las comisiones de Mercado Pago y Mercado Libre son de cada cuenta. La IA que propone «Emitir factura» entra en el tope mensual de la organización.
- **Personas B2C:** cobrar con link o mandar una factura a un cliente son recorridos con personas. **Para evaluar:** si el destinatario puede ser una persona B2C y si el resultado (factura, pago) es un dato compartido persona↔organización.

---

## F7 · Otros pendientes

Fuentes: [índice de planes](../README.md), [formularios después de la Etapa 5](../formularios/README.md#después-de-la-etapa-5) y los planes propios que se enlazan. La última columna dice si el pendiente ya figura en la [hoja de ruta del multitenant](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/plans/2026-10-12-formularios-dinamicos-hoja-de-ruta.md#después-del-bloque).

### Del producto de formularios

| Pendiente | Estado y alcance en ArquitecturaBase | En el multitenant |
| --- | --- | --- |
| **Copiar formularios** | Pendiente de tablero y decisión. Asignaría identidades y prefijo nuevos, sin numeración ni vínculos con Meta. Dar de baja, reactivar y eliminar ya están implementados | «Copiar y eliminar formularios, y deprecar Flows viejos», después del bloque |
| **Corregir una respuesta** | Una respuesta nueva que reemplaza a la anterior. Sin tablero ni decisión | «Corregir una respuesta con una respuesta sucesora», después del bloque |
| **Grupos y remitos con líneas** | Sin tablero ni decisión. La [demo de datos](../formularios-modelo-demo/README.md), que no es una migración, modela un «Remito de entrega» con un grupo «Productos entregados» de 1 a 20 filas y un `RowId` por fila en los valores | «Grupos y remitos con líneas», después del bloque |
| **Descargar todas las fotos juntas** desde el detalle | Sin tablero ni decisión | No figura **[sin verificar en otros documentos del multitenant]** |
| **Catálogo con una consulta (Query/K5)** | Solo si una lista no se puede armar con filtros. Requisitos fijados en el [ADR 0019](../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md): vistas inventariadas, parser con AST, credencial restringida, único parámetro `:usuario_actual`, permiso propio, aprobación por hash y revalidación tras cada migración | Su E13 lo deja fuera |
| **Intercambio cifrado con Meta** | Búsqueda en vivo, listas dependientes y listas largas ([F4](#f4--catálogos-después-de-e2)) | Después del bloque |
| **Borrador de formulario armado por la IA** | «A partir de los pedidos de formularios que faltan». Documentos lo retoma desde la bandeja (DOC6); el pedido original, desde Formularios que faltan, no tiene tablero propio | Después del bloque |
| **Cargar respuestas desde la web e importarlas desde Excel** | Pendientes, con tableros propios | Después del bloque |
| **Retención de respuestas** | Hoy registros, valores, recibos y versiones se conservan sin plazo. Decidir por negocio retención o anonimización antes de producción; no heredan los 90 días de WhatsApp | Decidido allá: sin plazo mientras exista la organización (decisión 23) |
| **Varios países y más idiomas** | «Quedan para después» | — |
| **Guía para quitar el módulo** | Falta `docs/guides/quitar-formularios.md`, como las de WhatsApp e IA | Su E12 prueba el retiro una vez |
| **Deuda menor de la Etapa 1** | Se arregla al tocar esos archivos (lista en el índice) | Son lecciones, no tareas |

### De WhatsApp, IA y archivos, que tocan al producto

| Pendiente | Estado y alcance | Relación con el resto |
| --- | --- | --- |
| [Sugerencias de texto con revisión humana](../2026-10-03-sugerencias-ia-whatsapp.md) | Sin implementar. En Atención manual, el operador pide una respuesta de texto, la revisa con la información usada, la pasa al borrador y la manda él. Canal del motor único, con el historial del chat y `business.information` como capacidades de lectura. Pendientes: permiso de uso, presupuesto del canal, casos de evaluación, presentación de los hechos y tablero junto a H2 | AS4 eligió el mismo borrador para el operador; ver [F1](#f1--casos-del-asistente) |
| [WhatsApp después del chat](../2026-10-01-bandeja-whatsapp.md) | Propuestas: Contactos (de solo lectura, permiso `whatsapp.contacts.read`), Plantillas de Meta (`whatsapp.templates.manage`), conexión del canal en pantalla, la guía de proyectos derivados y la prueba con dos instalaciones | La gestión de plantillas es requisito de la API pública ([F6](#f6--integraciones-partes-2-y-3)). En el multitenant, contactos, plantillas y alta embebida son de su E11 |
| [Archivos en Azure Blob](../2026-10-02-almacenamiento-de-archivos.md) | Hecho el primer consumidor. Quedan health check, antivirus, respaldo y restauración, retención y purga, fondo del chat, documentos desde la web, transporte de volumen y operación y costos | Documentos usa la copia privada; el antivirus antes de producción vale para los documentos leídos |
| [Asistente de Inicio: capacidad y retención](../2026-10-04-panel-conversaciones-inicio.md) | Ventanas de retención, purgas acotadas, ensayo de carga (10.000 a 1.000.000 de turnos, 10 a 100 sesiones contra dos hosts) y calibración de cuotas. Con el ADR 0020, todos los canales comparten cuotas y operación | Casos y Documentos suman llamadas al mismo motor: la calibración tiene que contarlas |
| Deuda menor del backend | `business.information` presenta preguntas sin respuesta y textos anteriores como hechos; comentarios que hablan del bot retirado | Es requisito de la pestaña Conocimiento de Casos |

### Fuera del producto de formularios

Figuran en el índice y no son parte de lo que se traspasa: [solicitudes de ingreso](../2026-09-30-solicitudes-de-ingreso.md) (propuesta, con un error actual a corregir antes), la [unificación de mensajes del front](../../../../ArquitecturaBaseFront/docs/plans/2026-09-30-unificacion-mensajes.md), la deuda menor del front y del arnés, y las mejoras del proceso ofrecidas sin respuesta (chequeo automático del tamaño de las guías por carpeta, por debajo de 30 KB, y una lista de chequeo previa al cierre con los errores que se repiten en los reviews). El multitenant tiene su propio modelo de identidad y sus propias reglas.

---

## F8 · Verificación y producción

Fuente: [verificación y preparación de producción](../verificacion-y-produccion.md): **39 casillas abiertas** al 2026-10-23.

### El piloto único con Meta (9 casillas)

Primero se corrige el [review de E2](../../reviews/2026-10-23-code-review-catalogos-e2.md); después, una sola prueba guiada con Aspire, túnel y proveedores reales. Lo decidió el usuario el 2026-10-22: junta los pilotos pendientes de dar de baja, reactivar y eliminar, el menú y el detalle, los contactos simultáneos y los catálogos E1 y E2.
1. Zonas y Proveedores ficticios con CUIT, Zona y otro campo; pegar opciones e importar el XLSX, revisando altas, cambios, bajas y errores antes de aplicar; definir qué título y descripción viajan.
2. Dos personas con zonas distintas: cada una ve solo sus opciones; a una sin el dato se le bloquea el envío con el motivo, sin mandar la lista completa; la muestra por destinatario y los avisos antes de publicar.
3. Dos o tres contactos simultáneos, en Asistente y en Atención manual: cada número recibe solo su propuesta y su contexto, y un turno lento no detiene a los otros.
4. Un `nfm_reply` real de CheckboxGroup: el array de identidades, el orden que entrega Meta y la diferencia entre `[]` y la clave ausente. **No se afirma que Meta devuelva el orden de selección.**
5. Respuestas con varias opciones y archivos, el bloque Por aprobar, D2 y pantalla completa, y la cuenta que solo decide.
6. Campos sellados en planilla, detalle, Excel y PDF, también después de editar el catálogo; Columnas y «Solo las columnas visibles».
7. Un catálogo decimal: texto y valor histórico, el límite de 15 cifras de Excel y la reimportación exacta.
8. Dar de baja con un envío abierto, responderlo antes de vencer y comprobar la deprecación al vencer el último envío.
9. Reactivar (vuelve a Activos solo al aprobar Meta) y eliminar un formulario sin respuestas.

### Producción

- **Despliegue (3):** ejecutar y validar el primer despliegue real con la guía de Azure dev; la lista antes del primer despliegue (Redis privado con TLS, prefijos, secretos, issuer, redirects y certificados); **WhatsApp en producción** (número y PIN, plantillas, webhook propio con HTTPS, secreto de la app, monitoreo del token y del costo, y el control de cambio de identidad: un número reciclado llega con otro BSUID y no traslada la cuenta).
- **Inteligencia artificial (5):** verificar y calificar Anthropic con credenciales propias; revisar egress y retención de las cuentas de OpenAI y Anthropic antes de usar datos reales (`store:false` no es retención cero); restaurar base, anillo e `InstallationId` en un ambiente aislado; probar una segunda instalación independiente; en el despliegue, anillo en Key Vault, alertas, OpenTelemetry, dos réplicas y apagado con turnos en curso.
- **Privacidad de la instalación de prueba (11):** borrado y anonimización probados, plazos de retención de códigos, enlaces y auditorías, revisión profesional, política enlazada, canal humano de contacto, campos del webhook, opt-in del código de vinculación, exclusión de invitaciones, confirmación del contacto, límite diario de invitaciones y lo que ve `users.read`.
- **Verificación manual (4):** el refactor de acceso y atención con teléfono real; Google, SMTP e invitaciones reales; arranque con una base nueva; caminos de salida de roles.
- **Adopción visual (7):** unificación de mensajes, sincronizar biblioteca y lienzo, tableros responsive pendientes, teclado en `main`, accesibilidad móvil, enlaces reales en `RowActions` y decisiones heredadas por confirmar.
- **Del índice, además:** la retención de las respuestas (ver [F7](#f7--otros-pendientes)).

### Qué cambia en multitenant

- **No se traslada tal cual:** es la verificación de una instalación de ArquitecturaBase. El multitenant tiene sus propias etapas de endurecimiento (E9) y despliegue (E10), y sus reglas de datos personales.
- **Sirve como lista de lo que solo prueba un piloto real:** el orden y el vacío de un `nfm_reply` de CheckboxGroup, el bloqueo por dato faltante en el celular, los contactos simultáneos y la deprecación de Flows. Ninguna prueba automática con Meta simulado lo acredita.
- **WhatsApp en producción es por organización** con el alta embebida de la E11; el riesgo del número reciclado con otro BSUID vale para cada número de cada organización.
- **Retención de la cuenta de IA:** vale para la clave de la plataforma y para cada clave propia de una organización.
- **La prueba de dos instalaciones** de ArquitecturaBase es, en el multitenant, la prueba de aislamiento entre organizaciones (el fuzz de la E9 sobre todas las rutas, según su hoja de ruta). **Para evaluar** que cubra también los workers de Casos y Documentos.

---

## Huecos de este anexo

- **No escrito en el repo, solo en el chat guía:** la recomendación de hacer Casos antes que Documentos. Está marcada **[sin verificar]**.
- **Riesgos técnicos sin fuente:** si los Flows de Meta permiten precargar un archivo en una pregunta de documento (el corazón de DOC3) y si las claves del intercambio cifrado de Meta son por número.
- **Valores que ninguna fuente fija:** el tope del historial de Casos, el largo máximo y el tope diario de respuestas, los topes de costo y de páginas de Documentos, y cómo se mide «se parecen».
- **Del multitenant:** no encontré un equivalente de «Información del negocio» ni de un anillo externo de credenciales como el ADR 0014; no busqué más allá de sus documentos.
- **Inconsistencia entre fuentes:** conector frente a VPN para llegar a las bases de los clientes (hoja de ruta de catálogos frente al índice de planes).
- **No relevado acá:** el detalle de lo construido y de los 39 puntos del review de E2, que están en los anexos [A](anexo-a-backend-desde-17-10.md), [B](anexo-b-front-desde-17-10.md) y [E](anexo-e-reviews-y-lecciones.md).
