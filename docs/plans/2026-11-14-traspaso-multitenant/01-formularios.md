# 01 · Formularios

Lo nuevo del módulo de formularios del 7 al 14 de noviembre, salvo renglones (traspaso 12) y la lectura de documentos
(02). Las reglas vigentes están en `docs/features/formularios.md`, con una sección fechada por desarrollo; el detalle de
servicios y pruebas, en `docs/features/formularios-servicios.md` y sus pares. Todo lo de esta parte vive en el módulo de
formularios y no nombra a ARCA: ARCA lo usa por puertos neutrales (04).

## 1. El motor para documentos (2026-11-09)

Nació como la etapa 2 de ARCA (plan `docs/plans/2026-11-09-arca-etapa-2-motor.md`; diseño en
`ArquitecturaBaseFront/docs/design/motor-para-documentos.md`), pero es genérico. El usuario eligió las cuatro recomendadas.

- **Cálculos (CA1-A).** El cálculo es una pregunta más, de tipo «Cálculo» («Qué calcula»), decimal y de sólo lectura,
  con tres operaciones fijas y sin fórmulas libres:
  - **Multiplicar** dos operandos: del mismo ítem si el cálculo está en el bloque, de afuera si no.
  - **Sumar** de 1 a 20 operandos; afuera del bloque, suma una pregunta del bloque en todos los ítems.
  - **Porcentaje:** base × alícuota / 100. La alícuota puede ser un decimal o la opción elegida de un catálogo con valor
    numérico.

  Redondea a dos decimales con half-even, cada cálculo por su lado. Los operandos van antes del cálculo, así que no hay
  ciclos. Un producto o un porcentaje con un operando vacío queda vacío; la suma ignora los vacíos. No puede ser
  obligatoria y una respuesta enviada a un cálculo es inválida: lo calcula el servidor, también al corregir. La carga de
  la app muestra el resultado en gris mientras se escribe; en WhatsApp no se ve mientras se completa. Planilla, filtros,
  Excel, PDF, reglas y avisos lo leen como un número. Publicar frena con `Forms.Publication.CalculationInvalid`. Se
  descartó CA1-B (columnas calculadas y totales al pie).
- **Condiciones (CO1-A).** «Se muestra: Siempre / Según otra respuesta», en cada pregunta, en la app y por WhatsApp. Una
  pregunta de afuera del bloque se muestra según una anterior de afuera, de elegir una (propia o de catálogo) o de sí o no.
  Oculta, no es obligatoria y no puede traer respuesta. Un título de sección sin preguntas visibles se oculta. Por
  WhatsApp hay límites de Meta (03, §4): con varias respuestas de un catálogo o una cadena de más de tres condiciones, el
  editor de un formulario que va por WhatsApp frena con «Sólo en la app» como salida. Se descartó CO1-B (la condición en el
  título de sección).
- **Resultado externo (RE1-A).** Lo que devuelve una acción de afuera (por ejemplo, el comprobante de ARCA) se guarda en la
  respuesta, sellado, con nombre por idioma y datos tipados, así que se sigue leyendo aunque se quite el módulo. Es una
  zona arriba de «Datos» en el detalle y en la copia PDF, y una columna por dato en la planilla y el Excel. Los avisos de
  una regla lo usan con `{result:<fuente>.<clave>}`, sólo después de la acción que lo declara. Se descartó RE1-B (el
  resultado en la actividad).
- **Emitir un documento.** Una acción de regla, `IssueDocument`, pide a un módulo emisor que emita algo desde la respuesta,
  por el puerto `IStructuredRecordIssuer`. La usa la factura de ARCA, con su «¿La emito?» (04, §2).
- **Commits:** backend `3c15292e`, `ed3ef5bc`, `0520a9bf`, `dea532ec`, `c1f7f4ff`, `4a391b3d`, `f204664b`, `91e6e66f`
  y `74040fa0`; front `650b4d40`, `d8c731e1`, `bf6f64ac`, `e8550e4a` y `49730621`. Migración `AddFormRecordResults`.

## 2. La biblioteca de estándares (BI1-A, ADR 0029)

