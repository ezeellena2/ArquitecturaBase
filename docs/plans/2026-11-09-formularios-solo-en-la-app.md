# Formularios sólo en la app

Decidido el 2026-11-09: el usuario eligió las siete recomendadas («Sí, las recomendadas»). **El backend quedó programado el
mismo día** (corte 1, con el visto bueno del usuario: «Ahora, el backend»). **El front quedó programado el 2026-11-11**, al
terminar la prueba integral (corte 2, «Sí, al terminar»). Las capturas de los ejemplos HTTP ya están (cinco cuerpos en
`forms-http/app-only`); falta el cierre: la exportación conjunta, las corridas completas, la prueba a mano y retirar el plan.
Las reglas vigentes están en [formularios](../features/formularios.md#formularios-sólo-en-la-app--sa1-a-a-sa5-2026-11-09). Lo pidió durante esa prueba: «Quiero poder
crear formularios que no vayan a WhatsApp, que sean sólo de la app». Hoy no se puede, porque publicar una versión siempre pasa
por un canal.

**Tableros:** SA0 a SA5 en el lienzo aparte [Formularios sólo en la app](https://claude.ai/artifact/GjxDspdmg3xtZzG4oH2Hzu)
(versión 5, con las elegidas marcadas y una nota verde por fila). El Sistema visual es de otra cuenta, así que, como en
renglones, carpetas y la IA, el diseño elegido queda registrado en el front:
[formularios sólo en la app](../../../ArquitecturaBaseFront/docs/design/formularios-solo-en-la-app.md).

## Lo que hay hoy

- **Publicar siempre pasa por un canal.** `BusinessFormPublicationService.CheckAsync` agrega
  `Forms.Publication.ChannelUnavailable` («No hay un canal de formularios instalado.») cuando no hay ningún
  `IStructuredFormChannel`, y `FormPublicationChannelVerifier` devuelve el mismo error al aceptar. El único canal es
  `WhatsAppStructuredFormChannel`: sin el módulo no se publica nada, aunque [formularios](../features/formularios.md) y
  [quitar WhatsApp](../guides/quitar-whatsapp.md) declaran a BusinessForms independiente.
- **La versión espera a Meta.** La aceptación sella la versión sin activarla; el worker la despacha y la activa recién con el
  canal en `Published` (Accepted → Preparing → Publishing → ActivationPending → Published). Si Meta la rechaza, la operación
  termina `Failed` y la versión activa anterior sigue. El 2026-11-09 Meta rechazó las versiones 2 y 3 de «Remito de prueba»
  (el bloque al final, que arregló e42cc2a1) y la app siguió cargando la 1.
- **La carga desde la app no depende de WhatsApp.** La carga, la planilla, el detalle, el Excel, las reglas, las aprobaciones y
  el PDF trabajan sobre la versión activa. Lo único que falta es una forma de activar una versión sin canal.
- **WhatsApp aparece en el editor para todos los formularios:** los topes de textos del compilador
  (`WhatsApp.Flow.TextTooLong`), los ítems del bloque (`RowsOverChannelLimit` y `MinRowsOverChannelLimit`, ya condicionados a
  que haya canal), los avisos de catálogo de `FormPublicationGuard` que sólo valen para el desplegable de Meta
  (`OptionsPerRecipientHigh` con su 200 y `DependsOnAnswer`), «Cuándo lo ofrece el asistente», «Qué sugerir después», la vista
  oficial y la vista previa del celular.

## Decisiones (2026-11-09)

El usuario eligió las siete recomendadas.

1. **¿Dónde se elige? SA1-A.** Un ajuste «Dónde se completa» en la zona «El formulario», al lado del prefijo, con «En la app y
   por WhatsApp» y «Sólo en la app»; se guarda con el borrador y cada versión lo sella. Descartó SA1-B, elegirlo en el diálogo
   de publicar en cada versión (el editor no puede adaptar avisos ni límites), y SA1-C, elegirlo al crear y que quede fijo
   (pasarlo a WhatsApp exige otro formulario, con otro prefijo y otra planilla).
2. **¿Con qué arranca un formulario nuevo?** Con «En la app y por WhatsApp», como hoy, también el que arma la IA.
3. **¿Qué cambia con «Sólo en la app»? SA2-A.** Se va todo lo de WhatsApp y el formulario no aparece en WhatsApp: «Enviar
   formulario» en el chat no lo lista, el asistente no lo ofrece, «Qué sugerir después» no lo usa como origen ni como destino,
   y se van la vista oficial, «Cuándo lo ofrece el asistente», el máximo de 10 ítems del bloque (llega a 100 sin avisos), el
   desplegable de 200 de «Elegir primero por un campo» y los topes de textos de Meta. Descartó SA2-B, los límites de WhatsApp
   para todos: no evita los rechazos de Meta (el del 9/11 no lo veía ningún chequeo) y sin el módulo no hay chequeo que correr.
4. **¿Las reglas siguen avisando por WhatsApp?** Sí: avisar no es completar. Cada regla ya elige si avisa, a quién y con qué;
   «Sólo en la app» no las toca.
5. **¿Qué dice el estado y dónde se ve el lugar? SA3-A.** Publicar algo de la app es instantáneo, el encabezado sigue diciendo
   sólo el estado («Publicado · versión 4», EP1-A), «Qué se publica» suma la fila «Dónde se completa» y el listado suma la
   columna «Dónde se completa», al lado de la que hoy se llama «En WhatsApp» y pasa a «Estado». Descartó SA3-B, el estado dice
   el lugar («Publicado en la app · versión 4»), y SA3-C, una etiqueta gris aparte. En el listado no había opción: «Un dato por
   columna», la regla elegida el mismo día, no deja el lugar en un renglón gris debajo del estado ni en una etiqueta junto al
   nombre.
6. **¿Cómo se pasa de la app a WhatsApp y al revés? SA4-A.** Se cambia «Dónde se completa», se guarda y se publica una versión
   nueva. A WhatsApp espera a Meta como hoy, y mientras tanto o si la rechaza la app sigue con la anterior; a la app es
   instantáneo, con el aviso «Deja de completarse por WhatsApp. Los envíos abiertos se pueden responder hasta que vencen.».
   Descartó SA4-B, que la app nunca espere a Meta: dos versiones activas a la vez (una en la app y otra en WhatsApp), dos
   estados en pantalla y un cambio para todos los formularios de WhatsApp.
7. **¿Qué hace la plantilla sin el módulo de WhatsApp? SA5.** Todo formulario es de la app: no aparece el ajuste, el botón dice
   «Publicar» y la versión queda activa al instante. Lo que se publicó por WhatsApp antes de quitar el módulo se sigue
   cargando en la app.

## Cómo queda

### En el editor

- «Dónde se completa» va en «El formulario», al lado del prefijo, con su (i). Aparece sólo si hay un canal de formularios
  instalado. Es un cambio del borrador: se guarda con «Guardar borrador» y rige desde la próxima versión.
- El botón del encabezado dice «Publicar en WhatsApp» o «Publicar» según lo elegido, aunque no esté guardado.
- Con «Sólo en la app» se van «Cuándo lo ofrece el asistente» (lo escrito se conserva), la zona «Qué sugerir después» (las
  sugerencias guardadas se conservan), «Ver vista oficial» y las menciones de WhatsApp de la (i) del máximo del bloque, de
  «Elegir varias» y de la línea de «Elegir primero» («entra en el desplegable (200)»).
- La vista previa del costado pasa a ser la de la app: el panel de «Nueva respuesta», en una columna y con un recuadro por ítem
  del bloque, en lugar del celular de WhatsApp.

### Al publicar

- El diálogo es «Publicar la versión N» (sin «en WhatsApp»), con «Problemas» y «Avisos» sólo si hay (PU1-A) y la fila «Dónde se
  completa» en «Qué se publica», con el valor de la versión nueva.
- Al aceptar, la versión queda activa: el aviso es «Publicamos la versión N.» y la etiqueta, «Publicado · versión N». No hay
  «Publicación aceptada», «Publicando» ni consulta.
- Reactivar un formulario de la app también es instantáneo.

### En el listado y en los diálogos de baja y eliminación

- En «Diseñar formularios», la columna «En WhatsApp» pasa a «Estado», el segmentado deja de llamarse «Estado en WhatsApp» y se
  suma la columna «Dónde se completa», con el lugar de la versión activa (en un borrador sin versión, el del borrador). Va sólo
  con un canal instalado. Las columnas siguen [un dato por columna](../../../ArquitecturaBaseFront/docs/plans/2026-11-09-un-dato-por-columna.md):
  si su UD2 («En WhatsApp», «Versión activa», «Último cambio» y «Cambió») se programa antes, este cambio renombra su «En
  WhatsApp» y suma la columna; si se programa después, UD2 parte de «Estado».
- La baja dice «App · No se carga más» para todos; «Chat», «Asistente» y «Sugerencias», sólo si la versión activa se publicó
  por WhatsApp. Una cuya versión activa es de la app, aunque una anterior haya sido de WhatsApp, muestra sólo «App · No se carga
  más» y suma el aviso con la fecha del último envío abierto únicamente si todavía quedan envíos de cuando era de WhatsApp: lo
  eligió el usuario el 2026-11-11 («App» y aviso), porque este plan y el diseño se contradecían. La advertencia de WhatsApp al
  eliminar aparece sólo si alguna versión se publicó ahí.
- «Formularios» (la pantalla con «Cargar») no cambia.

### En WhatsApp y el asistente

- Un formulario cuya versión activa es de la app no aparece en «Enviar formulario», el asistente no lo ofrece ni lo prellena,
  no es destino de una sugerencia y un pedido por WhatsApp va a «Formularios que faltan», como todo lo que no se puede
  completar ahí.

### Pasar de un lado al otro

- A WhatsApp: los problemas y avisos de WhatsApp aparecen al publicar, con «Ir a la pregunta»; la versión espera a Meta.
- A la app: los envíos abiertos de la versión anterior se responden hasta que vencen y sus Flows se deprecan después, como con
  cualquier versión reemplazada.
- Es el mismo formulario: respuestas, numeración, prefijo, reglas, PDF y planilla siguen.

## Diseño técnico (lo que no se pregunta)

La opción más simple que cumple lo anterior. Publicar en la app no tiene efectos fuera del sistema, así que no lleva recuperación
durable ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)): se acepta y se activa en un solo límite.

