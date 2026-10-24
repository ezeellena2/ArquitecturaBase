# 07 · Adaptación al multitenant, etapa por etapa

Este capítulo dice, para cada etapa del bloque de formularios del multitenant (E12 a E17, el asistente y lo de después), tres cosas:
- qué hizo ArquitecturaBase desde el 17/10;
- qué conviene sumar o cambiar en su spec;
- qué tiene que **evaluar técnicamente** el multitenant con su modelo de organizaciones, plataforma y personas.

**El multitenant decide.** Su condición sigue valiendo: ninguna decisión de ArquitecturaBase pasa por encima de su `AGENTS.md` ni de `docs/rules/`. Lo de abajo es material para que su guía arme las preguntas al usuario y actualice sus specs.

## Antes de cualquier etapa

1. **Adoptar las reglas del [04](04-reglas-del-usuario.md)** en su guía de diseño y en sus fichas:
   - botones sin explicaciones;
   - sin «Cambios sin guardar» ni números de revisión al lado de Guardar;
   - acciones a la derecha;
   - validar al guardar;
   - idioma escrito en el del menú, sin selector.

   Conviene revisar si su guía visual tiene la frase que causó el error en ArquitecturaBase: «el botón explica el estado».
2. **Sumar la lista de control del [05](05-calidad.md)** a su skill `/puerta` o a la ficha de cierre.
3. **Exigir ejemplos HTTP exportados** para todo contrato que consume el front, con su consumidor en zod estricto. Ya lo dicen: hay que cumplirlo sin excepciones, también en el asistente.

## E12 · Formularios: crear, publicar, mandar y recibir

**Qué sumó ArquitecturaBase:**
- **Idioma original y traducción opcional** (19/10), con la regla del 23/10: se escribe en el idioma del menú, sin selector en pantalla, y la traducción se carga cambiando el menú.
- **La pregunta «elegir varias»:**
  - en WhatsApp es un `CheckboxGroup` de 1 a 20 opciones, con mínimo y máximo, en el perfil `flow-static-7.3-r9`;
  - el prellenado acepta los perfiles r8 y r9;
  - la captura guarda una fila por opción, con su posición y en el orden recibido.
- **Error ya pagado en el piloto del 20/10:** abrir un Manual sin prellenado mandaba `prefill_FORM:{}`. Meta lo acepta, pero el formulario no abre; hay que omitirlo.
- **Ciclo de vida:**
  - dar de baja conserva la versión activa y los envíos abiertos;
  - reactivar es publicar una versión nueva;
  - eliminar es lógico y solo sin respuestas ni envíos abiertos;
  - la deprecación durable de los Flows ocurre al vencer el último envío.

  La hoja de ruta lo tenía para después.

**Qué evaluar:**
- **El ciclo de vida:** si entra al bloque.
- **La deprecación por WABA:** en el multitenant es por organización (E11), así que la operación durable y el token tienen que ser de esa organización.
- **El idioma:** la regla del menú, contra la decisión 17 («el idioma de las pantallas del sistema no cambia»).
- **«Elegir varias»:** si se suma a la E12 o a la E13.

## E13 · Catálogos

**Qué sumó ArquitecturaBase** (detalle en los anexos [A](anexo-a-backend-desde-17-10.md), [B](anexo-b-front-desde-17-10.md) y [C](anexo-c-reglas-vigentes.md)):
- **E1 · tipo de valor:** texto, entero, decimal o fecha. Se elige al crear y se guarda canónico.
- **E1 · carga de una vez:**
  - pegar e importar XLSX o CSV, con revisión de altas, cambios, bajas y errores;
  - aplicar con el SHA-256 del archivo bajo lock;
  - confirmar si se dan de baja más del 25 % o si hay más del 10 % de filas con error;
  - topes de archivo (10 MiB, 64 columnas, 10.000 filas, ZIP de 100 MiB y celdas, contando las vacías con formato);
  - CSV en UTF-8;
  - las fórmulas usan su valor guardado;
  - exportar y bajar la plantilla.