- **Qué es.** Cada módulo aporta sus estándares como datos, por el puerto `IStructuredFormStandardSource`: un estándar
  nuevo es una entrada de datos, no código. Hoy hay dos: «Factura», que trae ARCA, y «Comprobante de transferencia», que
  trae el mismo módulo de formularios con la lectura de documentos (02). Sin estándares instalados, «Biblioteca» no
  aparece en Gestión de formularios.
- **Agregar.** La pantalla lista «Estándar», «Versión», «Origen», «Formularios» y «Acciones». «Agregar» pide nombre,
  prefijo (el servidor sugiere uno libre, mirando también los eliminados) y carpeta, y crea una copia en borrador que
  recuerda su origen, con sus catálogos propios y sus primeras opciones, sus valores iniciales, sus casillas y sus reglas
  como revisión 1 activa. Se puede agregar más de una vez. Una versión nueva del estándar no cambia las copias.
- **Preguntas atadas (BI1-A).** Las preguntas de las que depende el módulo llevan un candado: se puede cambiar su texto, su
  ayuda y su lugar, pero no quitarlas ni cambiar su tipo, bloque, catálogo, cálculo o condición
  (`Forms.Draft.BoundQuestionChanged`). El editor muestra los controles apagados con su motivo. Se descartó BI1-B (una
  lista en «El formulario»).
- **Casillas y valor inicial.** Un estándar puede declarar casillas en sus preguntas, como «El precio incluye IVA» en
  «Precio unitario» (IV2-A), y una opción inicial, como la alícuota del 21 % (IV1-A).
- **Guardar dos filas que se apuntan.** La definición que crea «Agregar» y su primera revisión de reglas se guardan en un
  solo guardado con una clave diferida en PostgreSQL, sin excepción al ADR 0001 (06, §1).
- **Commits:** backend `e2cb8606`, `4f49f161`, `be8048ce`, `8fb5a9a0`, `054e419d` y `ce85f71b`; front `5e334795`,
  `9899a1cf` y `7e8b2ed3`. Migraciones `AddFormDefinitionStandard` y `DeferActiveRuleSetKey`.

## 3. Catálogos que mantiene un módulo y «Completar con un padrón» (2026-11-11)

- **Catálogos `External`.** Un módulo aporta listas por `IStructuredCatalogFeed` y Formularios las guarda como un tercer
  tipo de catálogo, de sólo lectura, con una clave estable. Se crean el primer día que el origen responde (una instalación
  que nunca conecta ARCA no tiene catálogos vacíos) y van a una carpeta del sistema. Un worker los relee cada día o cuando
  fallaron hace más de 15 minutos; lo que deja de venir se desactiva y nunca se borra. No se editan ni se importan
  (`Forms.Catalog.ReadOnly`). La pantalla dice «Actualizado por ARCA hace 2 h» o la última falla. Hoy los trae ARCA:
  condición frente al IVA, tipos de documento, conceptos, monedas y alícuotas.
- **«Completar con un padrón».** Por `IStructuredPartyLookup`, un módulo completa una opción de un catálogo desde un dato:
  «Completar con ARCA» llena una opción de «Clientes» desde la CUIT (razón social, condición frente al IVA y domicilio).
  No guarda nada: la persona revisa y guarda. Tiene cupo por persona y una caché corta (04, §6).
- **Commits:** backend `08c83c19`, `fa2acc24` y `80891224`; front `9da76575`. Migración `AddExternalCatalogs`.

## 4. Carpetas (CP1-A a CP5-A, programadas el 2026-11-09 y el 11)

- **Decisiones.** Un solo árbol para formularios y catálogos, con subcarpetas y un mismo elemento en varias. El árbol va en
  un panel a la izquierda de Formularios, Diseñar formularios y Catálogos (CP1-A); «Carpetas…» abre un diálogo con
  casillas (CP2-A); se administran en el mismo panel (CP3-A); y cada carpeta elige si va al menú lateral (CP5-A). Por
  defecto: hasta cinco niveles, «Todos» y «Sin carpeta» arriba, una carpeta incluye sus subcarpetas, sin cantidades, orden
  alfabético y la carpeta elegida en la URL.
- **Las carpetas sólo ordenan.** No dan ni quitan acceso y no hay permisos nuevos. Quien administra ve todas, también las
  vacías; los demás, sólo las que tienen algo visible para ellos. Borrar una carpeta nunca borra lo que contiene. En el
  celular, el árbol es un selector «Carpeta: Todos».