### Backend

- **El borrador.** `FormDraftInput` suma `appOnly` (`true` o null; `false` se guarda como null y se omite al escribir, como
  `repeated`), así un borrador de siempre conserva sus bytes y sus huellas. La definición lo guarda con la misma revisión.
  Entra en la comparación del chequeo y la aceptación (`FormPublicationSnapshotPolicy.Matches`) y en la huella de la
  publicación, así un comprobante de un modo no sirve para el otro.
- **Publica sólo en la app** cuando el borrador lo dice o cuando no hay canales instalados.
- **El chequeo** no llama al canal ni agrega `ChannelUnavailable`, `RowIssues`, `RowWarnings`, `OptionsPerRecipientHigh` ni
  `DependsOnAnswer` (`FormPublicationGuard.InspectCatalogsAsync` recibe si la publicación va a un canal). Responde
  `LocallyValidated` y `Available` sin canales. El aviso «Deja de completarse por WhatsApp» lo arma el front con lo que ya
  tiene: `publication.activeVersion` sin `appOnly` y el borrador con `appOnly` (sin código de error nuevo).
- **La aceptación** conserva lo local del verificador (almacenamiento de archivos, 10 archivos, catálogos válidos) y se saltea
  los canales. Sella la versión con las culturas completas del chequeo (`PublishedCultures`, original y secundario completo) y
  con `AppOnly` (columna nueva de `BusinessFormVersions`, en false para las anteriores: todas pasaron por un canal). La
  operación nace `Published` en el mismo límite (un método de Domain que sólo parte de `Accepted`) y la definición activa la
  versión con `ActivateVersion`, que también retira una baja. El recibo dice `Published` y la repetición del pedido devuelve el
  mismo. El worker no la ve: no queda abierta.
