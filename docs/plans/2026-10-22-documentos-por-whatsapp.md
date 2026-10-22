# Documentos por WhatsApp: biblioteca de formularios estándar y lectura con IA

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
2. Los tableros, con el chat de diseño y en la maqueta:
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