- **E1 · capacidad:** hasta 10.000 opciones por catálogo, contando las inactivas.
- **E2 · campos extra por opción:**
  - tipos: texto, entero, decimal, fecha, sí/no y «elegir de otro catálogo»;
  - papel «Dato» o «Filtro», obligatorio y único;
  - se guardan como JSON en el catálogo y en cada opción, con un índice GIN.
- **E2 · presentación:** qué campos van al título (30) y a la descripción (300) en WhatsApp. Puede ser propia de una pregunta, y a Meta viaja solo lo elegido.
- **E2 · datos de las personas:** atributos como «Zona» por persona. Las preguntas filtran por ellos: «la zona del proveedor = la zona de quien lo recibe». A quien no tiene el dato no se le manda y se dice el motivo.
- **E2 · sellado:** la respuesta guarda los campos de la opción elegida tal como estaban.
- **Los 39 puntos del [review E2](../../reviews/2026-10-23-code-review-catalogos-e2.md):** son la lista de lo que **no** hay que repetir.

**Qué evaluar:**
- **El alcance:** E1 y E2 son mucho más que su E13 («a mano y con filtros»). Puede ser una E13 ampliada o una E13b.
- **Los datos de las personas en multitenant:**
  - ¿son atributos del miembro **en la organización**, que es lo natural porque «Zona» es de la organización y no de la persona? Se guardarían con `ITenantOwned` y clave por membresía, no por cuenta;
  - con varias empresas, ¿un dato puede depender de la empresa?
  - en B2C, ¿una persona cliente puede tener datos para filtrar?
- **La fuente «Miembros»** (su §4) contra la fuente «Filtros» de ArquitecturaBase, que usa usuarios y roles. Los filtros por persona de E2 se apoyan en los datos de las personas, no en los roles.
- **Los ids que viajan a Meta.** Su decisión C3 usa ids locales a la oferta, `o1…o200`, y es mejor que mandar ids internos. Hay que mantenerla también con «elegir varias» y con la presentación de E2.
- **El idioma de los catálogos:** la decisión 20 (solo catálogos del mismo idioma original) contra el respaldo por opción de ArquitecturaBase y la regla del 23/10.
- **El rendimiento con RLS:** 10.000 opciones por 20 campos en el esquema `tenant`, con el índice GIN `jsonb_path_ops` y la contención antes del tope de 201.

## E14 · Fotos y documentos

**Qué sumó ArquitecturaBase:** sin cambios en los archivos adjuntos. Se decidió, sin programar, **Documentos por WhatsApp** ([anexo F](anexo-f-planes-pendientes.md), [ADR 0022](../../decisions/0022-lectura-de-documentos-con-ia.md)): leer con IA lo que manda una cuenta vinculada y proponer el formulario prellenado.

**Qué evaluar:**
- **Documentos como etapa propia,** después de los catálogos con campos extra, que necesita el cruce del CUIT, y de los Casos del asistente, que necesita la ráfaga y el tope de acciones.
- **El costo de la lectura con IA:** entra en el tope mensual de la organización (ADR 0051).
- **La retención:** ArquitecturaBase borra a los 90 días lo descartado o sin clasificar, pero la D4 de su E11 conserva el contenido del chat mientras exista el contacto. Hay que decidirlo.
- **Las validaciones fijas** (CBU/CVU con la entidad del BCRA, CUIT/CUIL): van como datos de referencia, según su ficha `docs/rules/datos-de-referencia.md`. Nunca una lista en el código.

## E15 · Planilla, detalle y Excel

**Qué sumó ArquitecturaBase:**
- **MF1:** la pantalla «Formularios», con los activos y los dados de baja, la última respuesta y el bloque «Por aprobar». Quien solo aprueba ve solo eso.
- **D2 como vista rápida y DC0/DC1 como pantalla completa:** con flechas que recorren en el orden de la planilla y la vuelta a la misma fila y filtros.
- **Subcolumnas de los campos sellados de catálogo,** elegibles por separado: los «Dato» se ven por defecto y los «Filtro» se prenden en «Columnas».
- **Excel:** tipado en elección simple; en «elegir varias», valores unidos con `; `.
- **Filtros:** «alguna de» para «elegir varias».

**Qué evaluar:**
- **El bloque «Por aprobar»** contra su `IPendingItemSource` (ADR 0040). Puede ser la misma fuente.
- **La empresa en la planilla** (su decisión 24) junto a las subcolumnas de catálogo.