- **Las lecturas.** El detalle y el listado suman `appOnly` de la versión activa y del borrador, sólo cuando es true (el front
  valida con `strictObject`, así que lo que existía no cambia); `canPreview` exige una versión activa publicada en un canal; el
  ciclo de vida suma `publishedOnlyInApp` cuando toda versión fue de la app (para la advertencia al eliminar). Los idiomas de
  una versión de la app salen de los que selló, también en el estado de la publicación y en la comparación del chequeo.
- **La selección.** `FormSelectionReader` excluye las versiones activas `AppOnly`: alcanza a «Enviar formulario», a los
  candidatos y el prellenado del asistente, a la aclaración y a las sugerencias en tiempo de ejecución. El editor de
  sugerencias no ofrece ni guarda un destino de la app («Elegí un formulario publicado disponible.»).
- **La capacidad del front.** `FormClientCapabilitySource` suma `form-channels` cuando hay al menos un `IStructuredFormChannel`
  registrado; como los canales son scoped y la fuente es singleton, lo pregunta con `IServiceProviderIsService`. Es neutral: el
  núcleo y BusinessForms siguen sin nombrar a WhatsApp.
- **El retiro de Flows** no cambia: la versión de WhatsApp reemplazada por una de la app ya figura como no usada
  (`IFormUnusedVersionReader`) y WhatsApp la depreca cuando vencen los envíos abiertos. Se agrega una prueba de integración.
- **`ChannelUnavailable`** sale del chequeo y de la aceptación; queda sólo para el estado de una publicación vieja cuyo canal ya
  no está instalado.
- **La migración** suma las dos columnas (definición y versión). El snapshot generado se revisa por el CRLF escapado de
  `CK_BusinessFormCatalogs_Proposal`.
- **Docs:** una sección nueva en [formularios](../features/formularios.md) (y se corrige «Quitar módulo produce
  ChannelUnavailable»), «Publicación» en [el detalle de los servicios](../features/formularios-servicios.md), la selección en
  [WhatsApp](../features/whatsapp.md) y [quitar WhatsApp](../guides/quitar-whatsapp.md): Formularios sigue entero y publica en
  la app.