- **El menú lateral (CP5-A).** Una carpeta puede quedar fuera del menú, ser un menú propio (con uno de 12 íconos) o ir
  dentro de «Gestión de formularios» o de «Administración». El menú muestra sólo los formularios activos que esa persona
  puede abrir, nunca más de dos niveles y nada vacío; abre la planilla a quien ve respuestas y la carga a quien sólo carga.
- **Carpetas del sistema.** Lo que crea el sistema (los catálogos de ARCA, la copia de un estándar) se archiva por clave en
  su carpeta, aunque se renombre o se mueva; si la borraron, se recrea.
- **Commits:** backend `724bcc25`, `e62d80ae` y `dde47722`; front `aa10d91d`, `9d34e95d`, `56eeec07` y `ab44dca9`.
  Migración `AddFormFolders`. Diseño en `ArquitecturaBaseFront/docs/design/carpetas.md`.

## 5. Formularios sólo en la app (SA1-A a SA5, 2026-11-09)

Nació en la prueba integral, cuando Meta rechazó dos versiones de un formulario y la app siguió en la anterior. El usuario
eligió las siete recomendadas (plan `docs/plans/2026-11-09-formularios-solo-en-la-app.md`; diseño en
`ArquitecturaBaseFront/docs/design/formularios-solo-en-la-app.md`).

- **SA1-A.** El ajuste «Dónde se completa» va en «El formulario», al lado del prefijo: «En la app y por WhatsApp» (el de un
  formulario nuevo, también si lo arma la IA) o «Sólo en la app». Va con el borrador y cada versión lo sella. Aparece sólo
  si hay un canal instalado.
- **SA2-A.** Con «Sólo en la app» se va todo lo de WhatsApp: «Cuándo lo ofrece el asistente», «Qué sugerir después», la
  vista oficial y las menciones en las (i); lo escrito se conserva. La vista previa pasa a ser la de la app. Valen los
  límites de la app (por ejemplo, el bloque hasta 100 ítems y sin el tope de 200 opciones del desplegable). Las reglas
  siguen avisando por WhatsApp: avisar no es completar.
- **SA3-A.** Publicar en la app es instantáneo («Publicamos la versión N.»). El estado se dice una vez («Publicado ·
  versión 4»); «Dónde se completa» es una fila de «Qué se publica» y una columna del listado, y la columna «En WhatsApp»
  pasó a «Estado».
- **SA4-A.** Se pasa de un lado al otro cambiando el ajuste y publicando: hacia la app es al instante, con el aviso «Deja de
  completarse por WhatsApp. Los envíos abiertos se pueden responder hasta que vencen.»; hacia WhatsApp espera a Meta y,
  mientras tanto o si rechaza, sigue la versión anterior.
- **SA5.** Sin el módulo de WhatsApp, todo formulario es de la app.
- **Lo demás.** «Enviar formulario», el asistente y los destinos de sugerencias no ofrecen un formulario de la app; una
  sugerencia hacia uno que pasó a la app se ve «Sólo en la app» y hay que quitarla para guardar. La baja de uno de la app
  dice «App · No se carga más» (decidido el 11/11). El front sabe si hay canal por una capacidad neutral, sin nombrar a
  WhatsApp.
- **Commits:** backend `1f72cd3f` (migración `AddFormAppOnly`), `ab66934c` y `2ebeed2d`; front `c91d85f7` a `da913f14`,
  `28e2f70d` y `66262d60`.

## 6. El Excel en una hoja (XL1-A, XL2) y el diálogo de exportar (EX1-A, EX2-A)

- **XL1-A (2026-11-09).** Una sola hoja «Respuestas» con una fila por ítem del bloque y los datos de la respuesta
  repetidos; una respuesta sin ítems ocupa una fila. Se descartaron los ítems en columnas (XL1-B) y las filas agrupadas
  (XL1-C). Columnas fijas: «N°», «Recibida», «Cargó», «Canal» («WhatsApp» o «App») y «Estado» (XL2, sumada el 11/11), más
  «Anulada» si se piden las anuladas. «Recibida» va en la zona horaria del perfil de quien exporta, como fecha de Excel
  con números y la hora después de una coma.
