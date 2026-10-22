# Documentos por WhatsApp: biblioteca de formularios estándar, lectura con IA y tableros

**Estado:** decidido el 2026-10-22 en lo principal, sin tableros y sin empezar. Es la etapa «fotos y documentos» que el usuario marcó como prioridad ([casos del asistente](2026-10-21-casos-del-asistente.md#etapa-siguiente-fotos-y-documentos)). Su orden frente a «Catálogos E2» y «Casos del asistente» lo define el usuario.

## Por qué

Hoy, cuando alguien manda por WhatsApp una foto o un PDF, se guarda como «archivo» sin contenido y la IA no lo ve. El producto tiene que poder recibir **cualquier tipo de documento**:
- comprobantes de transferencia, facturas, recibos y remitos;
- liquidaciones, DTE y constancias.

Con ese documento tiene que reconocer qué es, sacar sus datos y proponer el formulario que corresponde, ya completo, para que la persona lo valide. **Tiene que ser genérico:** ningún tipo de documento queda escrito en el código.

## Conceptos

### Biblioteca de formularios estándar

- **El producto trae formularios estándar** para los documentos comunes. Vienen como **datos de inicio** de cada versión, no como código. En una instalación de una empresa aparecen en la pantalla **«Biblioteca»**; en el multitenant, la misma biblioteca la comparten todas las organizaciones.
- **Cada estándar trae:**
  - sus preguntas, con sus tipos;
  - **«Cómo reconocerlo»**: la descripción para la IA;
  - sus **validaciones fijas**, que no dependen de la IA;
  - una pregunta de archivo para el documento original.
- **«Agregar desde la biblioteca» crea un formulario propio** de la empresa, que se puede editar como cualquier otro: nombre, preguntas, reglas y aprobaciones. Recuerda de qué estándar viene, con qué versión, y qué pregunta corresponde a cada dato del estándar.
- **Una empresa también puede activar la lectura de documentos en un formulario propio,** escribiendo su «Cómo reconocerlo». Se usa el mismo mecanismo.
- **Primer estándar: Comprobante de transferencia.** Sus preguntas:
  - banco o billetera de origen y de destino;
  - CBU, CVU o alias de origen y de destino;
  - titulares y CUIT/CUIL;
  - monto y moneda;
  - fecha y hora;
  - número de operación;
  - concepto;
  - el comprobante, como archivo.

  Sus validaciones fijas:
  - **el CBU y el CVU tienen dígitos verificadores, y sus primeros dígitos dicen el banco o la billetera,** según la tabla de entidades del BCRA, que se carga como datos;
  - el CUIT y el CUIL tienen dígito verificador;
  - montos y fechas en formato argentino.
- **Los siguientes estándares,** en etapas posteriores: factura (con el QR de AFIP), nota de crédito o débito, recibo, remito, ticket de gasto, orden de compra, presupuesto, cheque o eCheq, constancias, liquidaciones, carta de porte y DTE.

### El circuito

1. **Recibir:**
   - la imagen o el PDF se descarga y se guarda como **copia privada**, como los audios;
   - el texto que venga junto al archivo también se usa;
   - un PDF de varias páginas se lee entero.
2. **Clasificar,** entre los formularios con lectura de documentos que **esa persona puede completar**:
   - **seguro:** propone ese formulario;
   - **duda:** pregunta «¿Qué es este documento?», con botones para 2 o 3 formularios posibles;
   - **no lo reconoce o no hay formulario:** ofrece la lista que esa persona puede completar, más «Otro». «Otro» lo deja en la bandeja y lo anota en «Formularios que faltan».
3. **Extraer** los datos según las **preguntas del formulario elegido**, con su nivel de seguridad por dato.
4. **Validar sin IA:**
   - las validaciones fijas del estándar;
   - el cruce con catálogos (el CUIT en Proveedores o Clientes);
   - **los duplicados:** el mismo archivo, o la misma operación, monto y fecha.
5. **Proponer:** el formulario prellenado por WhatsApp, **con el documento adjunto** en su pregunta de archivo. Un dato que no se pudo leer queda vacío, y uno dudoso, marcado.
6. **Confirmar o corregir:** lo hace la persona. **En esta etapa, la IA nunca carga sola.**
7. **Organizar:** queda como una respuesta más, con sus reglas, aprobación y PDF.

### Casos a contemplar

| Situación | Qué hace |
| --- | --- |
| Foto movida, oscura o cortada | Pide reenviarla, diciendo qué falta |
| PDF con contraseña | Pide uno sin contraseña |
| Documento repetido | Avisa que ya se cargó, con cuándo y en qué respuesta |
| Datos que no están | Quedan vacíos y los completa la persona |
| Escrito a mano | Lo intenta y marca los datos dudosos |
| Otra moneda u otro idioma | Lo extrae igual |
| Documento sin texto | Funciona igual: el documento es el mensaje |
| La persona no puede completar ese formulario | Lo dice y deriva |
| Varias fotos de un mismo documento, o varios documentos en un mensaje | Después de esta etapa: necesita la ráfaga y las varias acciones de [casos del asistente](2026-10-21-casos-del-asistente.md) |

### La bandeja «Documentos recibidos»

- **Filtros:** tipo, estado (propuesto, confirmado, sin clasificar, descartado), persona y fecha.
- **El documento al lado de los datos extraídos,** para revisar, corregir y pasar al formulario.
- **Búsqueda por los datos:** monto, CUIT o número de operación.
- **Permisos propios** para ver y revisar documentos.

### Cómo mejora con el uso

- **Cada corrección queda registrada:** el dato propuesto, el confirmado y quién lo corrigió.
- **Las correcciones se suman como ejemplos** a «Cómo reconocerlo». No se entrena ningún modelo.
- **Se mide la precisión por formulario y por campo.** Es la base para la carga automática.

### Formulario nuevo armado por la IA

Para un documento que no encaja en ningún formulario:
1. **Queda en la bandeja como «Sin clasificar».**
2. **Alguien con permiso toca «Armar formulario con la IA».** La IA propone:
   - el nombre;
   - «Cómo reconocerlo»;
   - las preguntas con sus tipos, a partir de los datos del documento;
   - y, si alguno se parece, sugiere un estándar de la biblioteca.
3. **La persona revisa, ajusta y publica** como cualquier formulario. La IA propone la configuración y nunca la publica ni la aprueba, como ya dice el [ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md).
4. **El documento que lo originó se carga en el formulario nuevo,** y desde ahí los documentos parecidos se reconocen solos.

La bandeja **agrupa los documentos sin clasificar que se parecen** y avisa: «Llegaron 5 documentos parecidos. ¿Armamos un formulario?».

Retoma el pendiente «borrador de formulario armado por la IA a partir de los formularios que faltan» ([formularios](formularios/README.md#después-de-la-etapa-5)).

### Tableros por formulario

Cada formulario tiene una pestaña **«Tablero»**, armada sola a partir de sus preguntas y ajustable:

| Si el formulario tiene… | El tablero muestra… |
| --- | --- |
| Cualquier pregunta | Cuántas respuestas llegaron por día, semana o mes |
| Un número o un monto | Total, promedio, mínimo y máximo por período |
| Elegir una opción o un catálogo | El reparto por opción, por ejemplo transferencias por banco |
| Persona y estado | Quién cargó cuánto; aprobadas, rechazadas y pendientes |
| Fecha | La evolución en el tiempo |

- **Se puede ajustar:** sumar o quitar gráficos, y filtrar con la misma gramática de la planilla, por período, persona o estado.
- **No hay tableros en el código:** salen de los tipos de las preguntas, así que un formulario nuevo, también el armado por la IA, ya tiene el suyo.
- **Los permisos de la planilla:** quien no puede leer respuestas no ve el tablero.
- **Usa los valores con tipo** que ya guardan las respuestas (número, fecha, opción), para sumar y agrupar bien.
- **Más adelante:** tableros que cruzan formularios y que suman datos de sistemas externos, cuando exista la conexión con bases de clientes.

### La carga automática (etapa posterior)

Un modo por formulario, **«Cargar solo si es seguro»**, que carga sin preguntar cuando se cumplen las tres señales:
1. el tipo es claro;
2. las validaciones fijas de los campos elegidos dan bien y no es un duplicado;
3. el formulario tiene un historial suficiente de cargas sin correcciones en esos campos.

La persona recibe «Cargué tu transferencia de $150.000 (REC-000123). Si algo está mal, avisá», y en la bandeja queda marcada «Cargada automáticamente». **No se usa la seguridad que declara el modelo:** no es confiable por sí sola. Se usan las validaciones fijas y el historial medido.

## Decisiones tomadas

No se vuelven a preguntar.

- **Biblioteca:** formularios estándar como datos de inicio del producto. «Agregar» crea una copia propia y editable, que recuerda de qué estándar viene. Sirve para una instalación de una empresa y para el multitenant.
- **Primer estándar:** Comprobante de transferencia.
- **La bandeja «Documentos recibidos»** entra en la primera etapa.
- **La carga automática viene después, con datos:** la primera etapa siempre propone y una persona confirma.
- **Genérico:** ningún tipo de documento en el código. Un estándar nuevo es configuración.
- **Formulario nuevo armado por la IA:** nace en la bandeja, sobre un documento sin clasificar, con el aviso de documentos parecidos agrupados. La IA propone y una persona publica.
- **Tableros:** uno por formulario, automático a partir de sus preguntas y ajustable. Los que cruzan formularios o suman datos externos vienen después.

**Esta es la parte 1 de tres proyectos.** La parte 2, formularios conectados a sistemas externos (ARCA, Mercado Libre y cualquier API), y la parte 3, que otros sistemas reciban nuestros datos (webhooks, API pública y MCP), tienen su propio plan después de la investigación del 2026-10-22.

## Cómo encaja

- **El [motor de IA](../decisions/0020-motor-unico-de-ia-con-capacidades.md):**
  - el documento se vuelve legible para el modelo con un propósito propio, como hoy la transcripción del audio;
  - clasificar y proponer los datos son **capacidades** de BusinessForms, de lectura y propuesta;
  - ejecuta el módulo dueño.
- **[Casos del asistente](2026-10-21-casos-del-asistente.md):** «llega un documento» es un tipo de entrada de los casos, que ya quedó previsto. Sin casos del asistente, el circuito arranca igual, desde la recepción del documento.
- **Archivos privados** ([etapa 2B](../features/formularios.md#fotos-y-documentos--implementación-local-2b-2026-10-15)): la copia, la verificación por contenido, los topes y la retención.
- **El prellenado tiene que poder proponer un archivo:** hoy no lo hace ([formularios](../features/formularios.md)).

## Hace falta un ADR

Antes de programar, uno que fije:
- qué documentos y qué datos se mandan al proveedor de IA, y cuánto los guarda;
- cuánto tiempo guardamos nosotros cada documento y su lectura;
- que una persona siempre confirma, y en qué condiciones se habilitaría después la carga automática.

## Cómo se encara

1. El ADR.
2. Los tableros de diseño, con el chat de diseño y en la maqueta (incluidos el formulario armado por la IA y la pestaña «Tablero»):
   - la biblioteca;
   - el formulario con lectura de documentos;
   - los tres casos en el celular;
   - la bandeja.
3. La evaluación de impacto contra el código, back y front.
4. La spec técnica, con el modelo de datos, los contratos y el árbol de archivos.

## Cómo se cierra

**Piloto real con Meta,** con comprobantes reales de varios bancos y billeteras (Galicia, Santander, Nación, Mercado Pago, Brubank y otros):
- reconocimiento seguro, con duda y sin reconocer;
- un CBU con el dígito verificador mal leído, que no pasa;
- un duplicado;
- una foto movida;
- la bandeja y las correcciones registradas.
