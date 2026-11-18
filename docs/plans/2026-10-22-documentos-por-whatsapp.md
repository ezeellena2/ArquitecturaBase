# Documentos por WhatsApp: biblioteca de formularios estándar, lectura con IA y tableros

**Estado (2026-11-18): programado entero, en back y front; falta la prueba.** Lo decidió el usuario el 2026-10-22, con el
[ADR 0022](../decisions/0022-lectura-de-documentos-con-ia.md) aceptado y los tableros DOC5-A y DOC7-A elegidos. El 2026-11-11 lo
sumó entero a la tanda de la prueba integral, junto con la Etapa 3 de la IA, y ese día eligió también la
[pregunta «Ubicación»](#la-pregunta-ubicación) y lo que el alcance dejaba afuera
([W3 y varias cargas desde un Excel](#ubicación-y-contacto-por-whatsapp-w3-y-varias-cargas-desde-un-excel)). Todo quedó programado
del 2026-11-11 al 2026-11-14 ([Hecho](#hecho)) y calificado con OpenAI real. La exportación conjunta de los ejemplos HTTP se hizo el
2026-11-17.

- **Lo que hace el código** está en [formularios](../features/formularios.md), [WhatsApp](../features/whatsapp.md) e
  [inteligencia artificial](../features/inteligencia-artificial.md), y lo técnico en sus `*-servicios.md`. Las reglas de la bandeja
  «Documentos recibidos» todavía no tienen sección propia en formularios: hasta entonces están acá, en la
  [Parte B](#parte-b--la-bandeja-documentos-recibidos-doc5-a-y-doc6) y en [las filas en la bandeja](#las-filas-en-la-bandeja).
- **El texto completo** del plan (la evaluación de impacto, el modelo de datos, los contratos, el árbol de archivos, las tareas, los
  riesgos y el relato de cada tarea) quedó en Git: `git show c067117c:docs/plans/2026-10-22-documentos-por-whatsapp.md`. Se recortó el
  2026-11-18, en el barrido de la documentación.
- **Se retira después de la prueba integral,** cuando la bandeja tenga su sección en formularios y lo de [Lo que falta](#lo-que-falta)
  esté hecho o tenga otro dueño.

## Lo que falta

### Para cerrar el desarrollo

1. **Los lectores del front para las capturas nuevas.** La exportación del 2026-11-17 trajo `forms-documents-http` y
   `forms-dashboard-http`, pero el front sólo las califica por inventario (`src/test/httpCohort.ts` y `src/test/AGENTS.md` del front).
   Faltan los `*HttpContract.test.ts` de la bandeja, de las propuestas de DOC6 y del tablero; `entriesLocationHttpContract.test.ts`
   (los cuatro cuerpos de ubicación de `forms-entries-http`), y los lectores de `forms-http/document-reading`,
   `forms-http/location-contact`, de los hilos `document-thread`, `document-why-thread`, `contact-thread` y `spreadsheet-loads-thread`
   (`whatsapp-audio-http`), de `es/thread-location-*` y `es/thread-shared-*` (`whatsapp-forms-http`) y de `detail-shared-whatsapp-es`,
   `detail-location-whatsapp-es` y `rows-*` (`forms-responses-http`). Inicio ya está cubierto (`assistantHttpContract.test.ts`).
2. **La sección de la bandeja en [formularios](../features/formularios.md),** con lo de la Parte B y las filas, para que este plan deje
   de ser su única fuente.

### La prueba con Meta y a mano

Va en la prueba integral. Cada paso tiene que hacer lo que dicen las reglas; lo que falle se arregla antes de retirar el plan.

**Con Meta real** (lo que no se pudo comprobar sin Meta):
- La vista oficial del Flow `flow-static-7.3-r14`: el aviso de arriba, la ayuda y los errores dinámicos y la gemela de la pregunta del
  documento. Si Meta no los acepta, las marcas van en un texto al principio del formulario.
- El reenvío de W3 con el mismo `flow_token` del envío. Si Meta no lo acepta, el reenvío pasa a ser otro envío que cierra el primero.
- El pie («{formulario} · prellenado») de una carga por fila. Si Meta no lo muestra, el formulario va en el cuerpo.
- El `location_request_message` de W1-A y la ubicación que vuelve, del operador y del asistente.

**Por WhatsApp, el piloto** con comprobantes reales de varios bancos y billeteras (Galicia, Santander, Nación, Mercado Pago, Brubank y
otros):
- reconocimiento seguro, con duda y sin reconocer, con el Flow prellenado, sus marcas y el comprobante ya adjunto;
- un CBU con el dígito verificador mal leído, que no pasa y vuelve el formulario con el motivo;
- un repetido, una foto movida, un número sin cuenta y una cuenta sin permiso, que se deriva;
- una ubicación y un contacto compartidos con un formulario abierto, y una planilla de siete filas (el aviso, tres cargas y el resto
  para el equipo);
- la bandeja con las correcciones registradas, y los cupos de lectura (10 por persona y 50 por instalación) y el tope de 50 filas,
  que el piloto calibra.

**En la app**, con «Comprobante de transferencia» agregado y publicado, una cuenta que revisa documentos y otra que sólo carga:
1. **Biblioteca:** buscar «transferencia», filtrar «Próximamente», agregar el estándar y ver su carpeta y su catálogo «Bancos y
   billeteras».
2. **Editor:** «Lectura de documentos» con «Cómo reconocerlo», las preguntas atadas con candado y un cruce con un catálogo; publicar.
   Con algunas cargas, «Cómo viene leyendo» y «Ver los ejemplos», con y sin «Ver respuestas».
3. **Bandeja:** el número del menú; los filtros por estado, formulario, persona y fecha; buscar por monto, CUIT y número de operación;
   una foto con zoom y giro y un PDF en el visor; corregir y pasar al formulario (la respuesta dice «Documento» y quién lo revisó);
   pasar uno repetido, que pide confirmar; cambiar de formulario; descartar y volver a abrir, con la fecha de borrado; una fila de
   planilla («Fila 5», «La fila», «Descargar la planilla») y una que espera a su persona, con las acciones apagadas y su motivo. Una
   cuenta sin «Ver documentos recibidos» no ve la entrada.
4. **DOC6:** con tres sin clasificar parecidos, el aviso arriba de la bandeja; «Armar formulario con la IA» desde el aviso y desde el
   panel; editar y «Crear borrador»; al publicarlo, sus documentos vuelven propuestos.
5. **Tablero:** Día, Semana y Mes; los números contra el período anterior con un rango de fechas; el reparto por banco; agregar y
   quitar tarjetas, «Ver como tabla» y «Volver al automático»; el filtro compartido con la planilla; la cuenta que sólo carga ve sólo
   lo suyo; en el celular.
6. **Inicio:** con el clip, una foto, un PDF de varias páginas y un Word: «Leyendo el documento…», la burbuja con sus páginas y su
   tamaño, la propuesta con «Lee documentos» y «Crear borrador» con la lectura prendida. Un `.doc` y un PDF con contraseña dicen por
   qué no se leen.
7. **Ubicación:** completar «Registro de visita» en un celular (permitir y denegar el permiso) y en una computadora (precisión baja);
   ver el punto en la planilla y en el detalle; abrir el Excel y el PDF. Por WhatsApp (W1-A): mandar un texto en vez de la ubicación,
   mandarla dos veces, dejar vencer el envío, la opcional con «Seguir sin ubicación», dos pedidos abiertos a la misma persona y en
   inglés.

### Propuestas para después (sin programar)

- **Billeteras en «Bancos y billeteras».** La tabla trae sólo los 59 bancos del registro de cheques del BCRA: los proveedores de CVU
  (los asigna COELSA) no tienen una lista oficial leída, así que un comprobante con un CVU deja el banco vacío y marcado. Falta la
  fuente, sumarlos a `bank-entities.json` y decidir si una copia ya agregada recibe las opciones nuevas («Agregar» crea el catálogo
  sólo si no existe).
- **El monto en «no se le puede mandar».** DOC4 dibuja «Gracias, recibí tu comprobante de $412.000. No lo puedo cargar desde acá…»;
  el código dice el formulario, sin monto, porque el puerto no lo trae.
- **El resumen de IA3-A no dice que el borrador va a leer documentos.** La regla de hoy es que el resumen no cambia; sumarle un renglón
  lo decide el usuario.
- **La carga automática** ([abajo](#la-carga-automática-etapa-posterior)): necesita el historial del piloto, sus tableros y su plan, y
  revisar el ADR 0022.
- **Los estándares siguientes:** la biblioteca los muestra como «Próximamente» (`upcoming.json`). La factura recibida con el QR de AFIP
  no está en esa lista y necesita leer el QR.
- **Varias fotos de un mismo documento, o varios documentos en un mensaje:** hoy cada conversación usa su último archivo.
- **Tableros que cruzan formularios o suman datos externos:** tienen su fila en los [planes pendientes](README.md).

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

### Qué puede llegar

Se leen **solo los mensajes de personas con cuenta vinculada y activa,** como hoy en el asistente. Lo que manda un número desconocido queda en el chat para un operador, sin leerse.

| Lo que llega | Cómo se lee | Qué completa |
| --- | --- | --- |
| Texto | Como hoy: el asistente lo interpreta | Propone el formulario prellenado |
| Audio | Como hoy: se transcribe y se trata como texto | Igual |
| Foto o captura (JPEG, PNG) | La IA la lee | Tipo de documento y datos; propone el formulario con la foto adjunta |
| PDF | La IA lee todas sus páginas, con un tope | Igual que la foto |
| Word (`.docx`) | El servidor extrae el texto y las tablas, y la IA los lee | Igual, con el archivo adjunto |
| Excel (`.xlsx`) | El servidor lee las hojas, con los topes de la importación de catálogos, y la IA las lee | Igual. Una planilla con varias filas del mismo tipo propone hasta tres cargas; las demás quedan en la bandeja ([VC1-A](#ubicación-y-contacto-por-whatsapp-w3-y-varias-cargas-desde-un-excel)) |
| Ubicación | Sin IA: llega con latitud, longitud y, a veces, nombre y dirección | Un pedido de la pregunta [«Ubicación»](#la-pregunta-ubicación) que la espera (W1-A); si no, la pregunta de texto elegida en la zona «Ubicación y contacto» del editor (W3) |
| Contacto (tarjeta) | Sin IA: llega con nombre y teléfonos | Las preguntas de nombre y teléfono, elegidas en esa zona (W3) |
| Video o sticker | No se lee | — |

- **Los formatos viejos** (`.doc`, `.xls`), los archivos con macros y los protegidos con contraseña no se leen: se pide un PDF o una versión sin contraseña.
- **El tipo se verifica por el contenido,** nunca por la extensión, como ya hacen los archivos de la [etapa 2B](../features/formularios.md#fotos-y-documentos--implementación-local-2b-2026-10-15).
- **Se aplican topes de tamaño, páginas, hojas y celdas,** incluidas las celdas vacías con formato que encontró el [review de catálogos E1](../reviews/2026-10-22-code-review-catalogos-e1.md).
- **Una foto con un audio que la explica** («es el pago de Pérez») se lee junto solo con la espera de la ráfaga de [casos del asistente](2026-10-21-casos-del-asistente.md). Sin eso, cada mensaje se trata por separado.

### El circuito

1. **Recibir:**
   - la imagen o el PDF se descarga y se guarda como **copia privada**, como los audios;
   - el texto que venga junto al archivo también se usa;
   - un PDF de varias páginas se lee entero.
2. **Clasificar,** entre los formularios con lectura de documentos que **esa persona puede completar.** No hay permisos por formulario: son todos los publicados, si su cuenta tiene el permiso de cargar respuestas, como en el envío desde el chat.
   - **seguro:** propone ese formulario;
   - **duda:** pregunta «¿Qué es este documento?», con botones para 2 o 3 formularios posibles. Un botón de WhatsApp admite hasta 20 caracteres: un nombre más largo manda la pregunta como lista;
   - **no lo reconoce o no hay formulario:** ofrece la lista que esa persona puede completar, más «Otro». «Otro» lo deja en la bandeja y lo anota en «Formularios que faltan».
3. **Extraer** los datos según las **preguntas del formulario elegido**, con su nivel de seguridad por dato.
4. **Validar sin IA:**
   - las validaciones fijas del estándar;
   - el cruce con catálogos: el CUIT en Proveedores o Clientes, como [campo extra de Catálogos E2](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23);
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
| Un contacto sin cuenta vinculada | No se lee ([ADR 0022](../decisions/0022-lectura-de-documentos-con-ia.md)): queda en el chat para un operador, como hoy |
| Una cuenta sin permiso para cargar respuestas | No se lee, porque no tiene formularios posibles: lo dice y deriva |
| El formulario reconocido no se le puede mandar, por ejemplo porque le falta el dato con el que se filtra un catálogo ([E2](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23)) | Lo dice, deriva y queda propuesto en la bandeja; nunca va la lista entera |
| Varias fotos de un mismo documento, o varios documentos en un mensaje | Después de esta etapa: necesita la ráfaga y las varias acciones de [casos del asistente](2026-10-21-casos-del-asistente.md) |

### La bandeja «Documentos recibidos»

Forma elegida, **DOC5-A:** una planilla como la de respuestas, con sus filtros. El documento se abre en un panel al costado, junto a sus datos, como la vista rápida de una respuesta. Las reglas que se programaron están en la [Parte B](#parte-b--la-bandeja-documentos-recibidos-doc5-a-y-doc6).

- **Filtros:** tipo, estado (propuesto, confirmado, sin clasificar, descartado), persona y fecha.
- **El documento al lado de los datos extraídos,** para revisar, corregir y pasar al formulario.
- **Propuestos para revisar acá:** los que la persona no confirmó antes de que venciera el envío, los que no se le pudieron mandar y las filas de una planilla que pasaron el tope. Alguien con permiso los pasa al formulario, y la respuesta dice de qué documento salió y quién la revisó.
- **Cuándo se borra:** cada descartado o sin clasificar muestra la fecha, a los 90 días.
- **Búsqueda por los datos:** monto, CUIT o número de operación.
- **Permisos propios** para ver y revisar documentos.

### Cómo mejora con el uso

- **Cada corrección queda registrada:** el dato propuesto, el confirmado y quién lo corrigió.
- **Las correcciones se le muestran a quien diseña el formulario** («Ver los ejemplos»), que mejora «Cómo reconocerlo» a mano (decisión 3 del 2026-11-11). No se entrena ningún modelo y al proveedor no va nada de otras respuestas.
- **Se mide la precisión por formulario y por campo** («Cómo viene leyendo»). Es la base para la carga automática.

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

El chat de Inicio y el asistente de WhatsApp usan la misma propuesta y el mismo «Crear borrador», también con un pedido escrito
o un audio ([la IA arma formularios y catálogos](2026-11-04-ia-arma-formularios-y-catalogos.md), decidido el 2026-11-04).

### Tableros por formulario

Cada formulario tiene una pestaña **«Tablero»**, armada sola a partir de sus preguntas y ajustable:

| Si el formulario tiene… | El tablero muestra… |
| --- | --- |
| Cualquier pregunta | Cuántas respuestas llegaron por día, semana o mes |
| Un número o un monto | Total, promedio, mínimo y máximo por período |
| Elegir una opción o un catálogo | El reparto por opción, por ejemplo transferencias por banco |
| Persona y estado | Quién cargó cuánto; aprobadas, rechazadas y pendientes |
| Fecha | La evolución en el tiempo |

Forma elegida, **DOC7-A:** arriba, los números contra el período anterior; abajo, una tarjeta por gráfico.

- **Se puede ajustar:** sumar o quitar gráficos, y filtrar con la misma gramática de la planilla, por período, persona o estado.
- **No hay tableros en el código:** salen de los tipos de las preguntas, así que un formulario nuevo, también el armado por la IA, ya tiene el suyo.
- **Los permisos de la planilla:** quien no puede leer respuestas no ve el tablero.
- **Usa los valores con tipo** que ya guardan las respuestas (número, fecha, opción), para sumar y agrupar bien.
- **Más adelante:** tableros que cruzan formularios y que suman datos de sistemas externos, cuando exista la conexión con bases de clientes.

### La carga automática (etapa posterior)

Sin programar. Un modo por formulario, **«Cargar solo si es seguro»**, que carga sin preguntar cuando se cumplen las tres señales:
1. el tipo es claro;
2. las validaciones fijas de los campos elegidos dan bien y no es un duplicado;
3. el formulario tiene un historial suficiente de cargas sin correcciones en esos campos.

La persona recibe «Cargué tu transferencia de $150.000 (REC-000123). Si algo está mal, avisá», y en la bandeja queda marcada «Cargada automáticamente». **No se usa la seguridad que declara el modelo:** no es confiable por sí sola. Se usan las validaciones fijas y el historial medido.

### La pregunta «Ubicación»

Anexada el 2026-11-10. Un formulario puede pedir la ubicación de quien lo completa: un punto con su precisión, en el momento de
completarlo. No es seguimiento continuo y no prueba presencia: es lo que informa el dispositivo. **Programada entera el 2026-11-11,
en back y front:** la Etapa 1, en la app (UB1-A a UB4-A); la Etapa 2, por WhatsApp (W1-A), y el asistente que también manda
formularios con ubicación. Las reglas están en [formularios](../features/formularios.md#la-pregunta-ubicación--ub1-a-a-ub4-a-2026-11-11)
y en [WhatsApp](../features/whatsapp.md#la-ubicación-antes-del-flow--w1-a-2026-11-11); las pantallas, en el lienzo
[Pregunta «Ubicación» en los formularios](https://claude.ai/artifact/9z3HT35LTTF7C7hmwHgFZj) y en el
[diseño del front](../../../ArquitecturaBaseFront/docs/design/ubicacion-en-formularios.md).

Lo elegido el 2026-11-11, todas las recomendadas:
- **UB1-A:** latitud, longitud y precisión en metros (opcional).
- **UB2-A:** primero en la app; por WhatsApp, en la Etapa 2.
- **UB3-A:** el botón «Usar mi ubicación», con la precisión («± 15 m»), «Ver en el mapa» y «Volver a pedirla»; sin mapas ni scripts de
  terceros.
- **UB4-A:** un punto es un dato («-34.603722, -58.381592» con «Ver en el mapa»); el Excel lo reparte en tres columnas numéricas.
- **W1-A:** antes del Flow sale un `location_request_message`; la ubicación que vuelve se guarda en el envío y la captura la suma.
  Un formulario pide una sola ubicación por WhatsApp y la opcional ofrece «Seguir sin ubicación». W2 (pedirla después del Flow) se
  descartó porque cambiaba dos reglas de hoy; W3 (la que la persona comparte por su cuenta) se programó con el
  [anexo](#ubicación-y-contacto-por-whatsapp-w3-y-varias-cargas-desde-un-excel).

Fuera de alcance: el seguimiento continuo, las rutas y las geocercas; los mapas embebidos y la búsqueda de direcciones; verificar que
el punto es real o que está dentro de una zona; la ubicación del dispositivo fuera de un formulario.

## Decisiones tomadas

No se vuelven a preguntar.

- **Biblioteca:** formularios estándar como datos de inicio del producto. «Agregar» crea una copia propia y editable, que recuerda de qué estándar viene. Sirve para una instalación de una empresa y para el multitenant.
- **Primer estándar:** Comprobante de transferencia.
- **La bandeja «Documentos recibidos»** entra en la primera etapa.
- **La carga automática viene después, con datos:** la primera etapa siempre propone y una persona confirma.
- **Genérico:** ningún tipo de documento en el código. Un estándar nuevo es configuración.
- **Formulario nuevo armado por la IA:** nace en la bandeja, sobre un documento sin clasificar, con el aviso de documentos parecidos agrupados. La IA propone y una persona publica.
- **Tableros:** uno por formulario, automático a partir de sus preguntas y ajustable. Los que cruzan formularios o suman datos externos vienen después.
- **Quién manda:** se leen solo los mensajes de personas con cuenta vinculada y activa.
- **Retención:** un documento que termina en una respuesta se queda con ella. Uno descartado o sin clasificar se borra a los 90 días, con su lectura.
- **Tipos en la primera etapa:** foto, PDF, Word (`.docx`), Excel (`.xlsx`), ubicación y contacto.
- **Tableros elegidos (2026-10-22):** DOC5-A, la bandeja como planilla con panel, y DOC7-A, el tablero con tarjetas. DOC1–DOC4 y DOC6 tienen un solo concepto. Lo fijado al dibujar y las correcciones al elegir están en el [diseño del front](../../../ArquitecturaBaseFront/docs/design/documentos.md#los-tableros).

**Esta es la parte 1 de tres proyectos.** La parte 2, formularios conectados a sistemas externos (ARCA, Mercado Libre y cualquier API), y la parte 3, que otros sistemas reciban nuestros datos (webhooks, API pública y MCP), tienen su propio plan después de la investigación del 2026-10-22.

## ADR

Las reglas de lectura están en el [ADR 0022](../decisions/0022-lectura-de-documentos-con-ia.md), aceptado el 2026-10-22:
- qué se lee y qué no;
- qué se manda a la IA;
- las validaciones fijas;
- la confirmación de una persona;
- la retención;
- los costos.

## Implementación: lectura, carga, bandeja, Etapa 3 de la IA y tableros

Anexado el 2026-11-11 y aprobado el mismo día («Sí, arrancá»). Es la evaluación de impacto, la especificación y el plan de este plan y
de la Etapa 3 de [la IA arma formularios y catálogos](2026-11-04-ia-arma-formularios-y-catalogos.md#plan-por-etapas) (PDF, fotos y
Word), con el [ADR 0022](../decisions/0022-lectura-de-documentos-con-ia.md), el
[ADR 0024](../decisions/0024-ia-arma-formularios-y-catalogos.md) y el diseño elegido en el front:
[documentos](../../../ArquitecturaBaseFront/docs/design/documentos.md) (DOC1 a DOC7, con DOC5-A y DOC7-A) y
[la IA arma formularios](../../../ArquitecturaBaseFront/docs/design/ia-arma-formularios.md) (fila «3 · Documentos»). Se programó entero,
cada tarea con su revisión adversarial ([Hecho](#hecho)).

### Alcance

Elegido por el usuario el 2026-11-11, «Todo, con los tableros»:
- **Leer** fotos (JPEG y PNG), PDF y Word (`.docx`) con la IA: un propósito nuevo del motor, con su perfil calificado.
- **Cargar** un documento por WhatsApp (DOC1 a DOC4): la lectura en el formulario, el estándar «Comprobante de transferencia»
  con sus validaciones fijas, el circuito seguro, duda y no lo reconoce, el formulario prellenado con el documento adjunto y las
  correcciones medidas.
- **La bandeja «Documentos recibidos»** (DOC5-A) y **DOC6**, armar un formulario con la IA desde la bandeja.
- **La Etapa 3 de la IA:** armar un formulario con una foto, un PDF o un Word, en Inicio con el clip y por WhatsApp.
- **Los tableros por formulario** (DOC7-A).

Quedaron afuera, y se programaron después con el [anexo](#ubicación-y-contacto-por-whatsapp-w3-y-varias-cargas-desde-un-excel): la
ubicación y el contacto que manda la persona por su cuenta (W3 de la [pregunta «Ubicación»](#la-pregunta-ubicación)) y varias cargas
desde un Excel. Siguen afuera, sin programar: la carga automática, varias fotos de un mismo documento y los tableros que cruzan
formularios.

### Decisiones del usuario (2026-11-11)

Las tres, la recomendada:
1. **Alcance:** todo lo de arriba, con los tableros.
2. **El comprobante en el Flow:** Meta no deja traer un `DocumentPicker` ni un `PhotoPicker` prellenado. El comprobante que
   mandó por el chat queda adjunto a la respuesta sin volver a subirlo: en el Flow, su pregunta dice «Ya adjuntamos el comprobante
   que mandaste. Si querés cambiarlo, elegí otro.» y elegir otro lo reemplaza.
3. **Las correcciones se miden y se muestran:** alimentan «Cómo viene leyendo» y «Ver los ejemplos» se las muestra a quien diseña
   el formulario, que mejora «Cómo reconocerlo» a mano. Al proveedor no va nada de otras respuestas, como dice el ADR 0022.

### Diferencias con los tableros

Lo que los tableros dibujan y una regla escrita o un límite de Meta no deja; se resolvió con la regla, sin preguntar:
- **Los filtros de la bandeja y del tablero viven en memoria, no en la dirección** (DOC5-A y DOC7-A): la regla del front deja los
  filtros privados (búsquedas por montos y CUIT, personas, condiciones) en su dueño, como en la planilla. La dirección lleva la
  página, el orden, el documento abierto, el período y los ajustes del tablero.
- **Los ajustes del tablero son de cada persona en su dirección**, como las columnas de la planilla: no hay preferencias guardadas.
- **Qué pregunta recibe cada dato de un estándar no se cambia** (DOC1 dibuja una lista): lo fijó «Agregar» (BI1-A, ADR 0029) y el
  editor lo muestra con candado.
- **Un PDF se ve con el visor del navegador,** con sus páginas y su zoom; acercar y girar, como en DOC5-A, es para las fotos.
- **En el Flow no hay marcas que cambien mientras se completa** («Corregido», «Lo leímos como…», el bloqueo de «Continuar» de
  DOC3): las marcas salen con el envío y lo que no pasa al confirmar no se guarda, se dice por qué y vuelve el formulario.
- **Un monto del tablero lleva el nombre de su moneda en el título, sin símbolo** («Total de Monto (pesos)»).
- **Un solo período para todo el tablero** (DOC7-A dibuja a la vez una tarjeta por día y otra por semana): todas las tarjetas por
  período siguen el selector Día, Semana o Mes. Lo eligió el usuario el 2026-11-11.
- **El banco o la billetera es una pregunta de catálogo, no un texto** (DOC1 y DOC3 dibujan texto): el estándar trae su catálogo
  propio «Bancos y billeteras», armado con la tabla de entidades. El servidor la completa igual desde la cuenta, y así el tablero
  puede repartir por banco, el ejemplo de DOC7-A.
- **«No se lee» y «no se puede abrir» son dos casos.** Un documento de un número sin cuenta, o de una cuenta sin permiso, no se
  lee: deja su línea en el chat del operador y no entra a la bandeja («Lo que se fijó al dibujar»). Uno que no se puede abrir
  (con contraseña, viejo o con macros) entra como descartado, como muestra DOC4.
- **El aviso de «no se le puede mandar» no dice el monto** (DOC4 lo dibuja): el puerto del canal no lo trae. Se resolvió al programar
  la tarea 9 y queda como [propuesta](#propuestas-para-después-sin-programar).

### Decisiones técnicas

Lo que el diseño no fijaba, resuelto con la opción más simple que cumple el plan y las reglas. Lo que hace hoy el código está en las
fuentes que nombra cada parte; el texto con el que se programó, en Git (`c067117c`).

#### Parte L · Leer un documento

Tareas 1 a 4. Preparar un documento por el puerto de formularios, con PdfPig, sus topes y sus motivos, en los
[servicios de formularios](../features/formularios-servicios.md) («Un documento adjunto»). `ReadDocument`, su perfil, sus cupos fuera
del global y el documento en la interpretación, en [inteligencia artificial](../features/inteligencia-artificial.md#lectura-de-documentos-en-el-mismo-motor--2026-11-11)
y en [el documento en la interpretación](../features/inteligencia-artificial.md#el-documento-en-la-interpretación--2026-11-11). Los
documentos ficticios y la calificación, en [configurar IA](../guides/configurar-ia.md).

#### Parte D · Cargar un documento por WhatsApp (DOC1 a DOC4)

Tareas 5 a 9c. La lectura en el formulario, las validaciones fijas (`CuitNumber` y `BankAccountKey` en `Domain/ValueObjects`),
«Comprobante de transferencia», la biblioteca y «Cómo viene leyendo», en
[formularios](../features/formularios.md#lectura-de-documentos-y-validaciones-fijas--doc1-2026-11-11). El documento que llega, el
circuito, «Derivar», el Flow r14 con lo leído y el reenvío de una captura rechazada, en
[WhatsApp](../features/whatsapp.md#documento-recibido--tarea-8-de-documentos-2026-11-12). Lo que cambió al programar: el reenvío de
una captura rechazada pasó de la tarea 5 a la 9c; «Derivar» lo armó casos del asistente; las cargas que responden a un archivo no gastan
los ofrecimientos del día; el archivo que suma el servidor al capturar es la copia de WhatsApp, y «Bancos y billeteras» trae sólo
bancos.

#### Parte B · La bandeja «Documentos recibidos» (DOC5-A) y DOC6

Tareas 7, 7b, 10 y 14, con F9 y F13 de los arreglos de la [revisión de Formularios](../reviews/2026-11-14-arquitectura-formularios.md). **Es la única fuente
escrita de las reglas de la bandeja:** `docs/features/formularios.md` todavía no tiene su sección. Lo de abajo se contrastó con el
código el 2026-11-18 (`FormReceivedDocument`, `FormReceivedDocumentErrors`, `FormReceivedDocumentsController` y sus tests).

9. **La bandeja es de formularios** (`FormReceivedDocuments`, con su historial `FormReceivedDocumentEvents`, migración
   `AddFormReceivedDocuments`). Entra todo documento leído o rechazado de una cuenta vinculada y activa que puede cargar
   respuestas; lo de un número sin cuenta no entra (DOC4). Guarda su propia copia (`forms/documents/{id}.{ext}`, `WriteOnce` y
   relectura), la lectura y la propuesta cifradas, el estado (`Proposed`, `Unclassified`, `Confirmed` o `Discarded`) con su
   motivo, el tipo de documento, los números para buscar, la respuesta que salió, quién la revisó y el vencimiento de su envío. Un
   propuesto espera a la persona hasta que vence su envío y después queda «para revisar acá»: se calcula al leer, sin otro
   proceso. Mientras espera a su persona, nadie del equipo lo descarta, le cambia el formulario ni lo pasa (409
   `Forms.ReceivedDocument.AwaitingSender`). Un
   descartado o sin clasificar se borra a los 90 días con su lectura y su copia; uno confirmado sigue la retención de su respuesta. Un
   propuesto «para revisar acá» que nadie toca en 90 días (desde que vence su envío o su último cambio, por ejemplo haberlo reabierto)
   pasa a descartado con motivo «venció» y sigue esa retención (F9, 2026-11-15).
14. **Permisos nuevos de formularios:** `formdocuments.read` («Ver documentos recibidos») y `formdocuments.review` («Revisar
    documentos»: corregir, cambiar de formulario, pasar al formulario, descartar y volver a abrir), con la receta de permiso nuevo.
15. **Las rutas** van en `api/forms/documents` (tabla de abajo), con revisión esperada y 409 en lo que escribe y una sola UoW por
    método, y cada una en `ExplicitRouteInventoryTests` con su test. Cambiar de formulario vuelve a extraer con la lectura guardada,
    sin volver a leer el documento: una interpretación del canal `forms-documents`, con quien revisa como Actor, sin Subject y con el
    formulario fijado (`forms.documents.form@1`), que se espera como el audio. Pasar al formulario guarda la respuesta por la captura
    de siempre (`BusinessFormRecordRecorder`, idempotente por el UUID del pedido), como de quien lo mandó, que tiene que seguir activo,
    con quien revisa como operador del origen: el permiso lo pone quien revisa (`formdocuments.review`), no `formrecords.create` de
    quien lo mandó. Si repite una respuesta guardada, contesta 409 `Forms.ReceivedDocument.Repeated` con su número, y se pasa con
    `confirmRepeated` en un pedido nuevo (F13, 2026-11-15). La planilla y el detalle lo muestran con un origen propio («Documento»,
    revisado por quien lo pasó), que aporta formularios por `IStructuredRecordOriginSource` y que sólo ve quien ve la bandeja.
16. **La búsqueda** guarda con el documento los números normalizados de lo leído (montos sin separadores, CUIT, cuentas y
    operaciones sólo con dígitos) en una columna con índice, así se busca en SQL sin descifrar.
    - **Los filtros van en el cuerpo, no en la dirección** (DOC5-A dice «en la dirección»): la búsqueda trae montos, CUIT y
      operaciones, y la regla del front deja los filtros privados en su dueño, como en la planilla. En la URL van la página, el
      tamaño, el orden y el documento abierto (en la ruta, como una respuesta en D2).
    - **El visor** es una pieza de `shared/ui` (`DocumentViewer`): una foto se acerca, se gira y se descarga; un PDF usa el visor del
      navegador, con sus páginas y su zoom, más «Descargar».
17. **DOC6, armar un formulario con la IA** desde un sin clasificar o desde el aviso de los parecidos (tres o más del mismo tema), con
    la relectura al publicar: está en
    [formularios](../features/formularios.md#armar-formulario-con-la-ia-desde-la-bandeja--doc6-2026-11-12).

| Ruta | Permiso | Qué hace |
| --- | --- | --- |
| `POST /api/forms/documents/search` | `formdocuments.read` | La planilla de la bandeja, con los filtros, la búsqueda, los conteos por estado, los grupos de parecidos y las facetas de formulario y de quien lo mandó; los filtros van en el cuerpo |
| `GET /api/forms/documents/counts` | ídem | Los que esperan a alguien del equipo, para el menú |
| `GET /api/forms/documents/{id}` | ídem | La lectura, la propuesta con sus marcas y el historial |
| `GET /api/forms/documents/{id}/file` | ídem | El archivo, sin caché y con `nosniff`; en una fila, la planilla |
| `POST /api/forms/documents/{id}/options` | `formdocuments.review` | Buscar entre las opciones de un catálogo de más de 200 que puede elegir quien lo mandó, con el texto en el cuerpo |
| `POST /api/forms/documents/{id}/discard` y `/reopen` | ídem | Descartar y volver a abrir, con revisión esperada |
| `PUT /api/forms/documents/{id}/form` y `GET …/extraction` | ídem | Cambiar de formulario: vuelve a extraer con la lectura guardada; se espera como el audio |
| `POST /api/forms/documents/{id}/records` | ídem | Pasar al formulario con lo revisado, idempotente por el UUID del pedido |

#### Etapa 3 de la IA · Armar con un documento

Tareas 11 a 13. [Documentos en Inicio](../features/inteligencia-artificial.md#documentos-en-inicio--etapa-3-2026-11-12) (la subida, la
lectura antes del turno, el Word por `file-turns` y el bootstrap), [`propose-form@2`](../features/formularios.md#la-ia-arma-formularios-con-un-documento--etapa-3-propose-form2-2026-11-12)
y, por WhatsApp, el resumen de IA3-A, que no cambia ([WhatsApp](../features/whatsapp.md), «Armar formularios y catálogos»).

#### Parte T · Los tableros por formulario (DOC7-A)

Tareas 15 y 16. En [formularios](../features/formularios.md#tablero-por-formulario--doc7-a) y en el diseño del front
([DOC7-A](../../../ArquitecturaBaseFront/docs/design/documentos.md#doc7-a--tarjetas-armadas-solas-2026-11-11)). Lo decidido al programar:
la zona del perfil de quien lee, un solo período para todo el tablero, el monto con el nombre de su moneda en el título y los ajustes
de cada persona en la dirección, sin preferencias guardadas.

### Hecho

Cada tarea cerró con su revisión adversarial y su corrección. Los commits sin aclaración son del backend.

| Tarea | Commits |
| --- | --- |
| Ubicación, Etapa 1 (app) | 7514ded1, 278ab94c, e973e015; front a0fc834c (diseño), 1868c284, f06df796 y los arreglos 9d3f55b5, 6a49e9ae, 966bc970, ffb20cdf |
| Ubicación, Etapa 2 (W1-A) | 7cadd0ea, b1b891a5, 23fbd373, ded656c2; front 1d4ea87e (diseño), 937bacd1, f0acc367, 3da8c485 |
| El asistente con ubicación | 9c998afa, e0beb766; front 3666a106 (diseño), d62faae1 |
| 1 · Preparar documentos | 47d1dbec |
| 2 · `ReadDocument` en el motor | 7d559160 |
| 3 · El documento en la interpretación | 03cf386d |
| 4 · Arnés y `document-reading` | 7d737e2e; la corrida real, 8b057da5 (30 de 30) |
| 5 · La lectura en el formulario y el estándar | front 71937f06; 4dc93dc4 |
| 6 · Front: la lectura y la biblioteca | front 1ab8d45f |
| 7 y 7b · Capacidades de documentos y bandeja | f2f2356a, c1a53569 |
| 8 · WhatsApp: el documento entrante | 29da5591; front daff81dd |
| 9, 9b y 9c · WhatsApp: el circuito, el Flow r14 y el reenvío de un rechazo | 6aed2572, feb646a1, fbc68773; front 4e32b70c |
| 10 · Front: la bandeja | front d772179c |
| 11 · Inicio: el turno con documento | front 608e9ad2; a80e3192 |
| 12 · `propose-form@2` | 093f184f |
| 13 · Front de Inicio | front 3a1f4338; 87db44bc |
| 14 · DOC6 | 43fb61e6; front 83465252 |
| 15 · El tablero (backend) | a1c8c8ea |
| 16 · El tablero (front) | front b8e538d6, 5ac661af |
| 17 · Calificación y cierre | sets 40f27c08 (`home-documents`, evidencia 29f1087d), 2999e02b (`channel-documents`) y 5394e8dc (`forms-documents`); arreglos d8d14eda, 0a70cdbb, fac348ab, e0b2978b, 7ba8cc0e y 5e9f5644; ARCA con el `CuitNumber` del núcleo, 6c1aff65; cierre b240f686 |
| 18 a 30 · W3 y varias cargas | [abajo](#ubicación-y-contacto-por-whatsapp-w3-y-varias-cargas-desde-un-excel) |
| Después | F9, 78263d6f (2026-11-15); la ubicación de una carga completa la primera fila, 9cbf0ad0 (2026-11-16); la purga registra sólo el tipo de la excepción, 02c60e91 (F18) |

La exportación conjunta de los ejemplos HTTP se hizo el 2026-11-17: trajo las cohortes nuevas `forms-documents-http` y
`forms-dashboard-http` y los cuerpos nuevos de `ai-assistant-http`, `forms-http`, `forms-entries-http`, `forms-responses-http`,
`whatsapp-audio-http` y `whatsapp-forms-http`. Sus lectores en el front faltan ([Lo que falta](#para-cerrar-el-desarrollo)).

## Ubicación y contacto por WhatsApp (W3) y varias cargas desde un Excel

Anexado el 2026-11-11: lo que el [alcance](#alcance) dejaba afuera. El usuario eligió las dos recomendadas: **UC1-A**, una zona
propia en el editor, y **VC1-A**, una entrada por fila en «Documentos recibidos». **Programado entero del 2026-11-12 al 2026-11-13,**
cada tarea con su revisión.

| Tarea | Commits |
| --- | --- |
| 18 · El diseño | front f0133c43 |
| 19a y 19b · Los guards de W3 | front 91e888dc, 7cf29da5 |
| 20 · `receives` en la pregunta | 0b19e9cf |
| 21 · La zona del editor (UC1-A) | front 1b0c45e3 |
| 22 · Lo que llega: la dirección de un lugar y el contacto | f2b37c8c |
| 23 · El circuito y el reenvío | 2a2f7a0b |
| 24 · Lo que se ve | 02039892 |
| 25 · El chat y la actividad | front ee742958 |
| 26 · Los guards de las filas | front 9f2d7c5d |
| 27 · Las filas de cada hoja y el mapeo sin IA | c9db1571 |
| 28 · La bandeja con filas | c3c21bfe |
| 29 y 29b · La planilla, el aviso y las cargas; elegir de la lista | bba30e82, d9fb633c; front 62311ac6 |
| 30 · Front: la bandeja y el chat | front 117116de |

- **Los tableros** están en el lienzo [Ubicación, contacto y varias cargas por WhatsApp](https://claude.ai/artifact/Sv9qPDk5H6xbRabJqUT77w)
  y en la [sección del diseño del front](../../../ArquitecturaBaseFront/docs/design/documentos.md#ubicación-y-contacto-por-whatsapp-w3-y-varias-cargas-desde-un-excel),
  con los textos es/en y las alternativas descartadas (UC1-B, UC1-C y VC1-B).
- **W3:** el orden con W1-A, el envío abierto, lo primero vale, qué se escribe, el reenvío en el mismo envío, el modo que lo frena, una
  respuesta por envío, el contacto en el chat y la actividad están en
  [WhatsApp](../features/whatsapp.md#contacto-y-dirección-compartidos--w3-tarea-22-2026-11-12). `receives` en el borrador y su
  validador, en los [servicios de formularios](../features/formularios-servicios.md).
- **Las cargas por WhatsApp:** quién baja una planilla, el tope de 50 filas, el aviso, hasta tres cargas con su pie, «Por qué lo
  propuso», las repetidas, ninguna que encaja y elegir un formulario de la lista están en
  [WhatsApp](../features/whatsapp.md#planilla-recibida--ia-etapa-2-2026-11-10). Las filas armadas sin IA y su entrada por el puerto
  `IStructuredDocumentRowsChannel`, en los [servicios de formularios](../features/formularios-servicios.md).

### Las filas en la bandeja

Hasta que formularios tenga la sección de la bandeja, éstas son las reglas de las filas que la completan:
1. **Una entrada por fila** en `FormReceivedDocuments` (migración `AddReceivedDocumentRows`): la hoja (si el libro tiene más de una) y
   el número de fila; la lectura cifrada es la fila con los encabezados de su hoja; la copia es de la planilla, la comparten sus filas
   y la fuente de retención la conserva mientras alguna la necesite. El índice único por canal e id opaco suma la fila. Lo que no se le
   pudo mandar guarda su motivo: no se le puede mandar (CE4-A) o pasó el tope. Cambiar de formulario vuelve a proponer el mapeo de esa
   fila con el formulario fijado, sin leer de nuevo.
2. **En la bandeja** cada fila muestra «Fila 5» en gris debajo del archivo y, si el libro tiene varias hojas, «Ventas · fila 5». «Para
   revisar» es el propuesto que espera al equipo; «Propuesto», el que espera a la persona, con «Descartar» y «Pasar al formulario»
   apagados y su motivo encima (409 mientras su envío no venció). El filtro «Propuestos» incluye los dos. El panel muestra «La fila»
   como llegó, con «Descargar la planilla», en lugar del visor.
3. **Repetida** es la que coincide con una respuesta en lo que el formulario marca como repetido, o la igual a otra fila de la misma
   planilla: queda descartada, con su fecha de borrado. La misma planilla otra vez entra descartada, como un documento repetido.
4. **El origen** de una respuesta que salió de una fila dice «Salió de una planilla».