- **Contrato con el front:** se regeneran los ejemplos HTTP de publicación, detalle y listado con el exportador.

### Front

- El ajuste, con `SegmentedControl` (OE1-A) en un `FormField` con su `InfoTip`, sólo con `form-channels`; el borrador lo manda
  en `appOnly`. En el celular, la opción que no entra baja de renglón.
- Con «Sólo en la app»: sin `offerWhen`, sin `FormSuggestionsSection`, sin «Ver vista oficial», sin las menciones de WhatsApp
  de las (i) y de la línea de «Elegir primero», y la vista previa con las piezas de la carga en el panel en lugar de
  `PhonePreview`.
- «Publicar» o «Publicar en WhatsApp»; el diálogo con su título y la fila «Dónde se completa»; un recibo `Published` termina en
  el aviso «Publicamos la versión N.» sin pasar por «Publicación aceptada».
- El listado con «Estado» y la columna «Dónde se completa»; la baja y la eliminación como arriba.
- Textos en es y en; vitest del editor, el diálogo y el listado; escenas de test:visual del editor y del diálogo «Sólo en la
  app» a 1440, 390 y 320 px; contratos HTTP con los ejemplos nuevos.

## Cortes

1. **Backend**, con TDD: Domain (la operación que nace publicada y el sello de la versión), el chequeo, la aceptación, las
   lecturas, la selección, la capacidad, la migración (`AddFormAppOnly`) y las docs. **Programado el 2026-11-09.** Los ejemplos
   HTTP no cambian para lo que existía; los de un formulario de la app se exportan con el front.
2. **Front**, con los ejemplos nuevos y el front quieto (sin la app levantada ni otra sesión probando): editor, vista previa de
   la app, diálogo, listado, baja y eliminación. **Programado el 2026-11-11** (front c91d85f7 a da913f14), con un
   `SegmentedControl` que en el celular baja de renglón (`wrap`) y las escenas de `formAppOnly.spec.ts`. Los ejemplos HTTP de un
   formulario de la app pasaron al cierre: la exportación conjunta corre la suite entera y copia las 25 cohortes de una sola
   corrida, y ese día otras sesiones tenían cambios sin commitear en los dos repos y la PC al 100 %. La revisión contra el
   código del mismo día trajo tres arreglos, ya hechos: «Qué sugerir después» con un destino que pasó a la app (`AppOnly`), el
   motivo de lo apagado en la vista previa (MA1-A) y sin «En WhatsApp» en una pregunta de catálogo (backend ab66934c, front
   b73170c6 a 1bee2f51). La baja de uno que pasó a la app quedó decidida («App» y aviso) y programada (front 28e2f70d y ef91cc2b).
   El 2026-11-12, la sesión de documentos encontró que las respuestas de una versión de la app daban 500 al leerlas (los idiomas
   salían de los perfiles del canal): arreglado en 2ebeed2d, con un test que publica por la aceptación real. El revisor de
   documentos vio que la revisión de un 409 no comparaba «Dónde se completa»: ya es un cambio más (front 66262d60 y ef2531d9).
3. **Cierre:** la exportación conjunta de los ejemplos HTTP con los de un formulario de la app (las cinco capturas de
   `FormAppOnlyPublicationTests` en `forms-http/app-only`, escritas el 2026-11-11, y después su consumidor en el front); la
   corrida completa con Docker y el front entero (build, lint, vitest y test:visual); la prueba a mano (crear un
   formulario de la app, publicarlo, cargarlo, pasarlo a WhatsApp con Meta y volverlo a la app) y volver a probar «Quitar
   WhatsApp» en una copia. Las decisiones pasan a formularios y al front, y el plan se retira.

## Coordinación

- Nada se programa sin el visto bueno del usuario. Mientras dura la prueba integral no se edita el `src` del front.
- Carpetas en el menú lateral (CP5-A): no cambian; muestran los formularios activos sin mirar dónde se completan.
- El motivo de lo apagado (MA1-A): «Publicar» apagado sigue diciendo su motivo sobre el control.
- Un dato por columna (UD): ver «En el listado» arriba; los dos cambios tocan `FormsPage.tsx` y van uno después del otro.
- Al cerrar, se suma a un traspaso al multitenant.

## Fuera de alcance

- «Sólo WhatsApp»: lo publicado por WhatsApp también se carga en la app.
- Cambiar los límites propios de la app (40 preguntas, 20 opciones en «Elegir varias», 10 archivos, 100 ítems).
- Que la app no espere a Meta (SA4-B), descartado.
- Copiar formularios, que sigue esperando su tablero.