- **El tope.** 10.000 respuestas y 100.000 filas; al pasarlo corta antes de escribir con `Forms.Record.ExportTooManyRows`
  («El Excel tendría más de 100.000 filas, una por ítem. Acotá los filtros o sacá el bloque de las columnas.»). Lo eligió
  el usuario al ver la medición: un millón de filas tardaba 84 s y 2,8 GB; cien mil, 11 s y 0,4 GB.
- **El diálogo (EX1-A y EX2-A, 2026-11-11).** Arriba, «Respuestas» y «Filtros» en `DataList`, con los chips de la planilla
  sin cruz; después «Columnas» e «Incluir las anuladas», cada una con su (i); al pie, «Archivo» y «Filas» («Una por ítem de
  Artículo» o «Una por respuesta»). El filtro de una pregunta del bloque dice «Estado: Dañado (en algún ítem de
  Artículo)» (EX2-A). Una «Ubicación» cuenta como tres columnas.
- **Commits:** backend `545c9c71`, `b688b89d` y `511daf66`; front `1b9a9eda` y `1e71a5b8`.

## 7. La pregunta «Ubicación» (UB1-A a UB4-A) y por WhatsApp (W1-A), 2026-11-11

Anexada al plan de documentos (`docs/plans/2026-10-22-documentos-por-whatsapp.md`, «La pregunta «Ubicación»»); diseño en
`ArquitecturaBaseFront/docs/design/ubicacion-en-formularios.md`.

- **Qué es.** Un punto, en el momento de completar: latitud, longitud y precisión en metros (UB1-A). No es seguimiento
  continuo ni prueba de presencia, y no hay mapas embebidos.
- **En la app (UB2-A y UB3-A).** «Usar mi ubicación» pide el punto al tocar (nada se pide antes) y lo muestra en un
  recuadro con la precisión, «Ver en el mapa» y «Volver a pedirla»; con más de 100 m de imprecisión se avisa y se puede
  enviar igual. Tiene sus estados de espera, permiso denegado y sin señal. Sólo admite «Obligatoria», nunca va en el
  bloque y ninguna condición ni cálculo se apoya en ella.
- **En las lecturas (UB4-A).** Un punto es un dato: la planilla lo muestra como enlace al mapa, el detalle suma la
  precisión, el Excel lo reparte en tres columnas numéricas y el PDF escribe el punto con «Ver en el mapa».
- **Por WhatsApp (W1-A).** Un Flow no puede pedir la ubicación, así que antes del Flow sale un pedido de ubicación de Meta:
  «Para completar «<nombre>», mandanos tu ubicación.», con el botón de WhatsApp; si es opcional, también «Seguir sin
  ubicación». Cuando llega, sale el Flow de siempre y la respuesta se guarda en una sola captura. Si no llega, el envío
  vence a las 24 horas sin guardar nada. El chat del operador muestra el pedido con su estado y el detalle de la respuesta
  lo cuenta en la actividad. El asistente también ofrece y manda formularios con ubicación: primero el pedido y después el
  Flow. Se descartaron pedirla después del Flow o sólo por el asistente.
- **Commits:** backend `7514ded1`, `278ab94c`, `7cadd0ea` (migración `AddWhatsAppFlowLocation`), `23fbd373`, `ded656c2`
  y `e0beb766`; front `1868c284`, `937bacd1`, `f0acc367` y `d62faae1`.

## 8. Elegir primero por un campo: lo programado después del traspaso 10

Las decisiones LD1-A a LD3-A están en el 01 del traspaso 10. Desde entonces:

- **La carga en la app** (front `7af6c766`). Dos combos: el de la pregunta espera apagado con «Elegí primero «Rubro»» y, con
  el valor, trae «75 opciones de Herbicidas». Al corregir arranca con el valor guardado. En el bloque, dos columnas de la
  tabla.
- **WhatsApp con el perfil `r11`** (backend `1b59afe2`; 03, §4). Con un solo valor, en WhatsApp también hay que elegirlo.
- **El máximo por persona cuenta por valor** (backend `428ade2a`, front `5d51d62e`): «Todos ven las mismas 125, en 3 valores
  de «Rubro». El más grande tiene 75: entra en el desplegable (200).». El tope de 200 vale para cada valor y para la lista
  de valores, no para el total.