## E16 · Reglas, aprobación, PDF y avisos

**Qué sumó ArquitecturaBase:**
- **Sin cambios de fondo.** Las secciones de reglas, sugerencias y diseño del PDF son las que tenían «Cambios sin guardar», números de revisión y textos al lado de Guardar: no se copian así (punto 38).
- **El PDF muestra los campos «Dato»** de las opciones elegidas.

**Qué evaluar:** dibujar esas secciones con las reglas del [04](04-reglas-del-usuario.md).

## E17 · Biblioteca de plantillas de la plataforma

**Qué sumó ArquitecturaBase:** Documentos por WhatsApp propone una **biblioteca de formularios estándar** que viene con el producto. «Agregar» crea una copia editable que recuerda de qué estándar viene, como su decisión 2. Además, cada estándar trae su sección **«Lectura de documentos»**:
- «Cómo reconocerlo»;
- qué pregunta corresponde a cada dato estándar;
- las validaciones fijas.

El primero es «Comprobante de transferencia».

**Qué evaluar:**
- **Si la biblioteca de la E17 es la misma que la de formularios estándar.** Es lo natural: una plantilla de la plataforma puede traer su lectura de documentos.
- **Su decisión 14** («solo catálogos a mano»): es compatible con los campos extra.

## El asistente en WhatsApp

**Qué sumó ArquitecturaBase:**
- **Etapa 5 cerrada y en piloto.**
- **Latencia de 6,6 s,** antes 35 s:
  - señales de terminación al confirmar la UoW, que despiertan la entrada y la salida sin colas nuevas;
  - sondeo corto de 2 s solo mientras hay pendientes;
  - el webhook avisa a la captura.
- **Aislamiento:** el contexto se fija por conversación, con `ChannelConversationId` en el contexto neutral. Un operador interpreta varias conversaciones a la vez sin mezclarlas.
- **Casos del asistente, decididos sin programar** ([anexo F](anexo-f-planes-pendientes.md), [ADR 0021](../../decisions/0021-respuestas-redactadas-por-el-asistente.md)):
  - perfil, conocimiento, casos y límites **configurables por organización**;
  - respuestas redactadas dentro de esos límites;
  - un menú de acciones y «Cualquier otra cosa», que deriva;
  - historial de 24 h de la misma conversación;
  - ráfagas: espera 4 s sin mensajes nuevos, con un tope de 15 s;
  - hasta 3 acciones por turno;
  - agentes en una etapa posterior.

**Qué evaluar:**
- **Rehacer su resumen** (`asistente.md`) con los Casos. Hoy dice que lo que no es un formulario no se contesta.
- **Los costos:** respuestas redactadas, transcripción y lectura de documentos, todo dentro del tope por organización.
- **Medir la latencia desde el principio,** con el mismo arnés: de texto a envío y de audio a envío.

## Después del bloque

- **Integraciones** ([anexo F](anexo-f-planes-pendientes.md), [estudio](../integraciones/estudio-2026-10-22.md)):
  - **las fuentes:** ARCA (factura), Mercado Libre, Mercado Pago, webhooks, API pública y MCP;
  - **qué cambia en multitenant:** las credenciales y los certificados son de cada organización (el certificado de ARCA, el OAuth de Mercado Libre, la firma de los webhooks de Mercado Pago), y la API pública y el MCP necesitan identidad por organización. Las 13 decisiones de la sección E.4 del estudio siguen abiertas.
- **Conexión a la base de un cliente:**
  - en ArquitecturaBase se propuso una VPN sitio a sitio por cliente;
  - en un SaaS multitenant, una VPN por organización es pesada. Hay que evaluar alternativas que respeten «simple», como un agente del lado del cliente que empuje datos o una réplica de solo lectura expuesta de forma segura.
- **Tableros:**
  - uno automático por formulario, con tarjetas (DOC7-A), es por organización;
  - los que cruzan formularios o suman datos externos dependen de la conexión.
- **B2C:** los formularios para clientes y los documentos que manda un cliente sin cuenta vinculada. En ArquitecturaBase esos documentos no se leen; es el caso natural de B2C.