- **LD3-A** se programó con la etapa 2 de renglones: publicar frena si una pregunta de afuera depende de una del bloque.

## 9. Catálogos: «Ordenar por…» (OR1-A, 2026-11-11)

En el ⋮ de «Opciones», «Ordenar por…» reemplaza a «Ordenar A–Z» y abre «Ordenar las opciones»: una columna (Texto, Código o
un campo activo) y un orden con palabras según el tipo («De la A a la Z», «De menor a mayor», «De la más antigua a la más
reciente», «Primero «No»»). Hasta guardar, la tabla no cambia y arriba dice «Se ordenará por «Rubro», de la A a la Z, al
guardar.», con «Cancelar orden». Al guardar se ordenan todas las opciones, también las de otras páginas; los empates
conservan su orden y lo vacío va al final; los números dentro de un texto se comparan por su valor. Se descartó OR1-B
(ordenar tocando el encabezado). Backend `d461de05`; front `1c6bb42d` y `b5577643`.

## 10. Arreglos con regla nueva

- **Mensajes de validación** con texto del proyecto (06, §1).
- **Fechas en el PDF, los avisos, el Excel y el asistente**, sólo con números y la hora después de una coma (05, §2):
  backend `b1129d46`, `89cd4392`, `511daf66` y `25bc6823`.
- **Google sin cuenta con el registro cerrado** responde `Auth.Account.NotInvited`: «Para ingresar, pedile una invitación a
  un administrador.», en lugar de ofrecer «Crear cuenta», que no existe (backend `a1e59c6e`, front `57460ba9`;
  `docs/features/identidad.md`).
- **Plazos:** el correo corta a los 30 s sin respuesta del servidor (antes un servidor callado frenaba la cola unos 6
  minutos, `48f272d6`), y la vista oficial de Meta tiene un tope total de 30 s (`76e07a03`).
- **Guardar las opciones de un catálogo** no lleva la pantalla arriba (front `53ade737`).

## 11. Para el multitenant

- **Tu E19 (condiciones, cálculos y líneas).** Allá el cálculo tiene cuatro operaciones y redondeo `AwayFromZero`; acá son
  tres fijas (multiplicar, sumar, porcentaje, con la alícuota que puede venir de un catálogo) y half-even a dos decimales,
  igual que los importes de la factura. Las condiciones de acá tienen los límites de Meta de `r13`. Son preguntas para el
  usuario: el redondeo cambia importes.
- **Tu E22 (biblioteca de la plataforma).** Allá la arman los operadores de la plataforma y nace vacía; acá la aportan los
  módulos como datos, con preguntas atadas y reglas que llegan activas. En el multitenant pueden convivir las dos: las
  plantillas de la plataforma y los estándares de un módulo como ARCA. A decidir.
- **Tu E21 (reglas).** Suma el resultado externo sellado en la respuesta, `{result:…}` en los avisos y la acción de emitir.
- **Carpetas** es nuevo para vos: son de cada organización y no dan acceso; el menú lateral por carpeta se cruza con tu
  menú por acceso.
- **Sólo en la app contra tu decisión 98.** Allá un formulario pasa a ser de la app por lo que tiene (sin número, con una
  lista dependiente, con un catálogo grande); acá es un ajuste explícito y, sin canal, todo es de la app. Con el feature de
  WhatsApp por organización, el ajuste aparece sólo donde hay canal. A decidir.
- **El Excel.** Tu E15 tiene un tope de 10.000 respuestas; acá suma el de 100.000 filas, una por ítem, la columna «Estado» y
  la zona horaria de quien exporta (con varias organizaciones, la de la organización si la persona no tiene una).
- **La ubicación** es un tipo de pregunta nuevo: con tu regla del orden (78) hay que elegir en qué etapa entra y verla en
  todas las pantallas que ya existen. Las coordenadas son datos personales: entran en la retención que decida cada
  organización.
- **«Ordenar por…»** reemplaza el «Ordenar de la A a la Z» del ⋮ de «Opciones» de tus tableros de la E12.
- **`NotInvited`** depende de tu modelo de alta por organización.
