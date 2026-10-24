# Anexo D del traspaso 2: los tableros desde el 2026-10-17

Fecha: 2026-10-23. Este anexo sigue al [anexo D del traspaso del 2026-10-17](../../../../ArquitecturaBaseMutitenant/docs/plans/2026-10-17-formularios-traspaso/anexo-d-tableros.md).

Junta todos los tableros de la página «Formularios por WhatsApp» del lienzo que se dibujaron o se eligieron desde el 2026-10-17.

Son 55 tableros: 38 elegidos, 15 alternativas retiradas del lienzo, una alternativa que sigue en el lienzo (CE3-A) y una referencia (MF0). Al final va el recorrido guiado, que se eligió el 17.

Para cada tablero van:
- su estado;
- la fecha y la versión del lienzo;
- si está programado;
- qué muestra;
- el texto de sus notas;
- los choques con las reglas del 2026-10-23;
- dónde verlo.

## Cómo leerlo

**Los textos de los tableros salen de sus `.dc.html`, tal como se publicaron.** Están en la copia local del lienzo: `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\` y `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\`. Esa carpeta no tiene remoto.

Van textuales, sin reescribir:
- las notas numeradas;
- «Para comparar»;
- los casos numerados de los tableros de casos;
- las notas al pie;
- los títulos y los rótulos de las secciones de abajo.

Lo único escrito para este anexo es **«Qué muestra»**: resume la pantalla grande zona por zona. Los textos que están dentro de las capturas reducidas no se transcriben. En las alternativas retiradas tampoco va la lista de secciones de abajo: alcanzan su resumen, sus notas y «Para comparar».

**Hay tres estados:**
- **elegido;**
- **alternativa no elegida**, que sigue en el lienzo;
- **retirado:** una alternativa no elegida que se sacó del lienzo el 2026-10-22, en la versión 119. Un Artifact admite 512 archivos, y había que hacer lugar. Los sacó `gen/retirar_descartados.py`, y el archivo queda en `retirados/`.

**Las notas guía** del lienzo dicen «por programar» en todas las familias: no se actualizaron cuando se programaron. Si algo está programado o no, sale de los documentos del front y del back.

**Las versiones** son las que anotan los documentos del front y las notas guía del lienzo. Hasta donde registran el README del generador y el diseño del front, la última es la 119, del 2026-10-22. No la comparé con el Artifact publicado.

**Dónde se ve cada cosa:**
- **El lienzo:** el [Artifact del lienzo](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ), página «Formularios por WhatsApp». Es privado y cambia fuera de Git.
  - Lo arma el generador `C:\Users\ezequ\source\repos\lienzo-formularios\gen\`, que se explica en el `README.md` de esa carpeta.
  - Cada familia tiene en el lienzo una nota guía con su fecha y lo elegido. Va copiada al comienzo de cada sección.
- **El prototipo navegable:** el [Artifact del prototipo](https://claude.ai/artifact/2noVifPc2MX33UnNvT2jhY). Lo arman `prototipo.py`, `prototipo_catalogos.py` y `prototipo_campos.py` del generador. Tiene tres partes:
  - lo elegido el 2026-10-20: el menú MF1, dar de baja, D2 y DC1 ajustado;
  - los siete recorridos de catálogos;
  - los seis de campos extra.
- **La maqueta viva:** el [Artifact de la maqueta](https://claude.ai/artifact/CM56W2Yc4VVzwMmxCKFpdP), hecho con el repo `C:\Users\ezequ\source\repos\maqueta-formularios`, que no tiene remoto.
  - Es el front real, en modo demostración, con un backend falso en el navegador. Usa la copia del front `9f0d270`, del 2026-10-22.
  - Lo elegido que todavía no está programado se ve dentro de las pantallas reales, con la marca «Propuesta · <tablero>».
  - La barra de abajo cambia de persona y de idioma, abre el celular y tiene «Recorridos» CA6, CE6, AS6 y DOC.
  - En este anexo, «pantalla real» quiere decir que el tablero ya está programado y se ve como en la app.
- **Las evidencias del front** congelan lo elegido. Están en `ArquitecturaBaseFront/docs/design/evidence/` y se explican en:
  - [formularios-whatsapp.md](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md);
  - [asistente-casos.md](../../../../ArquitecturaBaseFront/docs/design/asistente-casos.md);
  - [catalogos-mejoras.md](../../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md);
  - [catalogos-campos-extra.md](../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md);
  - [documentos.md](../../../../ArquitecturaBaseFront/docs/design/documentos.md).

## Reglas del 2026-10-23: los tableros elegidos que hay que dibujar corregidos

**Las reglas son decisiones del usuario del 2026-10-23.** Están en estas fuentes:
- [`visual-baseline.md`](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#formularios-y-diálogos), en «Encabezados» y «Formularios y diálogos»;
- los puntos 35 a 39 del [review de catálogos E2](../../reviews/2026-10-23-code-review-catalogos-e2.md#arreglar-ahora);
- el README del generador.

**Qué dicen:**
1. **Botones:** un botón está habilitado o deshabilitado. No lleva textos debajo ni al costado, como «No hay cambios para guardar», «Revisá los datos marcados» o «Guardá … antes». No cambia de lugar ni de forma. Si el motivo no es obvio, va como tooltip. Los errores se marcan en sus campos.
2. **Estados:** no hay «Cambios sin guardar» ni números de revisión en los encabezados ni junto a los botones.
   - El estado del título es solo el del objeto, como «Activo» o «Dado de baja».
   - Los historiales de revisiones se conservan.
   - Se conserva el aviso al salir con cambios sin guardar.
3. **Idioma:** no hay selector de idioma («Textos en») en ninguna pantalla.
   - Cada texto tiene un solo campo, en el idioma del menú, que es el original. No hay «Nombre en inglés».
   - Para traducir, se cambia el idioma del menú y se edita lo mismo.
   - Un campo sin traducción muestra el original como referencia, sin copiarlo.
   - Las vistas previas van en el idioma del menú.
4. **«El catálogo»:** tiene solo Nombre y Descripción, y la descripción es opcional. De dónde salen las opciones y el tipo de valor van arriba, como dato de solo lectura: «Escritas a mano · Texto».
5. **También:** las acciones de una superficie van al borde derecho de su encabezado, a la altura del título. Se valida al intentar guardar, no al agregar una fila vacía.

**Los tableros quedaron como se publicaron.** El generador ya aplica estas reglas a los tableros nuevos (commits `63ce357` y `5d9f25b` del 2026-10-23), pero no republicó ninguno.

**La maqueta tampoco las tiene:** usa la copia del front del 2026-10-22. En el código del front, el review E2 las deja para arreglar. No verifiqué cuánto se aplicó.

**Cómo las marqué:** busqué en el texto de cada tablero «Cambios sin guardar», «Sin guardar», «Textos en», «Nombre en inglés», «De dónde salen», «Tipo de valor», «Guardá…», «Completá … en español e inglés» y «revisión N». Los textos al lado de un botón los busqué leyendo las pantallas: puede haber más. Marco «a revisar» lo que es dudoso.

**Elegidos que hay que dibujar corregidos:**

| Tablero | Qué muestra que ya no va |
| --- | --- |
| I1 | Todo el tablero: el selector «Textos en», los campos en inglés con «En español: …» y la vista previa de una cuenta en inglés. |
| I2 | «Completar el inglés» sin selector; a revisar «Se publica el borrador guardado hoy a las 15:20.» al lado de los botones. |
| D2 y DC1 ajustado | A revisar «Reglas de la revisión 3» en Actividad; D2 muestra además la navegación de antes de MF1. |
| BA2 y BA4 | El motivo de una acción apagada, escrito en el menú «⋯» («No se puede: tiene 24 respuestas»). |
| BA6 | «Quitala para poder guardar las sugerencias» en la fila, que explica el Guardar apagado. |
| AS2 | El texto al lado de «Agregar botón» y el «texto exacto» con dos campos por idioma. |
| AS3 | El aviso «Tenés cambios sin guardar: la prueba usa lo guardado». |
| CA1-A, CA2 y CA3 | «Textos en», «El catálogo» con «De dónde salen» (y en CA3, «Tipo de valor»), «Guardá el catálogo para pegar o importar…»; en CA3, «Activo · Cambios sin guardar». |
| CE1 y CE2 | Lo mismo que CA, más «Activo · Cambios sin guardar» y, en CE1, «Nombre en inglés»; tampoco tienen el ajuste del review de E2. |
| CE3-B | La sección del catálogo que toma de CE3-A trae lo de CA; le falta el ajuste del review de E2. |

**Sin choques visibles:** I3, MF1, DC0, BA1-A, BA3, BA5, AS1-A, AS4–AS6, CA4–CA6, CE4-A, CE5, CE6 y DOC1–DOC7. En CE4-A falta dibujar el diálogo para definir un dato, con un solo campo de nombre.

## Todos los tableros, en una tabla

La columna «Prototipo» dice si el tablero está en el prototipo navegable. La columna «Maqueta» dice cómo aparece ahí: «Real» si ya está programado y se ve como en la app, «Propuesta» si lleva la marca, «Recorrido» si está en «Recorridos».

| Tablero | Estado | Fecha · versión | Programado | Evidencia | Prototipo | Maqueta |
| --- | --- | --- | --- | --- | --- | --- |
| I1 | Elegido 18/10 | 18/10 · v85 | Sí (19/10) | Sí | — | Real |
| I2 | Elegido 18/10 | 18/10 · v85 | Sí (19/10) | Sí | — | Real |
| I3 | Elegido 18/10 | 18/10 · v85 | Sí (19/10) | Sí | — | Real |
| D1 | No elegido · retirado | 17/10 · v79 | No | — | — | — |
| D2 | Elegido 17/10 | 17/10 · v79 | Sí (21/10) | Sí | Sí | Real |
| D3 | No elegido · retirado | 17/10 · v79 | No | — | — | — |
| D4 | No elegido · retirado | 17/10 · v79 | No | — | — | — |
| MF0 | Referencia | 20/10 · v88 | — | — | — | — |
| MF1 | Elegido 20/10 | 20/10 · v88 | Sí (21/10) | Sí | Sí | Real |
| MF2 | No elegido · retirado | 20/10 · v88 | No | — | — | — |
| MF3 | No elegido · retirado | 20/10 · v88 | No | — | — | — |
| DC0 | Elegido 20/10 | 20/10 · v94 | Sí (21/10) | Sí | Sí | Real |
| DC1 ajustado | Elegido 20/10 | 20/10 · v94 | Sí (21/10) | Sí | Sí | Real |
| DC1 | No elegido · retirado | 20/10 · v94 | No | — | — | — |
| DC2 | No elegido · retirado | 20/10 · v94 | No | — | — | — |
| DC3 | No elegido · retirado | 20/10 · v94 | No | — | — | — |
| BA1-A | Elegido 20/10 | 20/10 · v88 | Sí (21/10) | Sí | Sí | Real |
| BA1-B | No elegido · retirado | 20/10 · v88 | No | — | — | — |
| BA2 | Elegido 20/10 | 20/10 · v88 | Sí (21/10) | Sí | Sí | Real |
| BA3 | Elegido 20/10 | 20/10 · v88 | Sí (21/10) | Sí | Sí | Real |
| BA4 | Elegido 20/10 | 20/10 · v88 | Sí (21/10) | Sí | Sí | Real |
| BA5 | Elegido 20/10 | 20/10 · v88 | Circuito | Sí | Sí | Real (sin recorrido) |
| BA6 | Elegido 20/10 | 20/10 · v96 | Sí (21/10) | Sí | Sí | Real |
| AS1-A | Elegido 21/10 | 21/10 · v98 | No | Sí | — | Propuesta |
| AS1-B | No elegido · retirado | 21/10 · v98 | No | — | — | — |
| AS1-C | No elegido · retirado | 21/10 · v98 | No | — | — | — |
| AS2 | Elegido 21/10 | 21/10 · v98 | No | Sí | — | Propuesta |
| AS3 | Elegido 21/10 | 21/10 · v98 | No | Sí | — | Propuesta |
| AS4 | Elegido 21/10 | 21/10 · v98 | No | Sí | — | Propuesta |
| AS5 | Elegido 21/10 | 21/10 · v98 | No | Sí | — | Propuesta |
| AS6 | Elegido 21/10 | 21/10 · v98 | No | Sí | — | Recorrido |
| CA1-A | Elegido 21/10 | 21/10 · v102 | Sí (22/10) | Sí | Sí | Real |
| CA1-B | No elegido · retirado | 21/10 · v102 | No | — | — | — |
| CA2 | Elegido 21/10 | 21/10 · v102 | Sí (22/10) | Sí | Sí | Real |
| CA3 | Elegido 21/10 | 21/10 · v102 | Sí (22/10) | Sí | Sí | Real |
| CA4 | Elegido 21/10 | 21/10 · v102 | Sí (22/10) | Sí | Sí | Real |
| CA5 | Elegido 21/10 | 21/10 · v102 | Sí (22/10) | Sí | Sí | Real |
| CA6 | Elegido 21/10 | 21/10 · v102 | Circuito | Sí | Sí | Recorrido |
| CE1 | Elegido 21/10 | 21/10 · v106 | Sí (23/10) | Sí | Sí | Propuesta |
| CE2 | Elegido 21/10 | 21/10 · v106 | Sí (23/10) | Sí | Sí | Propuesta |
| CE3-A | No elegido (en el lienzo) | 21/10 · v106 | En CE3-B | Sí | Sí | En CE3-B |
| CE3-B | Elegido 21/10 | 21/10 · v106 | Sí (23/10) | Sí | Sí | Propuesta |
| CE4-A | Elegido 21/10 | 21/10 · v106 | Sí (23/10) | Sí | Sí | Propuesta |
| CE4-B | No elegido · retirado | 21/10 · v106 | No | — | — | — |
| CE5 | Elegido 21/10 | 21/10 · v106 | Sí (23/10) | Sí | Sí | Propuesta |
| CE6 | Elegido 21/10 | 21/10 · v106 | Circuito | Sí | Sí | Recorrido |
| DOC1 | Elegido 22/10 | 22/10 · v116 | No | Sí | — | Propuesta |
| DOC2 | Elegido 22/10 | 22/10 · v116 | No | Sí | — | Propuesta |
| DOC3 | Elegido 22/10 | 22/10 · v116 | No | Sí | — | Propuesta |
| DOC4 | Elegido 22/10 | 22/10 · v116 | No | Sí | — | Propuesta |
| DOC5-A | Elegido 22/10 | 22/10 · v116 | No | Sí | — | Propuesta |
| DOC5-B | No elegido · retirado | 22/10 · v116 | No | — | — | — |
| DOC6 | Elegido 22/10 | 22/10 · v116 | No | Sí | — | Propuesta |
| DOC7-A | Elegido 22/10 | 22/10 · v116 | No | Sí | — | Propuesta |
| DOC7-B | No elegido · retirado | 22/10 · v116 | No | — | — | — |

## Idioma de los formularios (I1–I3)

Dibujados y elegidos el 2026-10-18, en la versión 85 del lienzo. Reemplazan lo de E (dos idiomas) y la nota 4 de P1 del traspaso anterior: solo el idioma original es obligatorio y, si la traducción no está completa, todo sale en el original.

Programados y verificados localmente el 2026-10-19 ([regla vigente](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19)). El piloto físico con Meta sigue pendiente.

**Ojo:** el 2026-10-23 el usuario decidió que no hay selector de idioma en ninguna pantalla (punto 35 del [review E2](../../reviews/2026-10-23-code-review-catalogos-e2.md#arreglar-ahora)). El review dice que eso cambia el editor de esta etapa. I1 hay que redibujarlo; I2 e I3 se pueden tomar casi como están.

**Diseño congelado en el front:** [formularios-whatsapp.md](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros).

**Nota guía de la sección «Idioma de los formularios · el editor y publicar» en el lienzo** (textual):

> Idioma de los formularios (decidido el 2026-10-16). I1–I3 elegidos el 2026-10-18; por programar.
>
> Solo el idioma original es obligatorio para guardar y publicar; la traducción es opcional. Si no está completa, el formulario, el PDF, los mensajes y las opciones de catálogo salen enteros en el original, sin mezclar.
>
> I1 · El editor: «Textos en» con el inglés opcional, «3 sin traducir» en gris y la vista previa de lo que recibe cada cuenta.
> I2 · Publicar: la comprobación ya no frena por una traducción que falta y dice qué idiomas van a WhatsApp.
> I3 · Los idiomas al publicar: traducción incompleta o completa, y una versión que suma o pierde el inglés.
>
> Reemplazan la nota 5 de E («Guardar sí, publicar no») con su aviso al publicar, y la nota 4 de P1 («en los dos idiomas»).

#### I1 · El editor: la traducción es opcional

Archivo `FWA-I1-Editor.dc.html` («Formularios · Idioma · I1 · editor»).

- **Estado:** Elegido el 2026-10-18.
- **Fecha y versión del lienzo:** 2026-10-18, versión 85.
- **Programado:** Sí, el 2026-10-19 (con el selector «Textos en»).
- **Qué muestra:**
  - **Arriba:** el editor de «Registrar visita» en borrador, con Descartar, Guardar borrador y Publicar en WhatsApp.
  - **Debajo del título:** el selector «Textos en», con «Español · original» y «English · 3 sin traducir», y una aclaración debajo.
  - **«El formulario»:** está en inglés. Cada campo lleva «En español: …» debajo. El prefijo VIS es «el mismo en los dos idiomas» y «se cambia desde Español».
  - **«Preguntas»:** «Sin traducir» en gris en las filas que les falta algo. «Notes» está abierta, con la ayuda sin traducir.
  - **A la derecha:** «Así le llega a una cuenta en inglés». Con 3 textos sin traducir, el teléfono sale entero en español. Completo, sale entero en inglés.
- **Título y bajada de las notas:** «I1 · El editor: la traducción es opcional». Ajuste del tablero E por la decisión del 2026-10-16: para guardar y publicar alcanza con el idioma original.
- **Notas numeradas:**
  1. **«Textos en».** El original sigue marcado como tal y es obligatorio. El otro idioma es opcional: el selector dice cuánto falta, «3 sin traducir», sin alarma (antes «faltan 3» o «N pendientes»).
  2. **Lo que falta se ve, sin frenar.** «Sin traducir» en gris en la fila y en el campo, con el original debajo para guiar. No es un error: se guarda y se publica igual.
  3. **La vista previa no mezcla.** Muestra lo que recibe una cuenta en inglés. Con la traducción incompleta, el formulario entero en español; completa, entero en inglés. Ya no aparece el original marcado entre textos en inglés.
  4. **Todo en un mismo idioma.** El formulario, el mensaje con «Abrir formulario», las opciones de los catálogos, los avisos y el PDF salen en el mismo idioma. Si falta un texto, todo en el original.
  5. **Publicar no pide traducciones.** El botón publica igual. La confirmación dice qué idiomas van a WhatsApp (I2).
- **En las notas, «Al completar la traducción»:** Con «Español · original» y «English · completo» en el selector: «La vista previa pasa a inglés y la confirmación de publicar suma el inglés. Si el formulario ya estaba publicado, el inglés llega con la versión siguiente.»
- **Reglas del 2026-10-23:**
  - **Selector de idioma:** todo el tablero gira en torno a «Textos en», con su conteo y su aclaración. Los campos se cargan en inglés, con «En español: …» debajo. Desde el 2026-10-23 no hay selector:
    - cada texto tiene un solo campo, en el idioma del menú, que es el original;
    - para traducir se cambia el idioma del menú y se edita lo mismo;
    - un campo sin traducción muestra el original como referencia, sin copiarlo;
    - la vista previa va en el idioma del menú. La de I1 muestra lo que recibe una cuenta en inglés.
  - **Lo que sigue valiendo:** el original es obligatorio y la traducción, opcional. Lo que se manda nunca mezcla idiomas.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-I1-Editor.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idi-i1-editor.png) · [Front, es, 320 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i1-es-320.jpg) · [Front, preview en inglés, 390 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i1-preview-en-390.jpg) · [Front, traducción completa, 320 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i1-complete-es-320.jpg).
  - **Maqueta:** Sí, como pantalla real: la copia del front que usa la maqueta (9f0d270, del 2026-10-22) ya tiene I1 programado, con «Textos en».

#### I2 · Publicar con la traducción incompleta

Archivo `FWA-I2-Publicar.dc.html` («Formularios · Idioma · I2 · publicar»).

- **Estado:** Elegido el 2026-10-18.
- **Fecha y versión del lienzo:** 2026-10-18, versión 85.
- **Programado:** Sí, el 2026-10-19.
- **Qué muestra:**
  - **Detrás:** el editor de «Registrar visita» en español.
  - **Adelante:** el diálogo «Publicar la versión 1 en WhatsApp», con tres partes:
    - **«Qué incluye»:** las 4 preguntas y «En WhatsApp, en español», que es el original y le llega también a las cuentas en inglés. Debajo, el aviso «El inglés no se publica. Le faltan 3 textos…», con la lista de lo que falta y «Completar el inglés»;
    - **«Qué queda fijo»:** las preguntas de la versión 1 y el prefijo VIS;
    - **«Qué va a pasar»:** tres pasos.
  - **Pie:** «Se publica el borrador guardado hoy a las 15:20.», Cancelar y «Publicar versión 1».
- **Título y bajada de las notas:** «I2 · Publicar con la traducción incompleta». Ajuste de P1: la comprobación ya no frena por una traducción que falta. Dice qué idiomas se publican en WhatsApp y cuál no.
- **Notas numeradas:**
  1. **Qué se publica en WhatsApp.** El original siempre, y la traducción solo si está completa. A Meta va el formulario en esos idiomas y en ningún otro.
  2. **Avisa, no frena.** Nombra la traducción que no se publica y lo que le falta. «Completar el inglés» vuelve al editor en inglés; si no, se publica igual.
  3. **Sumarla después es otra versión.** Como siempre, quedan fijas las preguntas de la versión. Completar el inglés cambia sus textos: se publica como versión 2.
  4. **Publicar versión 1.** Habilitado aunque falten traducciones. Frena solo lo que ya frenaba: cambios sin guardar, una pregunta que WhatsApp no puede mostrar o la cuenta de WhatsApp.
- **En las notas, «Al terminar»:** Con la etiqueta «Publicado · versión 1»: «El aviso dice el idioma: «Publicamos la versión 1, en español». La vista oficial de WhatsApp (P3) ofrece solo los idiomas publicados.»
- **Reglas del 2026-10-23:**
  - **Texto junto a los botones, a revisar:** en el pie, «Se publica el borrador guardado hoy a las 15:20.» queda al lado de los botones. Viene de P1.
  - **Sin selector:** «Completar el inglés» «vuelve al editor en inglés». Sin selector, tiene que llevar al editor con el menú en inglés. Cómo se hace no está decidido.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-I2-Publicar.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idi-i2-publicar.png) · [Front, es, 390 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i2-es-390.jpg).
  - **Maqueta:** Sí, como pantalla real (publicar ya programado).

#### I3 · Los idiomas al publicar

Archivo `FWA-I3-Idiomas.dc.html` («Formularios · Idioma · I3 · los idiomas al publicar»).

- **Estado:** Elegido el 2026-10-18.
- **Fecha y versión del lienzo:** 2026-10-18, versión 85.
- **Programado:** Sí, el 2026-10-19.
- **Qué muestra:**
  - **Tablero de casos:** cuatro diálogos de confirmación, uno por caso numerado:
    - con la traducción incompleta;
    - con la traducción completa;
    - una versión 2 que suma el inglés;
    - una versión 3 que lo pierde, con aviso.
  - **Abajo:** tres notas al pie.
- **Encabezado del tablero:** «I3 · Los idiomas al publicar». Qué dice la confirmación de publicar según la traducción, también cuando una versión nueva suma o pierde un idioma.
- **Casos numerados (son las notas numeradas de este tablero):**
  1. **La traducción está incompleta.** Se publica el original. La traducción no se publica y se dice qué le falta.
  2. **La traducción está completa.** Se publican los dos idiomas y cada cuenta lo recibe en el suyo.
  3. **Una versión que suma el inglés.** Completar la traducción de un formulario publicado cambia sus textos: va en la versión siguiente.
  4. **Una versión que deja de tener inglés.** Si la versión anterior estaba en inglés y la nueva tiene textos sin traducir, el inglés deja de publicarse. Se avisa antes.
- **Notas al pie:**
  - **Frena solo lo que ya frenaba.** Cambios sin guardar, una pregunta que WhatsApp no puede mostrar o la cuenta de WhatsApp. Una traducción que falta, no.
  - **Un idioma por cuenta.** Cada cuenta recibe todo en un solo idioma: el suyo si la traducción está completa; si no, el original.
  - **Lo publicado no cambia solo.** Sumar o perder una traducción es un cambio de textos: va en una versión nueva, y la anterior sigue andando hasta entonces.
- **Reglas del 2026-10-23:** Sin choques en lo dibujado: son diálogos de publicación que nombran los idiomas. El review E2 solo cambia el editor.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-I3-Idiomas.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idi-i3-idiomas.png) · [Front, suma inglés, 320 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i3-add-en-320.jpg) · [Front, pierde inglés, 390 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i3-remove-es-390.jpg).
  - **Maqueta:** Sí, como pantalla real (publicar ya programado).

## Detalle de una respuesta (D1–D4, elegido D2)

Dibujados el 2026-10-17 y elegido D2 ese mismo día, en la versión 79 del lienzo. En el anexo del 17 figuraban solo con su título; acá van con sus notas.

El tablero D2 que está hoy en el lienzo es el del 2026-10-20: tiene el ajuste de la versión 96, con «Por aprobar · N» y el pie que lo dice. Desde ese día, D2 es la vista rápida de escritorio, y la pantalla completa es DC1 ajustado (ver la sección siguiente).

D1, D3 y D4 se retiraron del lienzo el 2026-10-22, en la versión 119.

**Diseño congelado en el front:** [formularios-whatsapp.md](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros).

**Nota guía de la sección «Detalle de una respuesta · cuatro conceptos» en el lienzo** (textual):

> Detalle de una respuesta · cuatro conceptos (2026-10-17). Elegido D2 en la versión 79: reemplaza el detalle de T3, G4 y G5, que hoy es una sola columna larga, con el estado escondido, cada valor debajo de su pregunta y el historial desordenado.
>
> D1 · Pestañas con barra de estado: cabecera con Descargar ▾ y Aprobar/Rechazar; pestañas Respuesta, Fotos, Actividad y PDF. Con los cuatro casos: sin reglas, solo un aviso al recibirla, aprobada con PDF y avisos, y rechazada con un aviso «Sin confirmar».
> D2 · Panel lateral sobre la planilla: se abre al tocar una fila y ↑ ↓ pasan a la respuesta siguiente; en el celular ocupa la pantalla (elegido el 2026-10-17). Desde el 2026-10-20 es la vista rápida: la pantalla completa es DC1 ajustado, al final de la página. Entrando por «Por aprobar», ↑ ↓ recorren lo que espera.
> D3 · El comprobante primero: se lee como el papel firmado, con los botones al costado y la actividad en un panel aparte.
> D4 · La historia como columna: una línea de tiempo con las respuestas y el PDF adentro de cada hecho.
>
> En todos: el estado arriba, Aprobar y Rechazar mientras espera decisión, Descargar, «pregunta · valor» con las fotos, «Abrir conversación» y el celular. La barra (o la línea) no tiene pasos fijos: se arma con las reglas que tenía el formulario cuando llegó la respuesta. «Las 3 fotos juntas» es una idea para después.
>
> D1, D3 y D4, los conceptos que no se eligieron, se sacaron del lienzo el 2026-10-22 para hacer lugar: siguen en el repo lienzo-formularios, en retirados/.

#### D1 · Pestañas con barra de estado

Archivo `FWA-D1-Pestanas.dc.html` («Formularios · detalle · D1 · pestañas con barra de estado»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-17, versión 79.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** el detalle a página completa, con una barra de momentos arriba y las pestañas Respuesta, Fotos, Actividad y PDF.
  - **Abajo:** cuatro casos de barra según las reglas, las otras pestañas y el celular.
- **Título y bajada de las notas:** «D1 · Pestañas con barra de estado». Arriba, en qué está la respuesta y qué se puede hacer; la barra cuenta cómo llegó hasta ahí; las pestañas separan lo que respondió de lo que pasó después.
- **Notas numeradas:**
  1. **El estado, arriba.** La etiqueta junto al número. Aprobar y Rechazar se suman a la cabecera solo mientras espera decisión y para quien puede decidir (abajo, «C, mientras esperaba»).
  2. **La barra no tiene pasos fijos.** Se arma con la revisión de reglas con que llegó la respuesta. Muestra momentos: Recibida con su origen, Por aprobar → Aprobada o Rechazada, y Al recibirla, Al aprobarla o Al rechazarla con el resumen de sus acciones. Tocar un momento abre Actividad ahí.
  3. **Pestañas, las que hagan falta.** Respuesta, Fotos si hay fotos, Actividad y PDF cuando hay copia. Son una pieza nueva de la biblioteca: el activo se marca como en el menú, con la barra de la marca.
  4. **Pregunta · valor.** En un renglón, como en el PDF. Las miniaturas abren el visor de F4; el remito se descarga con su nombre.
  5. **Descargar ▾.** El comprobante PDF y los documentos. «Las 3 fotos juntas» es una idea para después: hoy cada foto se descarga desde el visor.
  6. **De dónde vino.** Quién la cargó, por dónde y quién se lo mandó: una persona desde el chat o el asistente. «Abrir conversación» aparece con permiso de ver el chat.
- **Para comparar:**
  - **A favor:** cada cosa tiene su lugar y la barra resume sin abrir nada.
  - **En contra:** fotos, actividad y PDF quedan a un toque de distancia; las pestañas son una pieza nueva.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-D1-Pestanas.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### D2 · Panel lateral sobre la planilla

Archivo `FWA-D2-Panel.dc.html` («Formularios · detalle · D2 · panel lateral»).

- **Estado:** Elegido el 2026-10-17. Desde el 2026-10-20 es la vista rápida de escritorio.
- **Fecha y versión del lienzo:** 2026-10-17, versión 79. Ajuste del 2026-10-20, versión 96.
- **Programado:** Sí: como detalle único el 2026-10-18 y como vista rápida el 2026-10-21.
- **Qué muestra:**
  - **Detrás:** la planilla de «Registrar visita», con VIS-000024 marcada. El panel de navegación es el viejo, con «Por aprobar 3» y «Respuestas» por formulario. MF1 lo reemplazó.
  - **Panel de 520 px a la derecha, de arriba abajo:**
    - el número, «Aprobada», ↑ ↓ y cerrar;
    - «Registrar visita · versión 3», con Abrir conversación y Descargar ▾;
    - los momentos, uno por renglón: Recibida, Al recibirla 1 de 1, Aprobada · Laura Ríos y Al aprobarla 2 de 2;
    - las pestañas Respuesta, Fotos 3, Actividad y PDF;
    - «pregunta · valor», con las fotos y el remito;
    - el pie «↑↓ pasan de respuesta · Esc cierra el panel».
  - **Abajo:** mientras espera decisión, la pestaña Actividad y el celular.
- **Título y bajada de las notas:** «D2 · Panel lateral sobre la planilla». La respuesta se abre al costado sin salir de la planilla. Sirve para revisar muchas seguidas, una detrás de otra.
- **Notas numeradas:**
  1. **Tocar una fila.** Abre el panel y marca la fila. La planilla queda a la vista con sus filtros y su orden; el panel tapa las columnas de la derecha.
  2. **↑ ↓.** Pasan a la respuesta anterior o siguiente de la planilla, también con las flechas del teclado. Si se entró por «Por aprobar · N», recorren lo que espera, de la más vieja a la más nueva, y el pie lo dice. Escape cierra y el foco vuelve a la fila.
  3. **El estado, arriba.** La etiqueta junto al número y los momentos en lista, uno por renglón: en 520 px no entran en fila. Mientras espera, Aprobar y Rechazar van en el renglón «Por aprobar».
  4. **Las mismas pestañas.** Respuesta, Fotos, Actividad y PDF, más angostas. La dirección lleva la respuesta abierta: se puede compartir. Se abre igual sobre Por aprobar (G3).
  5. **Acciones.** Abrir conversación y Descargar ▾, arriba del estado.
- **Para comparar:**
  - **A favor:** revisar una tanda sin ir y volver, con el contexto de la planilla.
  - **En contra:** poco ancho para fotos, PDF y formularios largos; en el celular no hay costado y queda igual que una página.
- **Abajo, sección por sección:**
  - **Mientras espera decisión, la actividad y el celular.** Aprobar y Rechazar van en el renglón del momento actual. Las pestañas funcionan igual dentro del panel.
- **Reglas del 2026-10-23:**
  - **Número de revisión, a revisar:** la pestaña Actividad, en el caso de abajo, dice «Reglas de la revisión 3». La regla saca los números de revisión de los encabezados. Este número dice con qué reglas llegó la respuesta, no es un estado de guardado: hay que decidir si queda.
  - **Navegación vieja:** el panel de navegación que se ve detrás no va. Va el de MF1.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-D2-Panel.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/det-d2-panel.png) · [Front, D2 escritorio, en](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/response-full-quick-en-1440.png) · [Front, migas de D2](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/d2-breadcrumb-es-desktop.png).
  - **Prototipo navegable:** Sí: «Formularios», entrá en Registrar visita y revisá VIS-000024 en D2.
  - **Maqueta:** Sí, como pantalla real.

#### D3 · El comprobante primero

Archivo `FWA-D3-Comprobante.dc.html` («Formularios · detalle · D3 · el comprobante primero»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-17, versión 79.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** la respuesta se lee como el comprobante PDF, con la firma, los botones al costado y la actividad en un panel aparte.
  - **Abajo:** mientras espera decisión, la actividad y el celular.
- **Título y bajada de las notas:** «D3 · El comprobante primero». La respuesta se lee como el papel que firma quien aprueba. Los botones van al costado y lo que pasó se abre aparte.
- **Notas numeradas:**
  1. **El estado, arriba.** La etiqueta junto al número y una línea con los momentos. Tocar uno abre la actividad en ese momento.
  2. **Se lee como el comprobante.** El orden y el aspecto del PDF de G2, con todas las preguntas. Es la respuesta, no una imagen: las fotos abren el visor y el remito se descarga.
  3. **La firma.** Abajo a la derecha, quién la aprobó y cuándo. Mientras espera, «Se completa al aprobarla»; si la rechazan, el motivo en su lugar.
  4. **Botones al costado.** Aprobar y Rechazar arriba, mientras espera decisión. Después, Descargar PDF, Abrir conversación y Actividad. Quedan fijos al bajar.
  5. **Actividad aparte.** Un panel con los momentos y sus acciones, que se abre sobre el comprobante sin reemplazarlo.
- **Para comparar:**
  - **A favor:** lo que se revisa es lo que se firma; ideal para quien aprueba.
  - **En contra:** un formulario sin PDF también se ve como papel; ver la actividad pide un paso más.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-D3-Comprobante.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### D4 · La historia como columna

Archivo `FWA-D4-Historia.dc.html` («Formularios · detalle · D4 · la historia como columna»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-17, versión 79.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** todo en una columna, como línea de tiempo: las respuestas dentro de «Recibida» y el PDF dentro de «Al aprobarla».
  - **Abajo:** mientras espera decisión y el celular.
- **Título y bajada de las notas:** «D4 · La historia como columna». Todo en una columna, en el orden en que pasó: las respuestas adentro de «Recibida» y el PDF adentro de «Al aprobarla».
- **Notas numeradas:**
  1. **El estado, arriba.** La etiqueta junto al número; Aprobar y Rechazar se suman a la cabecera mientras espera decisión.
  2. **Un hecho por momento.** Recibida con su origen, Al recibirla, Por aprobar → Aprobada o Rechazada y lo que pasó después. La línea de arriba los resume; lo que no aplica no aparece.
  3. **Las respuestas, en Recibida.** Pregunta · valor con las fotos y el remito, debajo de quién la respondió y quién se lo mandó, con «Abrir conversación».
  4. **El PDF, en su hecho.** Donde se armó: miniatura, nombre y Descargar. Cada acción lleva su estado al lado; «Consultar» o «Reintentar», en la misma línea.
  5. **Descargar ▾.** El PDF y el remito también desde la cabecera, para no bajar hasta el final.
- **Para comparar:**
  - **A favor:** se entiende de un vistazo qué pasó y en qué orden; nada queda escondido.
  - **En contra:** la página se hace larga y las respuestas comparten lugar con la actividad.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-D4-Historia.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

## Menú de formularios (MF0–MF3, elegido MF1)

Pedido, dibujado y elegido MF1 el 2026-10-20, en la versión 88 del lienzo. Se ajustó ese mismo día, en la versión 96: quien solo aprueba, las flechas, el segmentado y el celular.

Programado el 2026-10-21 ([reglas](../../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21)). Cambia la navegación de la Etapa 3 y saca la bandeja global de G3.

MF2 y MF3 se retiraron del lienzo el 2026-10-22, en la versión 119.

**Diseño congelado en el front:** [formularios-whatsapp.md](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros).

**Nota guía de la sección «Menú de Formularios · tres conceptos» en el lienzo** (textual):

> Menú de Formularios (pedido el 2026-10-20). MF1 elegido el 2026-10-20; por programar. Ajustado el mismo día: quien solo aprueba no ve respuestas ni la última, ↑ ↓ recorren lo que espera al entrar por «Revisar» y el celular conserva la tabla.
>
> MF0 · Hoy: capturas de Respuestas, Por aprobar, la planilla y aprobar.
> MF1 · Una tabla de formularios (elegido): lo pendiente en una columna y un filtro; sin bandeja aparte.
> MF2 · Tarjetas y una bandeja: la bandeja de hoy queda como una vista de Formularios.
> MF3 · Los formularios en el menú: lo que espera arriba del panel y «Revisar lo pendiente», de a una.
>
> Los tres reemplazan Respuestas y Por aprobar del panel por «Formularios», dentro de «Gestión de formularios». Diseñar formularios, Catálogos y Formularios que faltan quedan como están. Cada uno muestra los dados de baja con su filtro y su aviso, tres personas, el celular y el inglés.
>
> MF2 y MF3, los conceptos que no se eligieron, se sacaron del lienzo el 2026-10-22 para hacer lugar: siguen en el repo lienzo-formularios, en retirados/.

#### MF0 · Hoy: Respuestas y Por aprobar en el panel

Archivo `FWA-MF0-Hoy.dc.html` («Formularios · menú · MF0 · hoy»).

- **Estado:** Referencia, no es un concepto: las capturas de lo que había antes, para comparar. Sigue en el lienzo.
- **Fecha y versión del lienzo:** 2026-10-20, versión 88.
- **Programado:** No aplica.
- **Qué muestra:**
  - **Las capturas:** cuatro, de «El sistema hoy», cada una de una persona distinta: Respuestas, Por aprobar, la planilla de un formulario y Aprobar en el panel.
  - **Abajo:** tres notas al pie.
- **Encabezado del tablero:** «MF0 · Hoy: Respuestas y Por aprobar en el panel». Capturas de «El sistema hoy», para comparar con los tres conceptos. Cada captura es de una persona distinta: hoy el panel cambia según lo que cada uno puede hacer.
- **Notas al pie:**
  - **Lo que se reemplaza.** Las secciones Respuestas y Por aprobar del panel. En su lugar, la entrada «Formularios»; el grupo pasa a llamarse «Gestión de formularios».
  - **Lo que queda igual.** Diseñar formularios, Catálogos y Formularios que faltan, y el detalle D2 para revisar y decidir.
  - **Cómo comparar.** MF1, MF2 y MF3 muestran los mismos casos: el panel, la página, un formulario, aprobar, todo lo pendiente, tres personas, el celular y el inglés.
- **Abajo, sección por sección:**
  - «Respuestas · quien ve respuestas»: En el panel, «Respuestas» con algunos formularios y «Ver todos (26)». La página solo dice el nombre y la versión de cada uno.
  - «Por aprobar · quien aprueba»: Una bandeja aparte, con «Para mí» y «Todas abiertas». Quien solo aprueba ve solo esta entrada.
  - «La planilla de un formulario»: Lo que espera aprobación no se distingue: hay que filtrar por estado.
  - «Aprobar, en el panel»: Quien solo aprueba ve la tarea y decide; las respuestas piden permiso de verlas.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-MF0-Hoy.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### MF1 · Una tabla de formularios

Archivo `FWA-MF1-Tabla.dc.html` («Formularios · menú · MF1 · una tabla de formularios»).

- **Estado:** Elegido el 2026-10-20.
- **Fecha y versión del lienzo:** 2026-10-20, versión 88. Ajustes de la versión 96.
- **Programado:** Sí, el 2026-10-21.
- **Qué muestra:**
  - **Panel de «Gestión de formularios»:** «Formularios 4», con lo que espera tu decisión, y debajo Diseñar formularios, Catálogos y Formularios que faltan.
  - **La página «Formularios»:**
    - arriba, el buscador, el segmentado «Activos (6) · Dados de baja (2)» y el filtro «Con algo por aprobar (3)»;
    - una línea que avisa «Control de vehículo está dado de baja y tiene 1 por aprobar. Ver»;
    - la tabla, con Formulario, Por aprobar, Respuestas y Última respuesta (el número, cuándo y el estado).
  - **Abajo, secciones con capturas reducidas:**
    - la página y un formulario, con «Por aprobar · 2» arriba de la planilla;
    - aprobar y todo lo que espera;
    - tres personas y alguien sin permiso;
    - el celular y el inglés.
- **Título y bajada de las notas:** «MF1 · Una tabla de formularios». Concepto 1. «Formularios» es una tabla con todos los formularios y lo que cada uno tiene por aprobar. Reemplaza a Respuestas y a Por aprobar del panel.
- **Notas numeradas:**
  1. **Una entrada en el panel.** «Formularios», con lo que espera tu decisión. Diseñar formularios, Catálogos y Formularios que faltan quedan como están. El grupo pasa a llamarse «Gestión de formularios».
  2. **Una fila por formulario.** Lo que tiene por aprobar, cuántas respuestas y la última, con su estado. Primero los que tienen algo por aprobar; después, por la última respuesta.
  3. **Con algo por aprobar.** Deja solo los que esperan una decisión, incluidos los dados de baja que todavía tienen alguna.
  4. **«Activos» y «Dados de baja».** Las mismas palabras que en Diseñar formularios (BA1-A). Los dados de baja salen de la lista, con un aviso; si alguno espera una decisión, una línea lo dice arriba de la tabla.
  5. **Al entrar en uno.** Arriba, lo que espera aprobación, de la más vieja a la más nueva; debajo, la planilla de siempre. «Revisar» abre el detalle D2, y ↑ ↓ recorren lo que espera en ese orden.
- **Para comparar:**
  - **A favor:** una sola lista, que sigue sirviendo con 26 formularios; se ve de un vistazo dónde hay trabajo.
  - **En contra:** no hay una bandeja con todo lo pendiente junto: quien aprueba en varios formularios entra en cada uno.
- **Para comparar (en «Aprobar y todo lo que espera»):**
  - **A favor:** un solo lugar para todo; cada respuesta se decide en su formulario, con sus vecinas a la vista.
  - **En contra:** se pierde el orden por antigüedad entre formularios; para aprobar en tres formularios hay que entrar en tres.
- **Abajo, sección por sección:**
  - **La página y un formulario.** Los dados de baja con su filtro, solo lo que espera una decisión y un formulario abierto con lo suyo arriba.
    - «Dados de baja (2)»: Fuera de la lista principal, con un aviso.
    - «Con algo por aprobar»: Los tres que esperan, incluido el dado de baja.
    - «Registrar visita»: Las 2 que esperan, arriba; la planilla, debajo.
  - **Aprobar y todo lo que espera.** «Revisar» abre el detalle D2 elegido, con Aprobar y Rechazar en el renglón «Por aprobar». ↑ ↓ recorren lo que espera, de la más vieja a la más nueva, y el pie del panel lo dice; desde la planilla, recorren la planilla.
    - «Todo lo que espera, de todos los formularios»: No hay bandeja aparte. El número del menú dice cuántas esperan tu decisión, y «Con algo por aprobar» lleva a los formularios que las tienen. Quien aprueba en un solo formulario no nota el cambio. Quien aprueba en varios entra en cada uno: decide con la planilla al lado.
  - **Tres personas y sin permiso.** La misma entrada para todos: cambia lo que muestra.
    - «Solo ve respuestas · Marcos Gil»: La tabla sin «Por aprobar»; el estado de la última respuesta sí se ve. Sin número en el menú.
    - «Solo aprueba · Laura Ríos»: Solo sus formularios con algo por aprobar, y cuántas. Sin «Respuestas» ni «Última respuesta»: piden permiso de verlas. Al entrar, sus tareas.
    - «Las dos cosas · Ana Pérez»: Todo: el número del menú, «Por aprobar» y la planilla.
    - «Sin ninguna de las dos»: No ve «Formularios»; si diseña, le quedan sus entradas. Por la dirección, «No tenés permiso».
  - **En el celular y en inglés.** En el celular, «Gestión de formularios» se despliega en el menú; la tabla se desplaza dentro de su caja, como todas las del sistema.
    - «En inglés»: «Form management», «Forms», «To approve», «Last response» y «Deactivated (2)».
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-MF1-Tabla.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-mf1-tabla.png) · [Front, escritorio](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/form-overview-active-es-1440.png) · [Front, solo lectura, en, 390 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/form-overview-read-en-390.png) · [Front, pendientes, escritorio](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/mf1-pending-es-desktop.png) · [Front, pendientes, en, 320 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/mf1-pending-en-320.png).
  - **Prototipo navegable:** Sí: el comienzo «Formularios», más «Como Laura», «Como Marcos» y «Sin permiso».
  - **Maqueta:** Sí, como pantalla real.

#### MF2 · Tarjetas y una bandeja en la misma página

Archivo `FWA-MF2-Tarjetas.dc.html` («Formularios · menú · MF2 · tarjetas y una bandeja»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-20, versión 88.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** «Formularios» con dos vistas: una tarjeta por formulario y «Todo lo que espera», que es la bandeja de hoy mudada adentro.
  - **Abajo:** las mismas secciones que MF1.
- **Título y bajada de las notas:** «MF2 · Tarjetas y una bandeja en la misma página». Concepto 2. «Formularios» tiene dos vistas: una tarjeta por formulario y «Todo lo que espera», que es la bandeja de hoy mudada adentro.
- **Notas numeradas:**
  1. **Una entrada en el panel.** «Formularios», con lo que espera tu decisión. Diseñar formularios, Catálogos y Formularios que faltan quedan como están.
  2. **Una tarjeta por formulario.** Respuestas, la última con su estado y, si hay, «2 por aprobar», que abre el formulario en esa pestaña. Primero los que esperan aprobación.
  3. **Todo lo que espera.** La bandeja de hoy, con todos los formularios: de la más vieja a la más nueva, con «Para mí», «Todas las abiertas» y el formulario para achicarla.
  4. **Dados de baja.** Fuera de las tarjetas, con «Dados de baja (2)» y un aviso. Lo que esperan sigue en «Todo lo que espera», marcado.
  5. **El formulario, en dos pestañas.** «Respuestas» es la planilla; «Por aprobar» lista solo las suyas, en orden. El detalle D2 se abre igual en las dos.
- **Para comparar:**
  - **A favor:** se conserva la bandeja tal como se usa hoy, y cada tarjeta muestra el trabajo de su formulario.
  - **En contra:** dos caminos para lo mismo; las tarjetas ocupan más que una fila y con 26 formularios hay que bajar bastante.
- **Para comparar (en «Aprobar y todo lo que espera»):**
  - **A favor:** nada que aprender para quien hoy usa Por aprobar; el orden por antigüedad entre formularios sigue.
  - **En contra:** la misma respuesta se encuentra por dos caminos, y la bandeja sigue separada de las planillas.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-MF2-Tarjetas.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### MF3 · Los formularios en el menú

Archivo `FWA-MF3-Menu.dc.html` («Formularios · menú · MF3 · los formularios en el menú»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-20, versión 88.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** los formularios en el panel, con lo que espera aprobación primero, y «Revisar lo pendiente (4)» para ir de a una.
  - **Abajo:** las mismas secciones que MF1.
- **Título y bajada de las notas:** «MF3 · Los formularios en el menú». Concepto 3. El panel lista los formularios, con lo que espera aprobación primero. Lo pendiente se revisa de a una, sin buscarlo.
- **Notas numeradas:**
  1. **Esperan aprobación.** Arriba del panel, los formularios con algo por aprobar y su cantidad; un dado de baja aparece solo mientras espere una decisión.
  2. **Revisar lo pendiente (4).** Abre el detalle D2 en la respuesta que espera hace más tiempo, de cualquier formulario. Al decidir, pasa a la siguiente.
  3. **La planilla, con lo pendiente arriba.** Al entrar en un formulario, lo que espera aprobación va primero, de la más vieja a la más nueva; después, el resto.
  4. **Ver todos (6).** Debajo, los de respuestas más recientes; «Ver todos» abre la lista completa, con buscador y «Dados de baja (2)».
- **Para comparar:**
  - **A favor:** cero vueltas: el trabajo está en el menú y se revisa de corrido.
  - **En contra:** con muchos formularios el panel se alarga y hay que buscar; no hay una tabla con todo lo pendiente a la vista.
- **Para comparar (en «Aprobar y todo lo que espera»):**
  - **A favor:** no hay que buscar ni volver a una lista; el número del panel baja al decidir.
  - **En contra:** no se ve todo lo pendiente junto antes de empezar; saltar de un formulario a otro puede desorientar.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-MF3-Menu.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

## Detalle completo de una respuesta (DC0–DC3, elegidos DC0 y DC1 ajustado)

Pedido y dibujado el 2026-10-20, en la versión 94 del lienzo. Ese día se eligieron DC0 y DC1 ajustado; DC1 ajustado se dibujó a pedido, después de ver DC1. El ajuste de la versión 96 suma el enlace compartido y las flechas.

Programados el 2026-10-21, junto con MF1. DC1 (todo a la vista), DC2 y DC3 se retiraron del lienzo el 2026-10-22, en la versión 119.

**Diseño congelado en el front:** [formularios-whatsapp.md](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros).

**Nota guía de la sección «Detalle completo de una respuesta» en el lienzo** (textual):

> Detalle completo de una respuesta (pedido el 2026-10-20). DC0 y DC1 ajustado elegidos el 2026-10-20; por programar. DC1, DC2 y DC3, las alternativas que no se eligieron, se sacaron del lienzo el 2026-10-22 para hacer lugar: siguen en el repo lienzo-formularios, en retirados/.
>
> El panel D2 queda como vista rápida; «Abrir en pantalla completa» lleva a la respuesta en una pantalla propia, con «Volver a la planilla» y ↑ ↓.
>
> DC0 · Cómo se llega: el botón del panel, volver, ↑ ↓ (por «Por aprobar», lo que espera; por la planilla, la planilla), quien solo aprueba, un enlace compartido y el celular.
> DC1 · Todo a la vista: sin pestañas; el estado en fila, la respuesta, de dónde vino, el PDF y la actividad, juntos.
> DC2 · El comprobante: se lee como el papel que se firma, con los botones al costado (las piezas de D3).
> DC3 · La historia: una línea de tiempo con todo lo que pasó (las piezas de D4).
> DC1 ajustado · La respuesta y la actividad a todo el ancho, plegables; la actividad, como la historia de DC3. Sin «De dónde vino», sin el PDF al costado y sin la fila de momentos.
>
> Cambia lo elegido en D2 el 2026-10-17: el panel deja de ser el único detalle.

#### DC0 · De la vista rápida a la pantalla completa

Archivo `FWA-DC0-VistaRapida.dc.html` («Formularios · detalle completo · DC0 · de la vista rápida a la pantalla completa»).

- **Estado:** Elegido el 2026-10-20.
- **Fecha y versión del lienzo:** 2026-10-20, versión 94. Ajuste de la versión 96.
- **Programado:** Sí, el 2026-10-21.
- **Qué muestra:**
  - **Detrás:** Registrar visita dentro de MF1, con «Por aprobar · 2» arriba de la planilla.
  - **El panel D2 de VIS-000024, que está «Por aprobar»:**
    - «Abrir en pantalla completa» en la cabecera;
    - el renglón «Por aprobar · Supervisor · hace 2 horas», con Rechazar y Aprobar;
    - el pie «↑↓ recorren lo que espera · 2 de 2».
  - **Abajo:**
    - los tres conceptos de pantalla completa en miniatura: DC1, DC2 y DC3;
    - lo que no cambia con el concepto: quien solo aprueba, un enlace compartido, el celular y el inglés.
- **Título y bajada de las notas:** «DC0 · De la vista rápida a la pantalla completa». El panel D2 queda como vista rápida: se abre al tocar una fila, sin salir de la planilla. Para ver todo ordenado, la respuesta se abre en una pantalla propia.
- **Notas numeradas:**
  1. **Abrir en pantalla completa.** El botón de la cabecera del panel lleva a la respuesta en una pantalla propia. La dirección cambia: se puede compartir o abrir en otra pestaña.
  2. **Volver a la planilla.** La flecha de la pantalla completa vuelve a la planilla con la misma fila marcada, los filtros y el orden.
  3. **↑ ↓.** Entrando por «Por aprobar · N», recorren lo que espera, de la más vieja a la más nueva: lo dice el pie del panel y, en la pantalla completa, «2 de 2 por aprobar». Desde la planilla, la planilla: «1 de 24».
  4. **Decidir, en las dos.** En la vista rápida, en el renglón «Por aprobar», como hoy; en la completa, según el concepto.
  5. **Quien solo aprueba.** El panel ya muestra lo que puede ver, la tarea: no tiene «Abrir en pantalla completa».
  6. **En el celular.** No hay vista rápida: tocar una fila abre la pantalla completa, en una columna.
- **Abajo, sección por sección:**
  - **Los tres conceptos de la pantalla completa.** La misma respuesta, ya aprobada, en cada concepto. Cada uno tiene su tablero a la derecha.
    - «DC1 · Todo a la vista»: Sin pestañas: el estado en fila, la respuesta ancha, de dónde vino, el PDF y la actividad, todo junto.
    - «DC2 · El comprobante»: Se lee como el papel que se firma, con los botones al costado y la actividad aparte.
    - «DC3 · La historia»: Una línea de tiempo: quién la mandó, qué respondió, qué se pidió y qué pasó después.
  - **Quien solo aprueba, un enlace, el celular y el inglés.** Lo que no cambia con el concepto que se elija.
    - «Quien solo aprueba»: El panel ya muestra todo lo que puede ver. Sin «Abrir en pantalla completa».
    - «Un enlace, el celular y el inglés»: Abierta desde un enlace compartido, la pantalla completa no trae la búsqueda ni las personas filtradas: los datos privados no van en la dirección. ↑ ↓ siguen el orden y las versiones de la planilla. En el celular, la planilla abre directo la pantalla completa del concepto que se elija, en una columna. Volver deja la planilla en la misma fila. En inglés: «Open in full view», «Back to responses», «2 of 2 to approve» y, en la pantalla completa, los mismos textos que el detalle D2.
- **Reglas del 2026-10-23:** Sin choques visibles en la pantalla principal. «Reglas de la revisión 3» aparece solo en la miniatura de DC1, que no se eligió. En DC1 ajustado y en D2 sí está: ver ahí.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-DC0-VistaRapida.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/det-dc0-vista-rapida.png) · [Front, pendientes completos, 390 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/pending-full-es-390.png) · [Front, solo decisión, 320 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/decide-only-es-320.png).
  - **Prototipo navegable:** Sí: desde D2, «Abrir en pantalla completa».
  - **Maqueta:** Sí, como pantalla real.

#### DC1 ajustado · Respuesta y actividad

Archivo `FWA-DC1-Ajustado.dc.html` («Formularios · detalle completo · DC1 ajustado · respuesta y actividad»).

- **Estado:** Elegido el 2026-10-20.
- **Fecha y versión del lienzo:** 2026-10-20, versión 94. Ajuste de la versión 96.
- **Programado:** Sí, el 2026-10-21. Su ruta es `/formularios/{formId}/respuestas/{recordId}/completa`.
- **Qué muestra:**
  - **Pantalla completa de VIS-000024.** Las migas llegan hasta el número.
  - **Arriba:** el título con «Aprobada», «1 de 24» con ↑ ↓, Abrir conversación y Descargar ▾.
  - **«Respuesta», plegable:** «Versión 3 del formulario» y «pregunta · valor», con las fotos y el remito.
  - **«Actividad», plegable,** con «Reglas de la revisión 3» en el encabezado:
    - Recibida: quién respondió, desde qué número y quién se lo mandó;
    - Al recibirla: el aviso a Juan, «Leído»;
    - Por aprobar → Aprobada: el rol Supervisor y Laura Ríos;
    - Al aprobarla: el comprobante PDF, con una vista previa chica y Descargar, y el PDF que se le mandó a Juan, «Entregado».
  - **Abajo:** las dos secciones plegadas, mientras espera, el celular y el inglés.
- **Título y bajada de las notas:** «DC1 ajustado · Respuesta y actividad». DC1 con tus ajustes: sin «De dónde vino» ni el PDF al costado, y sin la fila de momentos de arriba. Quedan dos secciones a todo el ancho, que se pliegan.
- **Notas numeradas:**
  1. **Arriba, lo que se hace.** ↑ ↓, Abrir conversación y Descargar ▾, con el comprobante PDF y los documentos. Mientras espera, Aprobar y Rechazar.
  2. **Respuesta.** «Pregunta · valor» a todo el ancho, con las fotos y el remito. La flecha la pliega; plegada, dice cuántas preguntas, fotos y documentos tiene.
  3. **Actividad, como la historia de DC3.** En orden: quién la respondió y quién se la mandó, lo que se hizo al recibirla, la decisión y lo que se hizo después. También se pliega.
  4. **El PDF, en su momento.** En «Al aprobarla», con una vista previa chica que abre el visor y Descargar.
  5. **Volver y ↑ ↓.** Como en DC0: la flecha vuelve a la planilla en la misma fila; ↑ ↓ pasan de respuesta.
- **En las notas, «Respecto de DC1»:** Sale «De dónde vino»: lo cuenta la actividad, y «Abrir conversación» está arriba. Sale el PDF del costado: queda en la actividad y en Descargar ▾. Sale la fila de momentos: la actividad los cuenta en orden. La cabecera no repite el formulario: está en las migas, y la versión, en «Respuesta».
- **Abajo, sección por sección:**
  - **Plegadas, mientras espera decisión, en el celular y en inglés.** La misma pantalla en los otros casos.
    - «Las dos plegadas»: Cada sección dice en una línea lo que tiene; se despliega con la flecha.
    - «Mientras espera decisión»: Aprobar y Rechazar arriba; la actividad termina en «Ahora · por aprobar».
    - «En el celular, a 390 px»: La respuesta abierta y, debajo, la actividad plegada; la decisión, fija abajo.
    - «En inglés»: «Response», «Activity», «Collapse response», «Expand activity», «Open conversation» y «Download».
- **Reglas del 2026-10-23:**
  - **Número de revisión, a revisar:** el encabezado de la sección dice «Actividad · Reglas de la revisión 3». Hay que decidir si queda, como en D2.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-DC1-Ajustado.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/det-dc1-ajustado.png) · [Front, inicial, 320 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/response-full-initial-es-320.png) · [Front, actividad, en, 390 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/response-full-activity-en-390.png) · [Front, visor PDF, 320 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/response-full-pdf-es-320.png) · [Front, atajo PDF, en, 390 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/pdf-intent-en-390.png).
  - **Prototipo navegable:** Sí: el comienzo «Detalle completo».
  - **Maqueta:** Sí, como pantalla real.

#### DC1 · Todo a la vista

Archivo `FWA-DC1-TodoALaVista.dc.html` («Formularios · detalle completo · DC1 · todo a la vista»).

- **Estado:** Alternativa no elegida: se ajustó y quedó DC1 ajustado. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-20, versión 94.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** sin pestañas. El estado en fila, la respuesta ancha, «De dónde vino» y el PDF al costado, y la actividad abajo.
  - **Abajo:** mientras espera, el celular y el inglés.
- **Título y bajada de las notas:** «DC1 · Todo a la vista». Concepto 1. Todo lo de la respuesta en una sola pantalla, sin pestañas: el estado arriba, lo que respondió y de dónde vino, y lo que pasó después.
- **Notas numeradas:**
  1. **El estado, en fila.** Los momentos uno al lado del otro, como en D1: Recibida, Al recibirla, Por aprobar → Aprobada y Al aprobarla, con su resumen.
  2. **La respuesta, ancha.** «Pregunta · valor» con las fotos más grandes; las miniaturas abren el visor y el remito se descarga.
  3. **Al costado.** De dónde vino, con Abrir conversación, y el comprobante PDF cuando hay copia.
  4. **Lo que pasó después.** La actividad completa, agrupada por momento, con Consultar o Reintentar donde haga falta.
  5. **Arriba a la derecha.** ↑ ↓, Abrir conversación, Descargar ▾ y, mientras espera, Aprobar y Rechazar.
- **Para comparar:**
  - **A favor:** se ve todo de una vez, sin buscar en pestañas: lo más claro para entender una respuesta.
  - **En contra:** la pantalla es larga y la actividad queda abajo; en el celular es una columna larga.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-DC1-TodoALaVista.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### DC2 · El comprobante

Archivo `FWA-DC2-Comprobante.dc.html` («Formularios · detalle completo · DC2 · el comprobante»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-20, versión 94.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** las piezas de D3 en pantalla completa: el comprobante con su firma, los botones al costado y la actividad aparte.
  - **Abajo:** mientras espera, el celular y el inglés.
- **Título y bajada de las notas:** «DC2 · El comprobante». Concepto 2. La respuesta se lee como el papel que se firma, con los botones al costado. Lo que pasó se abre aparte.
- **Notas numeradas:**
  1. **El estado, en una línea.** Los momentos arriba; tocar uno abre la actividad en ese momento.
  2. **Se lee como el comprobante.** El orden y el aspecto del PDF, con todas las preguntas. Es la respuesta, no una imagen: las fotos abren el visor.
  3. **La firma.** Quién la aprobó y cuándo; mientras espera, «Se completa al aprobarla»; si la rechazan, el motivo en su lugar.
  4. **Botones al costado.** Aprobar y Rechazar mientras espera; después, Descargar PDF, Abrir conversación y Actividad. Quedan fijos al bajar.
  5. **Actividad aparte.** Un panel que se abre sobre el comprobante, sin reemplazarlo.
- **Para comparar:**
  - **A favor:** lo que se revisa es lo que se firma; ideal para quien aprueba.
  - **En contra:** un formulario sin PDF también se ve como papel; ver la actividad pide un paso más.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-DC2-Comprobante.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### DC3 · La historia

Archivo `FWA-DC3-Historia.dc.html` («Formularios · detalle completo · DC3 · la historia»).

- **Estado:** Alternativa no elegida: DC1 ajustado toma de acá la actividad contada como historia. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-20, versión 94.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** las piezas de D4 en pantalla completa: una línea de tiempo con la respuesta dentro de «Recibida» y el PDF dentro de «Al aprobarla».
  - **Abajo:** mientras espera, el celular y el inglés.
- **Título y bajada de las notas:** «DC3 · La historia». Concepto 3. La respuesta contada en orden: quién la mandó, qué respondió, qué se pidió y qué pasó después, en una línea de tiempo.
- **Notas numeradas:**
  1. **La etiqueta.** El estado actual, junto al número.
  2. **Los momentos, en una línea.** Un resumen arriba; abajo, la historia completa, de arriba hacia abajo.
  3. **Lo que respondió, en «Recibida».** Las respuestas, con las fotos y el remito, dentro del momento en que llegaron.
  4. **El PDF, en «Al aprobarla».** El comprobante aparece cuando se armó, con Descargar.
  5. **Arriba a la derecha.** ↑ ↓, Descargar ▾ y, mientras espera, Aprobar y Rechazar.
- **Para comparar:**
  - **A favor:** se entiende qué pasó y en qué orden, de un vistazo.
  - **En contra:** las respuestas quedan dentro de un momento; una respuesta larga empuja el resto hacia abajo.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-DC3-Historia.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

## Dar de baja, reactivar y eliminar (BA1–BA6)

Decidido, dibujado y elegido el 2026-10-20, en la versión 88 del lienzo: BA1-A y BA2–BA5. Ese mismo día, en la versión 96, se ajustaron con la guía y se sumó BA6.

Programados el 2026-10-21 ([reglas](../../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21)), con el review cerrado. El piloto real sigue pendiente. BA1-B se retiró del lienzo el 2026-10-22, en la versión 119.

**Diseño congelado en el front:** [formularios-whatsapp.md](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros).

**Nota guía de la sección «Dar de baja, reactivar y eliminar» en el lienzo** (textual):

> Dar de baja, reactivar y eliminar (decidido el 2026-10-20). BA1-A y BA2–BA5 elegidos el 2026-10-20; por programar. Ajustados el mismo día con la guía: «Activos» y «Dados de baja» como en Formularios, la tabla en el celular y BA6.
>
> BA1 · Dónde viven las acciones: A, en la fila de Diseñar formularios (elegido); B, en una ficha al costado.
> BA2 · El editor: el menú «⋯», el aviso de dado de baja y lo que pasa en WhatsApp.
> BA3 · El diálogo «Dar de baja»: qué deja de pasar, envíos abiertos, WhatsApp y lo que se conserva.
> BA4 · Reactivar y eliminar, y por qué Eliminar no está cuando hay respuestas.
> BA5 · El circuito de punta a punta: la persona, el sistema y el asistente.
> BA6 · Una sugerencia hacia un dado de baja: el editor de sugerencias la marca y no deja guardar hasta quitarla.
>
> Las respuestas de los dados de baja se ven desde el menú de Formularios elegido (MF1), en la sección de abajo. Reglas en ArquitecturaBase/docs/plans/formularios/dar-de-baja-formularios.md.
>
> BA1-B, el concepto que no se eligió, se sacó del lienzo el 2026-10-22 para hacer lugar: sigue en el repo lienzo-formularios, en retirados/.

#### BA1-A · Las acciones en la fila

Archivo `FWA-BA1-A-Fila.dc.html` («Formularios · dar de baja · BA1 · A · acciones en la fila»).

- **Estado:** Elegido el 2026-10-20.
- **Fecha y versión del lienzo:** 2026-10-20, versión 88. Ajuste de la versión 96.
- **Programado:** Sí, el 2026-10-21.
- **Qué muestra:**
  - **Diseñar formularios:** «Nuevo formulario», el buscador y el segmentado «Activos (7) · Publicados (6) · Borradores (1) · Dados de baja (2)».
  - **La tabla:** Formulario, Preguntas, En WhatsApp, Respuestas, Último cambio y Acciones. Las acciones son íconos: Editar; Dar de baja en los publicados; Eliminar solo sin respuestas, como Facturar entrega, que tiene 0.
  - **Abajo:** los dados de baja, los permisos, el celular y el inglés.
- **Título y bajada de las notas:** «BA1 · A · Las acciones en la fila». Concepto A. Diseñar formularios separa los dados de baja, y cada fila tiene en su grupo de acciones lo que se le puede hacer.
- **Notas numeradas:**
  1. **«Activos» y «Dados de baja».** Las mismas palabras que en Formularios (MF1). «Activos» son los publicados y los borradores; «Publicados» y «Borradores» los achican. Los dados de baja, aparte, con la cantidad del servidor.
  2. **Los íconos según el estado.** Editar; Dar de baja en los publicados; Eliminar, último, solo si no tiene respuestas; en los dados de baja, Reactivar. Cada uno dice su nombre al pasar el mouse o con el foco, y abre su diálogo (BA3 y BA4).
  3. **Respuestas.** La cantidad de cada uno: explica por qué uno se puede eliminar y otro no. Con respuestas, se da de baja.
  4. **«Dado de baja · versión N».** En «Dados de baja», con lo que pasa en WhatsApp debajo: «Se deprecan cuando venzan 2 envíos abiertos» o «Deprecados en WhatsApp el 2 oct».
  5. **Permisos.** Editar y Eliminar piden administrar; Dar de baja y Reactivar, publicar. Lo que no se puede no se dibuja; con solo lectura, la fila no tiene acciones.
- **Para comparar:**
  - **A favor:** un toque desde la lista, como en Usuarios y Roles; se ve de un vistazo qué se puede hacer con cada uno.
  - **En contra:** las consecuencias (envíos abiertos, sugerencias, WhatsApp) recién se ven en el diálogo; hasta tres íconos que cambian de una fila a otra.
- **Abajo, sección por sección:**
  - **Los dados de baja, los permisos, el celular y el inglés.** Los mismos íconos en todas partes; lo que no se puede hacer no se dibuja.
    - ««Dados de baja (2)»»: Solo los dados de baja, con lo que pasa en WhatsApp. Reactivar está a mano.
    - «Con permiso de publicar, sin administrar»: Queda Dar de baja; en «Dados de baja», Reactivar. Sin Editar, Eliminar ni «Nuevo formulario»; los borradores, sin acciones.
    - «En el celular, a 390 px»: La tabla y el segmentado se desplazan dentro de su caja.
    - «En inglés»: «Active», «Deactivated», «Deactivate», «Reactivate» y «Delete». Los nombres de los formularios quedan como se escribieron.
- **Reglas del 2026-10-23:** Sin choques: cada ícono dice su nombre al pasar el mouse o con el foco.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-BA1-A-Fila.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba1-a-fila.png) · [Front, 390 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba1-es-390.png) · [Front, NeedsAttention en la tabla](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-needsattention-list-es-320.png).
  - **Prototipo navegable:** Sí: el comienzo «Dar de baja, reactivar y eliminar».
  - **Maqueta:** Sí, como pantalla real.

#### BA1-B · La ficha al costado

Archivo `FWA-BA1-B-Ficha.dc.html` («Formularios · dar de baja · BA1 · B · la ficha al costado»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-20, versión 88.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** en la fila, solo Editar. El nombre abre una ficha al costado, como D2, con el contexto y las acciones.
  - **Abajo:** los otros estados, el celular, los permisos y el inglés.
- **Título y bajada de las notas:** «BA1 · B · La ficha al costado». Concepto B. La fila solo edita; tocar el nombre abre la ficha del formulario, con lo que lo rodea y lo que se le puede hacer.
- **Notas numeradas:**
  1. **La lista, más simple.** «Dados de baja (2)» en el segmentado y «Dado de baja · versión N» con lo que pasa en WhatsApp, como en A. En la fila, solo Editar, que sigue yendo al editor.
  2. **La ficha.** Tocar el nombre la abre al costado sin perder la lista, como el detalle D2: ↑ ↓ pasan de formulario y Escape cierra.
  3. **Antes de decidir.** Envíos abiertos, respuestas por aprobar y qué formularios lo sugieren: lo que el diálogo de Dar de baja va a explicar ya está a la vista.
  4. **Lo que se puede hacer, abajo.** Editar y Dar de baja; en un dado de baja, Reactivar. Eliminar va separado a la izquierda y en rojo, solo sin respuestas; si no está, la ficha dice por qué.
- **Para comparar:**
  - **A favor:** decide con el contexto a la vista y explica por qué no se puede eliminar; la tabla queda limpia.
  - **En contra:** un toque más que A; la ficha tapa columnas de la derecha y en el celular es otra pantalla.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-BA1-B-Ficha.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### BA2 · El editor

Archivo `FWA-BA2-Editor.dc.html` («Formularios · dar de baja · BA2 · el editor»).

- **Estado:** Elegido el 2026-10-20.
- **Fecha y versión del lienzo:** 2026-10-20, versión 88.
- **Programado:** Sí, el 2026-10-21.
- **Qué muestra:**
  - **El editor de «Registrar visita» dado de baja:** el título dice «Dado de baja · versión 3». Las acciones son Descartar, Guardar borrador, Reactivar (en el lugar de Publicar) y el menú «⋯».
  - **El aviso:** «Dado de baja por Ana Pérez el 9 oct 2026, 10:12…», con lo que pasa en WhatsApp: «se deprecan cuando venzan los 2 envíos abiertos».
  - **Lo demás:** «El formulario», «Preguntas» y la vista previa, como siempre.
  - **Abajo:**
    - un publicado con el menú abierto, con Eliminar apagado porque tiene respuestas;
    - uno ya deprecado;
    - el menú de un dado de baja;
    - el celular.
- **Título y bajada de las notas:** «BA2 · El editor». El editor de un formulario dado de baja: el aviso con quién y cuándo, qué pasa en WhatsApp y el menú «⋯» con las mismas acciones de la lista.
- **Notas numeradas:**
  1. **El menú «⋯».** Al lado de la acción principal. En un publicado: Dar de baja y Eliminar; en un dado de baja: Reactivar y Eliminar. Lo que no se puede hacer queda apagado con el motivo; sin permiso, no se dibuja.
  2. **El aviso.** Quién lo dio de baja y cuándo, que no se manda desde el chat ni lo ofrece el asistente, y que lo cargado se conserva.
  3. **Reactivar, como acción principal.** Ocupa el lugar de «Publicar en WhatsApp»: es publicar una versión nueva con el borrador, que se puede seguir editando.
  4. **WhatsApp, en palabras.** «Se deprecan cuando venzan los envíos abiertos», «Deprecados el …» o, si no se pudo confirmar, que se sigue consultando.
- **Abajo, sección por sección:**
  - **El publicado, los deprecados, el menú y el celular.** El mismo aviso y el mismo menú en todas las variantes.
    - «Un publicado, con el menú abierto»: Dar de baja abre el diálogo de BA3. Eliminar, apagado: tiene respuestas.
    - «Ya deprecados en WhatsApp»: Cuando vencen los envíos, el aviso lo dice con la fecha.
    - «El menú «⋯» de un dado de baja»: Reactivar también está en la cabecera; Eliminar, apagado con el motivo.
    - «En el celular, a 390 px»: La acción principal y el «⋯» quedan arriba; el aviso, primero.
- **Reglas del 2026-10-23:**
  - **Motivo escrito en el menú:** el menú «⋯» muestra las acciones apagadas con el motivo escrito: «Eliminar · No se puede: tiene 24 respuestas». Con la regla, el motivo va como tooltip. Hoy el front lo muestra como texto no accionable, según `formularios.md`.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-BA2-Editor.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba2-editor.png) · [Front, en, 320 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba2-en-320.png) · [Front, NeedsAttention en el editor](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-needsattention-editor-en-390.png) · [Front, motivo en el menú](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-unavailable-menu-en-320.png).
  - **Prototipo navegable:** Sí: el comienzo «Desde el editor».
  - **Maqueta:** Sí, como pantalla real.

#### BA3 · El diálogo «Dar de baja»

Archivo `FWA-BA3-DarDeBaja.dc.html` («Formularios · dar de baja · BA3 · el diálogo»).

- **Estado:** Elegido el 2026-10-20.
- **Fecha y versión del lienzo:** 2026-10-20, versión 88.
- **Programado:** Sí, el 2026-10-21.
- **Qué muestra:**
  - **Detrás:** el editor de «Registrar visita», publicado.
  - **El diálogo «Dar de baja «Registrar visita»»:**
    - «Qué deja de pasar»: el chat, el asistente y las 2 sugerencias, con sus nombres;
    - «Envíos abiertos y WhatsApp»: 2 envíos, el último vence el 9 oct a las 18:40, y «En WhatsApp no se deshace»;
    - «Lo que se conserva»;
    - Cancelar y Dar de baja.
  - **Abajo:** sin envíos abiertos ni sugerencias, mientras se da de baja y si falla, el celular y el inglés.
- **Título y bajada de las notas:** «BA3 · El diálogo «Dar de baja»». Dice qué pasa antes de confirmar. Los números y los nombres los calcula el servidor al abrirlo.
- **Notas numeradas:**
  1. **Qué deja de pasar.** Se deja de mandar desde el chat, el asistente no lo ofrece y las sugerencias de otros formularios hacia él dejan de ofrecerse, con sus nombres.
  2. **Envíos abiertos y WhatsApp.** Cuántos hay y cuándo vence el último: hasta entonces se pueden responder. Después se deprecan sus Flows en WhatsApp, y eso no se deshace.
  3. **Lo que se conserva.** Respuestas, fotos, PDF, aprobaciones y Excel. Lo que espera decisión se puede seguir decidiendo.
  4. **Dar de baja.** Con permiso de publicar. No borra nada: se puede reactivar con una versión nueva.
- **Abajo, sección por sección:**
  - **Los otros casos, el celular y el inglés.** El mismo diálogo, con lo que corresponde a cada formulario.
    - «Sin envíos abiertos, sugerencias ni respuestas»: Lo que no aplica no se muestra.
    - «Mientras se da de baja y si falla»: El botón queda ocupado mientras se guarda. Si falla, el diálogo sigue abierto.
    - «En el celular, a 390 px»: El diálogo ocupa la pantalla y los botones quedan fijos abajo.
    - «En inglés»: Los nombres de los formularios quedan como se escribieron.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-BA3-DarDeBaja.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba3-dar-de-baja.png) · [Front, 390 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba3-es-390.png) · [Front, sin envíos abiertos, 320 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-no-open-es-320.jpg).
  - **Prototipo navegable:** Sí: desde Diseñar formularios, dar de baja Registrar visita o Facturar entrega.
  - **Maqueta:** Sí, como pantalla real.

#### BA4 · Reactivar y eliminar

Archivo `FWA-BA4-Reactivar.dc.html` («Formularios · dar de baja · BA4 · reactivar y eliminar»).

- **Estado:** Elegido el 2026-10-20.
- **Fecha y versión del lienzo:** 2026-10-20, versión 88.
- **Programado:** Sí, el 2026-10-21.
- **Qué muestra:**
  - **Tablero de casos:** los diálogos de Reactivar y Eliminar, en siete casos numerados, en la computadora y a 390 px.
  - **El último caso:** el menú cuando Eliminar no se puede.
  - **Abajo:** tres notas al pie.
- **Encabezado del tablero:** «BA4 · Reactivar y eliminar». Los dos diálogos, en la computadora y en el celular, y lo que se ve cuando eliminar no se puede.
- **Casos numerados (son las notas numeradas de este tablero):**
  1. **Reactivar, sin cambios.** Publica una versión nueva con el borrador actual. La numeración sigue donde estaba.
  2. **Reactivar, con cambios.** Si el borrador cambió, se ve qué cambia respecto de la última versión, como al publicar (P1).
  3. **Reactivar, con algo que corregir.** La comprobación de siempre: si WhatsApp no puede mostrar una pregunta, no se reactiva.
  4. **En el celular, a 390 px.** Los dos diálogos ocupan la pantalla, con los botones fijos abajo.
  5. **Eliminar un borrador.** Solo sin respuestas. Desaparece de los listados y el prefijo queda reservado.
  6. **Eliminar un publicado sin respuestas.** Si llegó a publicarse, sus Flows se deprecan en WhatsApp, igual que al dar de baja.
  7. **Con respuestas, Eliminar no está.** No se ofrece un botón que no puede funcionar: el menú dice por qué y qué hacer.
- **Notas al pie:**
  - **Reactivar es publicar.** Una versión nueva con el borrador, con su comprobación y Flows nuevos. Pide permiso de publicar.
  - **Eliminar, solo sin respuestas.** Las respuestas no se borran desde el formulario. Pide permiso de administrar; sin él, no se dibuja.
  - **En inglés.** «Reactivate «Pedido de materiales»», «Delete «Encuesta de satisfacción»» y «Can't be deleted: it has 24 responses».
- **Reglas del 2026-10-23:**
  - **Motivo escrito en el menú:** el caso 7 muestra «Eliminar · No se puede: tiene 24 respuestas» escrito en el menú. En la ficha (B) y en el editor, Eliminar queda «apagado con el motivo». Con la regla, el motivo va como tooltip.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-BA4-Reactivar.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba4-reactivar.png) · [Front, Reactivar, 320 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba4-reactivar-es-320.png) · [Front, Eliminar, en, 320 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba4-eliminar-en-320.png) · [Front, eliminar borrador](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-delete-draft-es-320.png) · [Front, eliminar publicado](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-delete-published-en-320.png).
  - **Prototipo navegable:** Sí: reactivar desde «Dados de baja» y eliminar lo que no tiene respuestas.
  - **Maqueta:** Sí, como pantalla real.

#### BA5 · El circuito de punta a punta

Archivo `FWA-BA5-Circuito.dc.html` («Formularios · dar de baja · BA5 · el circuito»).

- **Estado:** Elegido el 2026-10-20.
- **Fecha y versión del lienzo:** 2026-10-20, versión 88.
- **Programado:** Es un circuito, no una pantalla. Sus pantallas están programadas.
- **Qué muestra:**
  - **Tablero de casos:** el circuito en cinco pasos numerados. Cada paso trae su pantalla y qué hacen la persona, el sistema y el asistente.
  - **Abajo:** tres notas al pie.
- **Encabezado del tablero:** «BA5 · El circuito de punta a punta». De activo a dado de baja y de vuelta: qué hace cada persona, qué hace el sistema y qué deja de hacer el asistente.
- **Casos numerados (son las notas numeradas de este tablero):**
  1. **Activo.** Registrar visita, publicado en su versión 3.
  2. **Dar de baja.** Ana lo da de baja desde la lista o el editor.
  3. **Un envío abierto se responde.** Juan tenía el formulario abierto desde antes.
  4. **Al vencer, deprecados en WhatsApp.** Cuando no quedan envíos vigentes.
  5. **Reactivar.** Ana lo reactiva con el borrador actual.
- **Notas al pie:**
  - **Lo cargado se conserva.** Respuestas, fotos, PDF, aprobaciones y Excel siguen igual en todo el circuito.
  - **En WhatsApp no se deshace.** Por eso se espera a que venzan los envíos abiertos, y reactivar publica Flows nuevos.
  - **La numeración sigue.** VIS-000025 llegó con el formulario dado de baja; la próxima, ya reactivado, es VIS-000026.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-BA5-Circuito.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba5-circuito.png).
  - **Prototipo navegable:** En parte: se puede recorrer con el comienzo «Dar de baja, reactivar y eliminar».
  - **Maqueta:** No tiene recorrido propio. Las pantallas reales sí están.

#### BA6 · Una sugerencia hacia un dado de baja

Archivo `FWA-BA6-Sugerencias.dc.html` («Formularios · dar de baja · BA6 · sugerencias»).

- **Estado:** Elegido como ajuste el 2026-10-20.
- **Fecha y versión del lienzo:** 2026-10-20, versión 96.
- **Programado:** Sí, el 2026-10-21.
- **Qué muestra:**
  - **Tablero de casos:** el editor «Qué sugerir después» de Registrar entrega, en tres casos numerados:
    - la fila hacia un dado de baja;
    - después de quitarla;
    - a 390 px.
  - **Abajo:** cuatro notas al pie.
- **Encabezado del tablero:** «BA6 · Una sugerencia hacia un dado de baja». Si un formulario sugiere otro que se dio de baja, el editor de sugerencias lo marca y no deja guardar hasta quitarla.
- **Casos numerados (son las notas numeradas de este tablero):**
  1. **Una sugerencia apunta a un dado de baja.** El editor de Registrar entrega: la fila de Registrar visita dice «Dado de baja» y pide quitarla. Mientras quede, Guardar sugerencias está apagado.
  2. **Al quitarla, se puede guardar.** Con el tacho de su fila, el único que tiene. Las demás sugerencias quedan como estaban.
  3. **En el celular, a 390 px.** La misma fila. Guardar sugerencias queda abajo, a todo el ancho, apagado hasta quitarla.
- **Notas al pie:**
  - **No se borra sola.** Dar de baja no toca las sugerencias de los otros formularios: dejan de ofrecerse, como avisa el diálogo (BA3). Si se reactiva, vuelven las que quedaron.
  - **Mientras quede, no se guarda.** Guardar sugerencias queda apagado y la fila dice por qué. Lo demás del editor no cambia.
  - **Al agregar, no aparece.** «Formulario a sugerir» solo ofrece publicados: un dado de baja no se puede elegir.
  - **En inglés.** «Deactivated» y «It's no longer offered. Remove it to save the suggestions.»
- **Reglas del 2026-10-23:**
  - **Motivo de un botón apagado, escrito en la fila:** la fila dice «Ya no se ofrece. Quitala para poder guardar las sugerencias.», y eso explica por qué Guardar sugerencias está apagado. Con la regla, ese motivo va como tooltip del botón; «Dado de baja» en la fila puede quedar.
  - **A revisar:** la bajada de la sección, «Se aplica al guardar: no hace falta volver a publicar en WhatsApp». Viene de s2.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-BA6-Sugerencias.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba6-sugerencias.png) · [Front, en, 390 px](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba6-en-390.png).
  - **Prototipo navegable:** Sí: con Registrar visita dado de baja, el editor de Registrar entrega marca su sugerencia.
  - **Maqueta:** Sí, como pantalla real.

## Casos del asistente (AS1–AS6)

Decidido, dibujado y elegido el 2026-10-21, en la versión 98 del lienzo: AS1-A y AS2–AS6. Se dibujó con lo que el plan sumó ese día: el agente principal, las ráfagas y varias acciones por respuesta.

**Por programar:** el [plan](../2026-10-21-casos-del-asistente.md) dice «sin empezar». AS1-B y AS1-C se retiraron del lienzo el 2026-10-22, en la versión 119.

**Multitenant:** el plan dice que el perfil, los casos y los límites son por organización.

**Diseño congelado en el front:** [asistente-casos.md](../../../../ArquitecturaBaseFront/docs/design/asistente-casos.md#los-tableros).

**Nota guía de la sección «Casos del asistente» en el lienzo** (textual):

> Casos del asistente (decidido el 2026-10-21). AS1-A y AS2–AS6 elegidos el 2026-10-21; por programar. AS1-B y AS1-C, las alternativas que no se eligieron, se sacaron del lienzo el 2026-10-22 para hacer lugar: siguen en el repo lienzo-formularios, en retirados/. Dibujados y ajustados el mismo día con los agregados del plan: el agente principal, las ráfagas y varias acciones por respuesta.
>
> AS1 · La pantalla del agente principal, en tres conceptos: A, con pestañas; B, una página guiada; C, una lista con el detalle al costado. Cada uno con el estado vacío, el celular y el inglés, y con lugar para que en la etapa Agentes haya varios.
> AS2 · Los casos: la lista en orden, los de los formularios y «Cualquier otra cosa»; el editor de un caso y lo que pide cada acción.
> AS3 · Probar el asistente: los mensajes de ejemplo, con la información cargada y sin ella, y una ráfaga que responde con tres acciones.
> AS4 · El chat: cada acción con su «por qué», y «Necesita una persona» en el chat y en la lista.
> AS5 · Límites y conocimiento: lo que vale siempre, lo que se configura, las acciones que puede usar y la Información del negocio reutilizada.
> AS6 · El circuito de punta a punta: la persona, el sistema y la IA, también con la ráfaga.
>
> Confirmado al elegir: las ráfagas (4 s sin mensajes nuevos, tope de 15 s), hasta 3 acciones por respuesta y el borrador para el operador en Atención manual (AS4). Derivar pasa la conversación a Atención manual (AS4).
>
> Reglas en ArquitecturaBase/docs/plans/2026-10-21-casos-del-asistente.md.

#### AS1-A · Pestañas

Archivo `FWA-AS1-A-Pestanas.dc.html` («Asistente · AS1 · A · pestañas»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 98.
- **Programado:** No.
- **Qué muestra:**
  - **Arriba:** el panel de WhatsApp, con Chat y Asistente. El título «Agente principal» y «Atiende todas las conversaciones en modo Asistente». A la derecha, Probar, Descartar y Guardar.
  - **Las pestañas:** Perfil, Conocimiento, Casos 10 y Límites. Se entra por Casos.
  - **La pestaña Casos:**
    - «Nuevo caso»;
    - «Casos propios · 5»: derivar, el texto exacto del horario, preguntar con botones, no responder y uno pausado;
    - «De los formularios · 4», con «Editar en el formulario»;
    - «Cualquier otra cosa», siempre al final.
  - **Abajo:**
    - vacío, para un negocio que empieza: abre en Perfil, con un aviso;
    - Probar, en un panel al costado;
    - el celular: Perfil a 390 px, con Guardar fijo abajo;
    - el inglés y sin permiso.
- **Título y bajada de las notas:** «AS1 · A · Pestañas». Concepto A. Una pantalla Asistente, en el panel de WhatsApp, con una pestaña por parte: perfil, conocimiento, casos y límites.
- **Notas numeradas:**
  1. **Cuatro pestañas.** Perfil, Conocimiento, Casos (con su cantidad) y Límites, como las secciones de Información del negocio. Se entra por Casos, que es lo que más se toca.
  2. **Probar, a mano.** Abre un panel al costado sin salir de la pestaña (AS3). Prueba lo guardado.
  3. **Un solo guardado.** Guardar guarda las cuatro pestañas juntas, con revisión: si otro guardó antes, avisa y no pisa nada (409), como Información del negocio.
  4. **«Cualquier otra cosa», al final.** Siempre existe, no se borra ni se mueve. Los casos de los formularios van entre los propios y este (AS2).
  5. **Quién lo ve.** Con «Configurar el asistente». Sin ese permiso, la entrada no aparece.
  6. **El agente principal.** La pantalla es la de un agente: el principal, que atiende todas las conversaciones en modo Asistente. Cuando haya varios, en la etapa Agentes, cada uno abre esta misma pantalla desde una lista. Acá no se crean.
- **Para comparar:**
  - **A favor:** cada parte en su lugar, como Información del negocio; se vuelve directo a la que se quiere cambiar.
  - **En contra:** para un negocio que empieza no dice por dónde seguir; lo que falta en otra pestaña no se ve.
- **Abajo, sección por sección:**
  - **Vacío, probar, el celular y el inglés.** El mismo contenido que AS2–AS5, repartido en pestañas.
    - «Vacío, para un negocio que empieza»: Se abre en Perfil, con un aviso: hasta que se configure, el asistente hace lo de hoy. «Cualquier otra cosa» empieza en «No responder».
    - «Probar, en un panel al costado»: La pestaña queda atrás; el panel se cierra con Escape.
    - «En el celular, a 390 px»: Las pestañas se desplazan de costado; Guardar queda abajo, fijo.
    - «En inglés y sin permiso»: «Main agent», «Profile», «Knowledge», «Cases», «Limits», «Test» y «Anything else».
- **Reglas del 2026-10-23:** Sin choques: Descartar y Guardar quedan sin «Cambios sin guardar».
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-AS1-A-Pestanas.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as1-a-pestanas.png).
  - **Maqueta:** Sí, como propuesta «Propuesta · AS1-A»: WhatsApp → «Asistente», ruta `/whatsapp/asistente`. Pide «Configurar el asistente».

#### AS1-B · Una página guiada

Archivo `FWA-AS1-B-Guiada.dc.html` («Asistente · AS1 · B · página guiada»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-21, versión 98.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** una sola página guiada en cinco pasos, con «N de 4 listos» y un solo Guardar arriba.
  - **Abajo:** vacío, el último paso, el celular y el inglés.
- **Título y bajada de las notas:** «AS1 · B · Una página guiada». Concepto B. Una sola página, de arriba abajo, en cinco pasos: quién es, qué sabe, qué hace con cada mensaje, hasta dónde llega y probalo.
- **Notas numeradas:**
  1. **Cuánto falta.** A la izquierda, los pasos con su estado y «N de 4 listos». Tocar uno lleva a su lugar en la página.
  2. **Lo hecho, resumido.** Un paso listo muestra una línea con lo cargado y «Editar», que lo abre en el lugar.
  3. **El paso abierto.** Se edita en la misma página. Los casos son los de AS2, con «Cualquier otra cosa» al final.
  4. **Probalo, al final.** El último paso es la prueba (AS3), en la misma página, debajo de los límites.
  5. **Un solo guardado.** Arriba, para toda la página, con revisión y 409.
  6. **El agente principal.** La pantalla es la de un agente: el principal, que atiende todas las conversaciones en modo Asistente. Cuando haya varios, en la etapa Agentes, cada uno abre esta misma pantalla desde una lista. Acá no se crean.
- **Para comparar:**
  - **A favor:** para empezar dice qué falta y en qué orden; todo se ve en un solo lugar, de corrido.
  - **En contra:** con muchos casos la página se alarga; para cambiar algo puntual hay que ubicarlo en el recorrido.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-AS1-B-Guiada.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### AS1-C · Una lista con el detalle al costado

Archivo `FWA-AS1-C-ListaDetalle.dc.html` («Asistente · AS1 · C · lista y detalle»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-21, versión 98.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** una lista con el perfil, el conocimiento, los límites y los casos, y el detalle a la derecha. Probar queda en una línea fija abajo.
  - **Abajo:** vacío, el celular y el inglés.
- **Título y bajada de las notas:** «AS1 · C · Una lista con el detalle al costado». Concepto C. Todo es una lista: arriba el perfil, el conocimiento y los límites; después los casos en orden. Lo que se toca se edita a la derecha.
- **Notas numeradas:**
  1. **La lista entera, siempre a la vista.** Cada ítem dice lo suyo en una línea y si está listo. Los casos llevan su número y se ordenan arrastrando.
  2. **Los casos, en el orden en que se eligen.** Primero los propios, después los de los formularios y al final «Cualquier otra cosa».
  3. **El detalle, a la derecha.** El editor del caso (AS2) o el del perfil, el conocimiento o los límites. Quitar está arriba.
  4. **Probar, siempre abajo.** Una línea fija para probar un mensaje sin moverse; «Ver el detalle» abre el resultado completo (AS3).
  5. **Un solo guardado.** Arriba, para todo, con revisión y 409.
  6. **El agente principal.** La pantalla es la de un agente: el principal, que atiende todas las conversaciones en modo Asistente. Cuando haya varios, en la etapa Agentes, cada uno abre esta misma pantalla desde una lista. Acá no se crean.
- **Para comparar:**
  - **A favor:** se ve todo el asistente mientras se edita una parte; probar y ajustar sin cambiar de lugar.
  - **En contra:** es la más densa; en el celular la lista y el detalle pasan a ser dos pantallas.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-AS1-C-ListaDetalle.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### AS2 · Los casos

Archivo `FWA-AS2-Casos.dc.html` («Asistente · AS2 · los casos»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 98.
- **Programado:** No.
- **Qué muestra:**
  - **La pestaña Casos con el editor «Caso 3» abierto al costado:**
    - Cuándo;
    - Tipo de entrada;
    - Qué hacer, con las seis acciones y una línea de cada una;
    - la configuración de «Preguntar con botones»: la pregunta, y los botones con su destino y «Agregar botón»;
    - Activo, Quitar caso, Cancelar y Listo.
  - **Abajo:** lo que pide cada acción, un caso de formulario, «Cualquier otra cosa», el celular, el inglés y quién lo edita.
- **Título y bajada de las notas:** «AS2 · Los casos». La lista dice qué hace el asistente con cada mensaje, en orden. El editor de un caso: cuándo, el tipo de entrada, qué hacer y su configuración.
- **Notas numeradas:**
  1. **Los casos propios, en orden.** El asistente elige el que encaja, de arriba abajo. Si le piden varias cosas, responde con hasta 3 acciones, en orden (AS3). Se ordenan arrastrando; uno pausado se saltea.
  2. **Tipo de entrada.** Cualquier entrada, texto o audio (ya transcripto). Es una lista, para sumar fotos y documentos en la etapa siguiente.
  3. **Qué hacer, de un menú cerrado.** Seis opciones, las que el agente tiene permitidas (AS5). Es lo único que vive en el código: una opción nueva es una más del menú, nunca una respuesta escrita en el código.
  4. **La configuración de cada una.** Cambia con la opción elegida: el formulario, el texto exacto, las instrucciones de la respuesta redactada, los botones y su destino, o el motivo.
  5. **Los botones y su destino.** Hasta 3. Cada uno manda un formulario, responde un texto exacto o deriva.
  6. **Los de los formularios.** Salen de «Cuándo lo ofrece» de cada formulario, sin duplicarlo: se editan en el formulario.
  7. **«Cualquier otra cosa».** Siempre existe y va al final. Se edita qué hace; no se borra, no se pausa ni se mueve.
- **Abajo, sección por sección:**
  - **La configuración de cada acción, los formularios, «Cualquier otra cosa», el celular y el inglés.** Arriba, el editor con «Preguntar con botones». Acá, lo que pide cada una de las otras opciones.
    - «Mandar un formulario»: Uno publicado, elegido de la lista.
    - «Responder con un texto exacto»: Español obligatorio; inglés opcional, como los formularios.
    - «Responder con la información del negocio»: Las instrucciones de la respuesta redactada.
    - «Derivar a una persona»: El motivo, para el chat y la lista.
    - «No responder»: Sin configuración.
    - «Un caso de un formulario»: «Editar en el formulario» abre el editor de Registrar mantenimiento en «Cuándo lo ofrece el asistente» (03).
    - ««Cualquier otra cosa»»: El mismo editor, con el cuándo fijo y sin quitar ni pausar.
    - «En el celular, a 390 px»: Cada caso en su renglón, con las acciones debajo. El editor ocupa la pantalla, con Listo abajo.
    - «En inglés»: «When», «Input type», las seis opciones y «Anything else». Los textos del negocio quedan como se escribieron.
    - «Quién lo edita»: Ver y editar la pantalla pide «Configurar el asistente» (whatsapp.configureassistant), el mismo permiso que hoy pasa una conversación a Asistente. Sin él, «Asistente» no aparece en el panel de WhatsApp y por la dirección dice «No tenés permiso». Editar un caso de un formulario pide además «Administrar formularios», en el editor del formulario.
- **Reglas del 2026-10-23:**
  - **Texto junto a un botón:** «Agregar botón» lleva al costado «Hasta 3, como permite WhatsApp. Cada botón manda un formulario, responde un texto exacto o deriva.».
  - **Dos campos por idioma:** «Responder con un texto exacto» tiene dos campos: español, obligatorio, y «En inglés (opcional)». Con la regla, es un solo campo en el idioma del menú, y el otro idioma se carga cambiando el menú. Como no está programado, conviene dibujarlo ya así.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-AS2-Casos.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as2-casos.png).
  - **Maqueta:** Sí, como propuesta, en la pestaña Casos de `/whatsapp/asistente`. Los casos se arrastran y se pausan.

#### AS3 · Probar el asistente

Archivo `FWA-AS3-Probar.dc.html` («Asistente · AS3 · probar»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 98.
- **Programado:** No.
- **Qué muestra:**
  - **El panel «Probar el asistente» al costado de Casos:**
    - «Como si escribiera», con Juan Gómez;
    - la caja de mensajes, uno por renglón, y Probar.
  - **Los resultados, el último arriba:**
    - la ráfaga de 3 mensajes, con 3 acciones: un texto, Registrar mantenimiento y Pedir repuesto. Cada acción trae el caso, por qué, lo que usó y cómo la ve la persona. Al final, «No se mandó nada»;
    - debajo, la prueba de «hola».
  - **Abajo:** los otros ejemplos, cuándo no se puede probar, el celular, el inglés y quién prueba.
- **Título y bajada de las notas:** «AS3 · Probar el asistente». Se escribe lo que mandaría alguien y se ve qué haría el asistente, en orden y por qué. No se manda nada ni queda en ningún chat.
- **Notas numeradas:**
  1. **Como si escribiera alguien.** Se elige una cuenta vinculada: los formularios dependen de lo que esa cuenta puede completar. La prueba usa lo guardado.
  2. **Una ráfaga.** Un mensaje por renglón se prueba como varios mensajes seguidos: el asistente espera 4 s sin mensajes nuevos, como mucho 15, y responde a todo junto.
  3. **Las acciones, en orden.** Hasta 3, cada una con su caso, por qué, lo que usó y cómo la vería la persona. Se validan por separado, y los formularios cuentan para los 2 ofrecimientos del día.
  4. **Las pruebas se acumulan.** La última arriba. Se borran al cerrar el panel; no quedan guardadas.
- **Abajo, sección por sección:**
  - **Los otros mensajes de ejemplo, cuándo no se puede, el celular y el inglés.** Los mensajes de ejemplo, con la información cargada y sin ella.
    - ««¿qué hora es?»»: Cae en «Cualquier otra cosa»: contesta la hora del negocio.
    - ««¿hacen envíos a Córdoba?», con la información cargada»: Redacta con la pregunta frecuente y la zona de entrega.
    - «La misma pregunta, sin esa información»: No encuentra la respuesta: deriva, como dicen las instrucciones de «Cualquier otra cosa».
    - ««se rompió la heladera en la sucursal Centro»»: Encaja con el «Cuándo lo ofrece» de un formulario: lo manda prellenado.
    - ««quiero hablar con alguien»»: El caso 1: deriva, con el mensaje fijo del perfil.
    - «Cuándo no se puede probar»: Lo mismo que hoy para el modo Asistente.
    - «En el celular, a 390 px»: Ocupa la pantalla; el resultado queda debajo de los mensajes.
    - «En inglés»: La pantalla en inglés. Si la persona escribe en inglés, la respuesta sale en inglés.
    - «Quién prueba»: Probar pide «Configurar el asistente», como la pantalla. No hace falta poder escribir en el chat: no se manda nada.
- **Reglas del 2026-10-23:**
  - **«Cambios sin guardar»:** el caso «Cuándo no se puede probar» muestra «Tenés cambios sin guardar: la prueba usa lo guardado.» Choca con la regla. El diseño del front, en AS3, decía «avisa si hay cambios sin guardar».
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-AS3-Probar.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as3-probar.png).
  - **Maqueta:** Sí, como propuesta: «Probar», al costado, en `/whatsapp/asistente`.

#### AS4 · El chat

Archivo `FWA-AS4-Chat.dc.html` («Asistente · AS4 · el chat»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 98.
- **Programado:** No.
- **Qué muestra:**
  - **El chat de WhatsApp:**
    - los filtros «Todos (4) · Sin leer (1) · Necesita una persona (1)»;
    - la lista de chats, con sus marcas.
  - **La conversación de Ana Martínez:**
    - en la cabecera, «Necesita una persona» y Atención manual;
    - una franja con el motivo;
    - la respuesta del asistente, con «Por qué respondió esto»;
    - el mensaje al derivar y la línea «El asistente derivó la conversación…»;
    - el compositor.
  - **Abajo:** los otros desenlaces, la ráfaga con 3 acciones, el celular, el inglés y quién lo ve.
- **Título y bajada de las notas:** «AS4 · El chat». Lo que hace el asistente queda en el chat, con su motivo: cada acción, el suyo. Una derivación marca la conversación hasta que alguien responde.
- **Notas numeradas:**
  1. **Una respuesta redactada.** Sale como «Asistente», igual que hoy un formulario.
  2. **Por qué respondió esto.** Debajo de cada acción: el caso, lo que usó y el texto, guardado tal como salió. No vuelve a consultar a la IA.
  3. **El mensaje al derivar.** El texto fijo del perfil, con «mensaje al derivar» en el pie.
  4. **Derivar pasa a Atención manual.** Para que el asistente no siga contestando mientras la persona espera. Queda anotado con el motivo y el caso.
  5. **«Necesita una persona».** En la cabecera y en la franja de arriba, con el motivo. Se quita cuando alguien le responde.
  6. **En la lista de chats.** La misma marca en la fila y un filtro, «Necesita una persona (N)», para encontrarlas.
- **Abajo, sección por sección:**
  - **Los otros desenlaces, varias acciones, el celular y el inglés.** Cada acción del asistente dice por qué; lo que no se contesta también queda.
    - «Un texto exacto»: Sale tal cual lo escribió el negocio; el motivo lo dice.
    - «Una pregunta con botones»: Lo que toca la persona sigue el destino de cada botón.
    - «No responder»: No sale nada; queda la línea con el caso.
    - «Al llegar al tope del día»: El límite de respuestas por conversación y día también deriva.
    - «En Atención manual, un borrador para el operador»: Elegido el 2026-10-21 con AS4: reusa la sugerencia de hoy (H2), sin un mecanismo nuevo.
    - «Una ráfaga, con tres acciones»: Tres mensajes seguidos, una sola respuesta con tres acciones en orden: un texto y dos formularios, cada uno con su «por qué».
    - «La lista, a 390 px»: La marca y el filtro, igual que en la computadora.
    - «La conversación, a 390 px»: La franja queda arriba; el motivo, debajo de la respuesta.
    - «En inglés»: «Why it replied this», «Why it sent it», «Needs a person» y «Handoff message». Lo que escribió la persona y el negocio, como está.
    - «Quién lo ve»: La marca y los porqués se ven con «Ver chats» (whatsapp.read), como el resto de la conversación. Responder pide «Responder chats»; volver a poner la conversación en Asistente, «Configurar el asistente». No hay avisos fuera del sistema: ni correo ni notificaciones. La marca está en el chat y en la lista.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-AS4-Chat.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as4-chat.png).
  - **Maqueta:** Sí, como propuesta en el chat: los porqués, «Necesita una persona» y el borrador para el operador.

#### AS5 · Límites y conocimiento

Archivo `FWA-AS5-Limites.dc.html` («Asistente · AS5 · límites y conocimiento»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 98.
- **Programado:** No.
- **Qué muestra:**
  - **Pestaña Límites:**
    - lo que vale siempre;
    - temas que no responde, como etiquetas;
    - lo que no puede prometer;
    - largo máximo: 400;
    - tope de respuestas: 10 por conversación y día;
    - acciones que puede usar;
    - cuando deriva.
  - **Conocimiento:** «Editar en Información del negocio» y cada sección con su estado.
  - **Abajo:** el conocimiento vacío o con un texto anterior sin revisar, el celular, el inglés y los permisos.
- **Título y bajada de las notas:** «AS5 · Límites y conocimiento». Hasta dónde llega el asistente cuando redacta, y la información del negocio que usa, tal como se ve desde la pantalla Asistente.
- **Notas numeradas:**
  1. **Lo que vale siempre.** Las reglas decididas para las respuestas redactadas, a la vista y sin opción de apagarlas.
  2. **Temas que no responde.** Etiquetas que se suman y se quitan. Si le preguntan por uno, deriva.
  3. **Largo máximo.** En caracteres. El sistema lo vuelve a revisar: una respuesta que se pasa no sale y deriva.
  4. **Tope de respuestas.** Por conversación y día, aparte de los 2 ofrecimientos de formularios. Al llegar, deriva.
  5. **Cuando deriva.** El mensaje fijo es el del perfil; acá se ve junto a lo que lo dispara.
  6. **El conocimiento.** Es la Información del negocio que ya existe, de solo lectura acá: qué secciones están cargadas y qué dice cada una. Se edita en su pantalla.
  7. **Acciones que puede usar.** Las del menú cerrado que el agente tiene permitidas. Lo que se apaga no se ofrece en el editor de casos; con varios agentes, cada uno tendrá las suyas.
- **Abajo, sección por sección:**
  - **El conocimiento vacío o con algo para revisar, el celular y el inglés.** La Información del negocio no se copia: se lee la misma.
    - «Sin información del negocio»: Para un negocio que empieza: lo dice y no inventa.
    - «En inglés»: «Limits», «Knowledge», «Loaded», «Not loaded» y «Earlier text, not reviewed».
    - «Con un texto anterior sin revisar»: La deuda que se corrige antes de usarla: un texto guardado antes de las opciones nuevas no se presenta como un hecho.
    - «Quién lo ve y lo edita»: Ver y editar la pantalla pide «Configurar el asistente» (whatsapp.configureassistant), el mismo permiso que hoy pasa una conversación a Asistente. Sin él, «Asistente» no aparece en el panel de WhatsApp y por la dirección dice «No tenés permiso». El conocimiento se ve acá con ese mismo permiso. Editarlo pide «Administrar información del negocio», en su pantalla.
    - «En el celular, a 390 px»: Una columna; el conocimiento, cada sección con su estado arriba.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-AS5-Limites.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as5-limites-y-conocimiento.png).
  - **Maqueta:** Sí, como propuesta: las pestañas Límites y Conocimiento.

#### AS6 · El circuito de punta a punta

Archivo `FWA-AS6-Circuito.dc.html` («Asistente · AS6 · el circuito»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 98.
- **Programado:** No.
- **Qué muestra:**
  - **Tablero de casos:** seis ejemplos numerados. Cada uno con el celular de la persona, el chat del operador y quién hace qué.
  - **Abajo:** cuatro notas al pie.
- **Encabezado del tablero:** «AS6 · El circuito de punta a punta». Los mensajes de ejemplo y una ráfaga, en el celular de la persona y en el chat del operador: qué hace la persona, qué hace el sistema y qué hace la IA.
- **Casos numerados (son las notas numeradas de este tablero):**
  1. **«hola».** Un saludo.
  2. **«¿qué hora es?».** Una pregunta general.
  3. **«¿hacen envíos a Córdoba?».** Una consulta con la información cargada.
  4. **«se rompió la heladera en la sucursal Centro».** Un pedido que es un formulario.
  5. **«quiero hablar con alguien».** Una derivación.
  6. **Una ráfaga: «Hola», «Estoy en la sucursal Centro», «Se rompió la heladera y necesito un repuesto».** Tres mensajes seguidos y una sola respuesta, con hasta 3 acciones en orden: acá, un texto y dos formularios.
- **Notas al pie:**
  - **Lo que sale, queda.** Cada respuesta guarda el caso, lo que usó y el texto: es lo que muestra «Por qué respondió esto».
  - **Quién ve qué.** El chat y sus motivos, con «Ver chats»; la pantalla Asistente, con «Configurar el asistente». El chat a 390 px está en AS4.
  - **La IA elige y redacta; el sistema decide.** El sistema vuelve a revisar los límites y los permisos antes de mandar. Lo que no pasa, no sale y deriva.
  - **Como hoy.** Solo en conversaciones en modo Asistente y dentro de las 24 horas. Tomar la atención manual lo frena; responde en el idioma de la persona.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-AS6-Circuito.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/asistente-casos/as6-circuito.png).
  - **Maqueta:** Sí: «Recorridos» → AS6, en `/propuestas/as6`. Pasa a Juan a modo Asistente y escribe desde su celular.

## Catálogos: carga de una vez, tipo de valor y elegir varias (CA1–CA6)

Dibujados y elegidos el 2026-10-21, en la versión 102 del lienzo: CA1-A y CA2–CA6. La versión 110 suma el ajuste de claridad de la vista previa en CA1-A.

Programados como Catálogos E1, con el review cerrado el 2026-10-22 ([reglas](../../features/formularios.md#catálogos-base-de-la-etapa-e1--2026-10-21)). El piloto real se hace junto con el de E2. CA1-B se retiró del lienzo el 2026-10-22, en la versión 119.

**Diseño congelado en el front:** [catalogos-mejoras.md](../../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md#los-tableros).

**Nota guía de la sección «Catálogos · carga de una vez, tipo de valor y elegir varias» en el lienzo** (textual):

> Catálogos: carga de una vez, tipo de valor y elegir varias. CA1-A y CA2–CA6 elegidos el 2026-10-21; por programar. CA1-B, la alternativa que no se eligió, se sacó del lienzo el 2026-10-22 para hacer lugar: sigue en el repo lienzo-formularios, en retirados/. Dibujados el 2026-10-21.
>
> La carga de a una opción (Agregar opción y el lápiz de cada fila) queda tal cual en todos los tableros: pegar, importar y la tabla editable son caminos adicionales para listas grandes.
>
> CA1 · El editor de un catálogo escrito a mano, en dos conceptos de dónde viven las acciones nuevas: A, todo a la vista (barra de Opciones, tabla editable y el desplegable de WhatsApp al costado); B, la pantalla de hoy más un menú «Cargar de una vez» y una vista de planilla. Cada uno con el estado vacío, 1.200 opciones, el celular y el inglés.
> CA2 · Importar y pegar: el mismo recorrido, con errores por fila y columna, las diferencias, las protecciones y el 409.
> CA3 · Tipo de valor: se elige al crearlo; la columna «Valor», el editor de formularios, el Excel y el PDF.
> CA4 · Elegir varias: mínimo y máximo, la pregunta publicada, el tope de 20 opciones y la vista previa.
> CA5 · Las respuestas con varias opciones: la planilla, «alguna de», el detalle, el chat, el Excel y el PDF.
> CA6 · El circuito de punta a punta.
>
> Ajuste de claridad, 2026-10-21 (la elección no cambia): la vista previa «Así lo ve el empleado» muestra por defecto el campo cerrado, con «Elegí una opción»; al tocarlo, la lista con los círculos de una sola opción, que se cierra al elegir o con «Volver»; y arriba, «Una opción / Varias»: lo decide cada pregunta del formulario. En el navegable, la vista previa se toca.
>
> Para recorrerlo tocando, el prototipo navegable tiene siete recorridos: https://claude.ai/artifact/2noVifPc2MX33UnNvT2jhY.
>
> Reglas en ArquitecturaBase/docs/plans/2026-10-21-catalogos-con-origenes-externos.md.

#### CA1-A · Todo a la vista

Archivo `FWA-CA1-A-Vista.dc.html` («Catálogos · CA1 · A · todo a la vista»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 102. Ajuste de claridad, versión 110.
- **Programado:** Sí: E1, el 2026-10-22.
- **Qué muestra:**
  - **Arriba:** el editor de «Tipo de mantenimiento», con «Activo · Usado en 2 formularios» y Dar de baja, Descartar y Guardar.
  - **Debajo:** «Textos en», con su aclaración, y «El catálogo», con Nombre, De dónde salen y Descripción.
  - **A la derecha, «Así lo ve el empleado»:** «Una opción / Varias» y el campo cerrado «Elegí una opción».
  - **«Opciones»:**
    - en la barra, Agregar opción, Pegar desde Excel, Importar, Exportar y «⋯»;
    - la búsqueda;
    - la tabla editable, con una casilla por fila, Texto (con el contador 10/30), Código, Detalle, Estado y las acciones.
  - **Abajo:** vacío, 1.200 opciones, la vista previa al tocarla y con «Varias», el lápiz, el texto repetido, el menú «⋯», el inglés con los permisos y el celular.
- **Título y bajada de las notas:** «CA1 · A · Todo a la vista». Concepto A. Lo nuevo está siempre a la vista: en la barra de Opciones, en la tabla, que se edita en el lugar, y en el desplegable de WhatsApp al costado. Agregar opción y el lápiz quedan como hoy.
- **Notas numeradas:**
  1. **Agregar opción, como hoy.** Primero en la barra. Suma una fila, y el lápiz de cada fila abre la opción entera, con su texto, código, detalle y estado.
  2. **Pegar, importar y exportar, al lado.** Botones en la misma barra. «⋯» tiene la plantilla, ordenar de la A a la Z y generar los códigos que faltan. Pegar e importar usan el mismo recorrido (CA2).
  3. **La tabla se edita en el lugar.** En pantallas anchas, cada celda es un campo: el texto cuenta hasta 30 y los repetidos avisan. En el celular sigue el editor por opción.
  4. **Elegir varias filas.** Una casilla por fila y otra para las de esta página. Aparece una barra para darlas de baja o reactivarlas juntas.
  5. **Así lo ve el empleado.** Al costado, como lo ve la persona: por defecto, el campo cerrado; al tocarlo, la lista, con lo que se está editando. Arriba, «Una opción» o «Varias»: lo decide cada pregunta. En la cabecera, «Usado en N formularios», solo el número.
- **En las notas, «Ajuste de claridad, 2026-10-21»:** La vista previa mostraba solo la lista abierta y parecía de elegir varias. Ahora muestra por defecto el campo cerrado, con «Elegí una opción»; al tocarlo, la lista con los círculos de una sola opción; y arriba, «Una opción / Varias».
- **Para comparar:**
  - **A favor:** todo se encuentra sin buscar; cargar muchas opciones es escribir en la tabla, como en una planilla.
  - **En contra:** con 3 opciones se ven más botones y casillas de los que hacen falta, y la tabla editable cambia el aspecto de la pantalla de hoy.
- **Abajo, sección por sección:**
  - **Vacío, 1.200 opciones, el lápiz, el celular y el inglés.** La misma sección Opciones en todos los casos; lo nuevo se suma sin sacar nada de lo de hoy.
    - «Vacío, en un catálogo nuevo»: Agregar opción, como hoy. Pegar e importar, apagados con su motivo hasta guardar el catálogo.
    - «1.200 opciones, con búsqueda y páginas»: Se eligen filas de la página y la barra las da de baja o las reactiva. Para cambios grandes, exportar e importar.
    - «Así lo ve el empleado, al tocarlo y con «Varias»»: Ajuste de claridad, 2026-10-21: por defecto se ve el campo cerrado, y arriba, «Una opción / Varias». Lo decide cada pregunta del formulario.
    - «Al tocarlo»: La lista, con los círculos de una sola opción. «Volver» o la elección la cierran, y el campo muestra lo elegido.
    - «Con «Varias»»: Las casillas cuadradas, con «Elegí hasta N», como en CA4.
    - «El lápiz, como hoy, con las ayudas»: Abre la opción debajo de su fila. El texto cuenta hasta 30; un código repetido no deja guardar y dice dónde está el otro.
    - «Un texto repetido avisa, sin frenar»: El código es la identidad y no se puede repetir; el texto sí, con el aviso.
    - «En inglés y quién lo ve»: «Sort A to Z», «Generate missing codes» y «Save the catalog to paste or import». Exportar y bajar la plantilla piden «Ver catálogos»; agregar, pegar, importar, editar y dar de baja, «Administrar catálogos». Con solo ver, la tabla es de lectura y sin casillas.
    - «En el celular, a 390 px»: Sin tabla editable: se edita por opción, con el lápiz. Pegar, importar y exportar, en «⋯».
- **Reglas del 2026-10-23:**
  - **Selector de idioma:** «Textos en», con «Completá el nombre y la descripción en español e inglés. En las opciones, el inglés es opcional.» No va: hay un solo campo por texto, en el idioma del menú.
  - **«El catálogo»:** muestra «De dónde salen · Escritas a mano · No se cambia: para otro origen se arma otro catálogo.» como un campo más. Va así:
    - arriba, un dato de solo lectura, «Escritas a mano · Texto»;
    - debajo, solo Nombre y Descripción, que es opcional.
  - **Texto junto a los botones apagados:** en el caso «Vacío», debajo de Pegar e Importar apagados dice «Guardá el catálogo para pegar o importar: las opciones se cargan sobre un catálogo guardado.»; en inglés, «Save the catalog to paste or import». Es el punto 37 del review: se saca. El motivo va como tooltip y los botones, al borde derecho, a la altura del título.
  - **«Sin guardar»:** el catálogo nuevo dice «Sin guardar» en el encabezado. Se saca.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CA1-A-Vista.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/ca1-a-todo-a-la-vista.png) · [Front E1, es, 1440 px](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-ca1-es-1440.jpg) · [Front E1, es, 390 px](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-ca1-es-390.jpg).
  - **Prototipo navegable:** Sí: «Catálogos», recorridos 1 a 3.
  - **Maqueta:** Sí, como pantalla real: E1 ya está programado y la maqueta simula su backend.

#### CA1-B · Un menú y una vista de planilla

Archivo `FWA-CA1-B-Menu.dc.html` («Catálogos · CA1 · B · un menú y una planilla»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-21, versión 102.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** la sección Opciones de hoy, más el menú «Cargar de una vez» y una vista Lista/Planilla. La vista previa se abre en un diálogo.
  - **Abajo:** los mismos casos que A.
- **Título y bajada de las notas:** «CA1 · B · Un menú y una vista de planilla». Concepto B. La sección Opciones queda como hoy. Lo nuevo vive en un solo menú, «Cargar de una vez», y en una vista de planilla que se elige cuando hace falta.
- **Notas numeradas:**
  1. **Lo de hoy, igual.** Agregar opción, las flechas para ordenar y el lápiz que abre la opción. Para 3 opciones no cambia nada a la vista.
  2. **«Cargar de una vez».** Un menú con pegar desde Excel, importar un archivo, exportar y descargar la plantilla. Pegar e importar usan el mismo recorrido (CA2).
  3. **Lista o planilla.** En pantallas anchas, «Planilla» muestra la misma tabla para editar en las celdas y elegir varias filas, con ordenar de la A a la Z y generar los códigos que faltan. En el celular no está.
  4. **Usado en N formularios.** En la cabecera, junto al estado: solo el número.
  5. **Vista previa.** Abre el desplegable de WhatsApp en un diálogo, sin ocupar lugar fijo.
- **Para comparar:**
  - **A favor:** la pantalla de siempre no cambia; lo masivo aparece cuando se pide y no distrae con listas chicas.
  - **En contra:** pegar, importar y la planilla están a un clic más, y hay que saber que existe «Cargar de una vez».
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-CA1-B-Menu.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### CA2 · Importar (y pegar)

Archivo `FWA-CA2-Importar.dc.html` («Catálogos · CA2 · importar y pegar»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 102.
- **Programado:** Sí: E1, el 2026-10-22.
- **Qué muestra:**
  - **Detrás:** el editor de «Proveedores», con 1.097 opciones.
  - **El diálogo «Importar opciones a «Proveedores»», en el paso Revisar:**
    - el archivo y las filas leídas;
    - pestañas de Errores, Altas, Cambios y Bajas;
    - la tabla de errores, por fila y columna;
    - ejemplos de cambios y bajas;
    - Cancelar y «Aplicar sin las 2 filas con errores».
  - **Abajo:** el archivo y la plantilla, pegar, cuándo no se puede, el 409, exportar, el celular, el inglés y los permisos.
- **Título y bajada de las notas:** «CA2 · Importar (y pegar)». Pegar desde Excel e importar un archivo son el mismo recorrido: el archivo o las filas pegadas, revisar y aplicar. Nada se guarda hasta aplicar.
- **Notas numeradas:**
  1. **Tres pasos.** Archivo (bajar la plantilla y subirla, o pegar), revisar y aplicar. Lo pegado pasa por la misma revisión que un archivo.
  2. **Qué trae.** Cuántas filas leyó y qué cambia: altas, cambios, bajas y filas con errores.
  3. **Errores por fila y columna.** «Fila 37 · Código: repetido», con el motivo. Las filas con errores no entran.
  4. **Bajas por inactivación.** Lo que no viene en la planilla se da de baja, no se borra, y vuelve si aparece en otra. Si daría de baja una parte grande, pide confirmarlo.
  5. **Aplicar.** Una sola vez, sobre el catálogo guardado y con su revisión. Se aplica todo junto y queda en el historial.
- **Abajo, sección por sección:**
  - **El archivo, pegar, cuándo no se puede, el 409, exportar, el celular y el inglés.** El mismo recorrido para un archivo y para lo pegado.
    - «1 · El archivo y la plantilla»: La plantilla trae una columna por campo, si es obligatoria y una fila de ejemplo.
    - «Pegar es lo mismo»: Lo pegado no entra directo al catálogo: pasa por la misma revisión.
    - «Cuándo no se puede»: Pegar e importar trabajan sobre lo guardado.
    - «Si alguien guardó antes (409) y las protecciones»: La revisión vieja no se aplica: se revisa de nuevo con lo guardado.
    - «Exportar»: Baja todas las opciones, activas y dadas de baja, con las columnas de la plantilla: se editan afuera y se vuelven a importar.
    - «En el celular, a 390 px»: Los pasos uno debajo del otro; los botones, fijos abajo.
    - «En inglés»: «Import options», «Download the template», «New», «Changes», «Deactivated» y «Apply without the 2 rows with errors».
    - «Quién importa»: Pegar e importar piden «Administrar catálogos»; exportar y bajar la plantilla, «Ver catálogos».
- **Reglas del 2026-10-23:**
  - **Selector de idioma:** «Textos en», con «Completá el nombre y la descripción en español e inglés. En las opciones, el inglés es opcional.» No va: hay un solo campo por texto, en el idioma del menú.
  - **«El catálogo»:** muestra «De dónde salen · Escritas a mano · No se cambia: para otro origen se arma otro catálogo.» como un campo más. Va así:
    - arriba, un dato de solo lectura, «Escritas a mano · Texto»;
    - debajo, solo Nombre y Descripción, que es opcional.
  - **Texto que explica botones apagados:** el caso «Cuándo no se puede» dice «Guardá el catálogo para pegar o importar…» y «Guardá o descartá los cambios para pegar o importar.» Con la regla, va como tooltip.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CA2-Importar.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/ca2-importar-y-pegar.png) · [Front E1, es, 1440 px](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-ca2-es-1440.jpg).
  - **Prototipo navegable:** Sí: «Catálogos», recorridos 2 y 3.
  - **Maqueta:** Sí, como pantalla real.

#### CA3 · Tipo de valor del catálogo

Archivo `FWA-CA3-TipoValor.dc.html` («Catálogos · CA3 · tipo de valor»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 102.
- **Programado:** Sí: E1, el 2026-10-22.
- **Qué muestra:**
  - **Arriba:** el editor de «Recargo por zona», que es de valor decimal, con «Activo · Cambios sin guardar».
  - **«El catálogo»:** De dónde salen y Tipo de valor, Decimal, como campos.
  - **La tabla:** suma la columna Valor. La opción «Interior» está abierta, con el error «mil no es un número decimal».
  - **Abajo:** al crearlo, el editor de formularios, el Excel y el PDF, la plantilla, el inglés, los permisos y el celular.
- **Título y bajada de las notas:** «CA3 · Tipo de valor del catálogo». Un catálogo puede decir qué es el valor de cada opción: texto, como hasta hoy, entero, decimal o fecha. Lo que se ve en WhatsApp sigue siendo el texto.
- **Notas numeradas:**
  1. **Se elige al crearlo.** Texto por defecto. No se cambia después; los catálogos de la base son siempre de texto.
  2. **La columna «Valor».** Solo en los catálogos tipados, y obligatoria. Se valida al escribir, al pegar y al importar. El código sigue siendo la identidad de la empresa.
  3. **No deja guardar un valor de otro tipo.** El editor lo marca en el campo. Al pegar o importar, dice la fila y la columna: «Fila 3 · Valor: no es un número decimal».
- **Abajo, sección por sección:**
  - **Al crearlo, en los formularios, en el Excel y el PDF, la plantilla, el celular y el inglés.** El valor acompaña al texto; no lo reemplaza.
    - «Al crearlo»: El diálogo de hoy suma el tipo de valor, solo para las escritas a mano. Texto viene marcado.
    - «En el editor de formularios»: Al elegir el catálogo se ve su tipo.
    - «En el Excel y el PDF»: WhatsApp, los filtros, las reglas y el asistente no cambian: siguen trabajando por opción.
    - «La plantilla»: La columna del valor dice su tipo.
    - «En inglés»: «Value type», «Text», «Whole number», «Decimal», «Date» y «Row 3 · Value: not a decimal number».
    - «Quién lo usa»: Elegir el tipo y cargar los valores piden «Administrar catálogos». En el editor de formularios, el tipo se ve con «Administrar formularios».
    - «En el celular, a 390 px»: La opción se edita entera, con el valor.
- **Reglas del 2026-10-23:**
  - **«Cambios sin guardar»:** el encabezado dice «Activo · Cambios sin guardar». Debe decir solo «Activo».
  - **Selector de idioma:** «Textos en», con «Completá el nombre y la descripción en español e inglés. En las opciones, el inglés es opcional.» No va: hay un solo campo por texto, en el idioma del menú.
  - **«El catálogo»:** muestra «Tipo de valor · Decimal · Se eligió al crearlo y no se cambia…» y «De dónde salen» como campos. Va arriba de Nombre, como dato de solo lectura: «Escritas a mano · Decimal». Se sacan las explicaciones.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CA3-TipoValor.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/ca3-tipo-de-valor.png) · [Front E1, es, 1440 px](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-ca3-es-1440.jpg).
  - **Prototipo navegable:** Sí: «Catálogos», recorrido 4.
  - **Maqueta:** Sí, como pantalla real.

#### CA4 · Elegir varias

Archivo `FWA-CA4-ElegirVarias.dc.html` («Catálogos · CA4 · elegir varias»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 102.
- **Programado:** Sí: E1, el 2026-10-22.
- **Qué muestra:**
  - **El editor de «Registrar mantenimiento», en borrador, con la pregunta «Fallas» abierta:** Elegir de un catálogo · Fallas, «Permitir elegir varias», y Mínimo 1 y Máximo 3, con sus ayudas.
  - **La lista de preguntas:** dice «varias, hasta 3».
  - **La vista previa:** casillas, con «Elegí hasta 3».
  - **Abajo:** una pregunta publicada, más de 20 opciones, opciones propias, el inglés, los permisos y el celular.
- **Título y bajada de las notas:** «CA4 · Elegir varias». «Elegir de un catálogo» y «Elegir una opción» suman «Permitir elegir varias», con un mínimo y un máximo. Elegir una sola sigue igual.
- **Notas numeradas:**
  1. **Permitir elegir varias.** Una casilla en la pregunta. Por dentro es otro tipo de pregunta: lo que hoy espera una sola opción no la lee mal.
  2. **Mínimo y máximo.** Si es obligatoria, el mínimo es 1. El máximo llega hasta 20, lo que muestra WhatsApp en una pregunta de varias.
  3. **En la lista de preguntas.** Dice «varias, hasta 3», junto al catálogo.
  4. **La vista previa.** Casillas en lugar del desplegable, con «Elegí hasta 3».
  5. **Sin prellenado, por ahora.** En el primer corte, el asistente no prellena una pregunta de varias ni la usa en las sugerencias entre formularios.
- **Abajo, sección por sección:**
  - **Una pregunta publicada, más de 20 opciones, opciones propias, el celular y el inglés.** Elegir una sola sigue igual; elegir varias es una opción nueva de la pregunta.
    - «Activarlo en una pregunta publicada»: Como cambiar de catálogo: es una pregunta nueva, y se avisa antes.
    - «Más de 20 opciones»: La comprobación de publicar lo frena y dice qué hacer. En un catálogo de la base, si a una persona le tocarían más de 20, el envío se bloquea con un aviso.
    - «Con opciones propias»: «Elegir una opción» suma lo mismo, con sus opciones escritas en la pregunta.
    - «En inglés»: «Allow choosing several», «Minimum», «Maximum», «Choose up to 3» y «WhatsApp shows up to 20 options».
    - «Quién lo configura»: Como hoy: el editor de formularios pide «Administrar formularios», y para elegir un catálogo hay que poder verlo.
    - «En el celular, a 390 px»: La pregunta se edita entera, con la misma casilla.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CA4-ElegirVarias.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/ca4-elegir-varias.png) · [Front E1, es, 1440 px](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-ca4-es-1440.jpg).
  - **Prototipo navegable:** Sí: «Catálogos», recorrido 5.
  - **Maqueta:** Sí, como pantalla real.

#### CA5 · Las respuestas con varias opciones

Archivo `FWA-CA5-Respuestas.dc.html` («Catálogos · CA5 · respuestas con varias»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 102.
- **Programado:** Sí: E1, el 2026-10-22.
- **Qué muestra:**
  - **La planilla de «Registrar mantenimiento»:** filtrada por «Fallas: alguna de 2», con el chip «Frío, Ruido»: 7 de 18. La columna Fallas muestra «Frío, Ruido».
  - **Abajo:** Más filtros, el detalle D2, la tarjeta del chat, el Excel, el PDF, el celular, el inglés y los permisos.
- **Título y bajada de las notas:** «CA5 · Las respuestas con varias opciones». Una pregunta de varias se lee igual en todos lados: las opciones elegidas, en el orden en que se eligieron, separadas por coma.
- **Notas numeradas:**
  1. **La celda de la planilla.** «Frío, Ruido». Si no entra, sigue en la línea de abajo. La columna no se ordena: una respuesta puede tener varias.
  2. **El filtro «alguna de».** Arriba, con cuántas opciones marcadas, y en un chip para quitarlo. Muestra las respuestas que eligieron alguna.
  3. **Más filtros.** Las opciones del catálogo con su cantidad. Una respuesta con Frío y Ruido cuenta en las dos.
- **Abajo, sección por sección:**
  - **Más filtros, el detalle, el chat, el Excel, el PDF, el celular y el inglés.** Las mismas opciones elegidas en cada lugar donde se ve la respuesta.
    - «En «Más filtros»»: Las opciones con su cantidad, contadas con los otros filtros aplicados.
    - «El detalle D2»: Una opción por renglón, en el orden en que se eligieron.
    - «La tarjeta en el chat»: La respuesta que llega a la conversación, separadas por coma.
    - «El Excel»: Una celda con las dos, separadas por punto y coma: «Frío; Ruido».
    - «El PDF»: Como en la planilla: «Frío, Ruido».
    - «En el celular, a 390 px»: La tabla se desplaza dentro de su caja; los filtros, arriba.
    - «En inglés»: «any of», «Faults» y «Cold, Noise». Las opciones salen en el idioma de cada una, como hoy.
    - «Quién lo ve»: Como hoy: la planilla, el detalle y la tarjeta piden «Ver respuestas»; el Excel, «Exportar respuestas».
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CA5-Respuestas.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/ca5-respuestas.png) · [Front E1, es, 1440 px](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-ca5-es-1440.jpg).
  - **Prototipo navegable:** Sí: «Catálogos», recorrido 7.
  - **Maqueta:** Sí, como pantalla real.

#### CA6 · El circuito de punta a punta

Archivo `FWA-CA6-Circuito.dc.html` («Catálogos · CA6 · el circuito»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 102.
- **Programado:** Es un circuito. Sus pantallas están programadas.
- **Qué muestra:**
  - **Tablero de casos:** el circuito en cuatro pasos numerados.
  - **Abajo:** tres notas al pie.
- **Encabezado del tablero:** «CA6 · El circuito de punta a punta». De cargar un catálogo de proveedores pegando desde Excel a mandarlo en un formulario con «elegir varias» y verlo en la planilla.
- **Casos numerados (son las notas numeradas de este tablero):**
  1. **Pegar los proveedores.** Ana copia 12 filas de su planilla de proveedores y las pega en el catálogo «Proveedores».
  2. **La pregunta con elegir varias.** En «Pedir repuesto», la pregunta usa el catálogo y permite hasta 3.
  3. **Juan elige dos en WhatsApp.** Las opciones salen del catálogo, como casillas.
  4. **En la planilla y el Excel.** La respuesta muestra los dos; el Excel, en una celda.
- **Notas al pie:**
  - **La carga de a una sigue.** Para una lista chica, Agregar opción como hoy. Pegar e importar son para las grandes.
  - **Lo pegado se revisa.** No entra directo: pasa por la misma revisión que un archivo, con errores por fila y columna.
  - **Varias, con su tope.** Una pregunta de varias muestra hasta 20 opciones en WhatsApp. Con más, se filtra por persona o se espera la búsqueda en vivo.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CA6-Circuito.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/ca6-circuito.png).
  - **Prototipo navegable:** Sí: los siete recorridos de «Catálogos».
  - **Maqueta:** Sí: «Recorridos» → CA6, en `/propuestas/ca6`.

## Catálogos: campos extra, qué viaja a WhatsApp y filtrar por persona (CE1–CE6)

Dibujados y elegidos el 2026-10-21, en la versión 106 del lienzo: CE1, CE2, CE3-B, CE4-A, CE5 y CE6.

Las versiones siguientes:

- la 107 los marca y suma la nota del módulo en CE4-A;

- la 108 suma el ajuste de la evaluación de impacto;

- la 110, el de claridad de la vista previa.

CE3-A queda en el lienzo: CE3-B toma de ahí la sección del catálogo. CE4-B se retiró el 2026-10-22, en la versión 119.

Programados como E2 el 2026-10-23 ([reglas](../../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23)). El review de E2 sigue abierto y de ahí salen las reglas del 2026-10-23.

**Diseño congelado en el front:** [catalogos-campos-extra.md](../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md#los-tableros).

**Nota guía de la sección «Catálogos · campos extra, qué viaja a WhatsApp y filtrar por persona» en el lienzo** (textual):

> Catálogos: campos extra (fase 2 de la hoja de ruta). CE1, CE2, CE3-B, CE4-A, CE5 y CE6 elegidos el 2026-10-21; por programar. CE3-A queda como la alternativa que no se eligió, porque CE3-B toma de ella la sección del catálogo. CE4-B se sacó del lienzo el 2026-10-22 para hacer lugar: sigue en el repo lienzo-formularios, en retirados/. Dibujados el 2026-10-21. Se apoyan en lo elegido para la próxima etapa (CA1-A) y no lo cambian.
>
> CE1 · Definir los campos: clave, nombre en español e inglés, tipo (texto, entero, decimal, fecha, sí o no, lista de valores), reglas (obligatorio, único, mínimo y máximo) y papel (filtro o dato). Quitar un campo con datos o en uso, y cambiar el tipo de uno cargado.
> CE2 · Cargar los valores: una columna por campo en la tabla de CA1-A, errores por celda, la plantilla y la revisión por tipo al pegar e importar.
> CE3 · Qué viaja a WhatsApp: el título (hasta 30) y la descripción (hasta 300) se arman con campos; lo demás no sale del sistema. Dos variantes: A, solo en el catálogo; B, el catálogo y, si hace falta, la pregunta.
> CE4 · Filtrar por persona y filtro fijo: «Proveedores de mi zona», «Como si lo recibiera», la comprobación de publicar y el envío a alguien sin zona. Dos opciones de dónde sale la zona: A, un dato propio de la persona, cargado en Usuarios; B, los roles.
> CE5 · Las respuestas: la planilla, el detalle, el Excel y el PDF con los campos de la opción elegida, sellados.
> CE6 · El circuito de punta a punta.
>
> CE3-B toma la sección «En WhatsApp» del catálogo tal como está dibujada en CE3-A. «Datos de las personas» es del módulo de formularios: la pantalla y la sección «Datos» de «Editar usuario» las aporta el módulo por un punto de extensión, y sin el módulo no aparecen.
>
> Ajuste de la evaluación de impacto, 2026-10-21 (la elección no cambia): una sola fuente de valores, «elegir de un catálogo» como «Zonas», para los campos y para los datos de las personas; «Datos de las personas» muestra la tabla de personas con sus valores, con ruta propia, y la lista de usuarios no suma columnas; la comprobación de publicar separa lo que frena de los avisos, que no frenan; un campo nuevo nace opcional y se carga después de guardarlo, y el menú «Campos» muestra u oculta columnas; en las respuestas, las subcolumnas solo se muestran; «en uso» mira el borrador y la versión activa. CE6 arranca creando «Zonas».
>
> Ajuste de claridad, 2026-10-21: en CE1, CE2 y CE3-A, la vista previa del catálogo muestra por defecto el campo cerrado y, arriba, «Una opción / Varias», como en CA1-A; CE3-A suma la lista, como al tocarlo.
>
> Para recorrerlo tocando, el prototipo navegable tiene la sección «Campos extra»: https://claude.ai/artifact/2noVifPc2MX33UnNvT2jhY.
>
> Reglas en ArquitecturaBase/docs/plans/2026-10-21-catalogos-con-origenes-externos.md («Campos extra del catálogo»).

#### CE1 · Definir los campos de un catálogo

Archivo `FWA-CE1-Campos.dc.html` («Campos · CE1 · definir los campos»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 106. Ajustes de las versiones 108 y 110.
- **Programado:** Sí: E2, el 2026-10-23.
- **Qué muestra:**
  - **Arriba:** el editor de «Proveedores», con «Activo · Cambios sin guardar», «Textos en», «El catálogo» y la vista previa.
  - **La sección «Campos»:**
    - «Agregar campo» y la línea de los campos fijos;
    - la tabla con #, Campo, Clave, Tipo, Reglas, Papel y Acciones: CUIT, Zona (de Zonas · 72), Rubro (de Rubros · 12) y «Plazo de pago (días)», que dice «Nuevo, sin guardar»;
    - «Plazo de pago» abierto, con Nombre, Nombre en inglés, Clave, Tipo, Reglas y Papel.
  - **Debajo:** «Opciones», con la nota de la columna apagada.
  - **Abajo:** quitar un campo con datos, uno en uso, cambiar el tipo, elegir de un catálogo, pasarlo a obligatorio, el inglés con los permisos y el celular.
- **Título y bajada de las notas:** «CE1 · Definir los campos de un catálogo». Columnas propias de cada catálogo, además del código, el texto y el detalle. Por ejemplo, en Proveedores: CUIT, Zona, Rubro y Plazo de pago. Nada queda fijo en el código.
- **Notas numeradas:**
  1. **Campos, entre el catálogo y las opciones.** Una sección nueva del editor elegido (CA1-A). Solo en los catálogos escritos a mano; los de la base traen sus columnas.
  2. **Agregar, ordenar y quitar.** «Agregar campo» suma uno al final y lo abre: nace opcional y no se carga hasta guardarlo. Las flechas ordenan las columnas de la tabla, la plantilla y el Excel; el tacho lo quita (abajo).
  3. **Tipo y reglas.** Texto, entero, decimal, fecha, sí o no, o elegir de un catálogo: los valores salen de otro catálogo, como «Zonas». Reglas: obligatorio, único y mínimo y máximo; en un texto, el largo.
  4. **Papel: filtro o dato.** Filtro sirve para mostrarle a cada persona lo suyo o para un filtro fijo de la pregunta (CE4). Dato se guarda con la opción y viaja con la respuesta (CE5).
  5. **La clave.** La usan la plantilla, la importación y lo guardado: no cambia después de guardar. Los campos se guardan con el catálogo, con su revisión y el 409.
- **En las notas, «Ajuste de la evaluación de impacto, 2026-10-21»:** «Lista de valores» pasa a ser «elegir de un catálogo»: Zona y Rubro toman sus valores de «Zonas» y «Rubros», sin un editor propio. Un campo nuevo nace opcional, y pasarlo a obligatorio exige valor en todas las opciones activas. «En uso» mira el borrador y la versión activa de cada formulario.
- **Abajo, sección por sección:**
  - **Quitar, cambiar el tipo, elegir de un catálogo, el obligatorio, el celular y el inglés.** Los campos se definen una vez y valen para la tabla, la plantilla y el Excel.
    - «Quitar un campo con datos»: Pide confirmarlo y dice cuántos valores se van. Lo ya respondido no cambia.
    - «Un campo en uso no se quita»: Si filtra una pregunta o arma lo que se ve en WhatsApp, en el borrador o en la versión activa de un formulario, primero se sacan esos usos.
    - «Cambiar el tipo de un campo cargado»: Se revisan los valores: los que no se convierten se listan por fila y no dejan guardar.
    - «Elegir de un catálogo»: Los valores salen de otro catálogo «Escritas a mano», como «Zonas»: no hay un editor de valores propio.
    - «Pasarlo a obligatorio»: Exige que todas las opciones activas tengan valor; si no, dice cuántas faltan.
    - «En inglés y quién lo ve»: «Fields», «Add field», «Key», «Rules», «Required», «Unique», «Role: Filter · Data» y «Remove the field and its 1,200 values». Definir, ordenar y quitar campos pide «Administrar catálogos». Con «Ver catálogos», los campos se ven sin editar.
    - «En el celular, a 390 px»: Los campos, uno debajo del otro; cada uno se edita entero.
- **Reglas del 2026-10-23:**
  - **«Cambios sin guardar»:** el encabezado dice «Activo · Cambios sin guardar». Se saca. La fila dice «Nuevo, sin guardar»: a revisar.
  - **Selector de idioma:** «Textos en», con «Completá el nombre y la descripción en español e inglés. En las opciones, el inglés es opcional.» No va: hay un solo campo por texto, en el idioma del menú.
  - **«Nombre en inglés»:** el editor del campo tiene Nombre y «Nombre en inglés». Va un solo Nombre, en el idioma del menú.
  - **«El catálogo»:** muestra «De dónde salen · Escritas a mano · No se cambia: para otro origen se arma otro catálogo.» como un campo más. Va así:
    - arriba, un dato de solo lectura, «Escritas a mano · Texto»;
    - debajo, solo Nombre y Descripción, que es opcional.
  - **Falta el ajuste del review de E2, del 2026-10-23:** no está dibujado. Está en el diseño del front, en [«Ajuste del review de E2»](../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md#ajuste-del-review-de-e2-2026-10-23).
    - quitar un campo que usa la presentación del propio catálogo;
    - «Cambiar el tipo», con las filas que devuelve el servidor y «Ver los N en la tabla»;
    - la sección «Campos» sin la lista de códigos.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CE1-Campos.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce1-definir-los-campos.png).
  - **Prototipo navegable:** Sí: «Campos extra», recorrido 1.
  - **Maqueta:** Sí, como propuesta «Propuesta · CE1», en un catálogo escrito a mano: Proveedores ya tiene CUIT, Zona, Rubro y Plazo de pago. La maqueta usa el front del 22, sin E2.

#### CE2 · Cargar los valores

Archivo `FWA-CE2-Valores.dc.html` («Campos · CE2 · cargar los valores»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 106. Ajustes de las versiones 108 y 110.
- **Programado:** Sí: E2, el 2026-10-23.
- **Qué muestra:**
  - **Arriba:** el editor de «Proveedores». La sección Campos está en reposo, con «Editar campos» y el resumen.
  - **La tabla de opciones:**
    - una columna por campo: CUIT, Zona, Rubro y Plazo de pago;
    - el menú «Campos»;
    - la nota de que la tabla se desplaza de costado;
    - las celdas con error.
  - **Abajo:** la plantilla, la revisión por tipo, elegir de un catálogo en la celda, los otros tipos, el menú Campos, un campo nuevo antes de guardar, el inglés con los permisos y el celular.
- **Título y bajada de las notas:** «CE2 · Cargar los valores». La tabla elegida (CA1-A) suma una columna por campo, y pegar e importar validan cada celda según su tipo.
- **Notas numeradas:**
  1. **Una columna por campo.** Después del código, en el orden de los campos. Cada celda se edita según su tipo: un texto, un número, una fecha, una casilla o un desplegable.
  2. **Elegir de un catálogo.** La celda es un desplegable con las opciones del catálogo fuente («Zonas»): no se escribe otra. Al pegar o importar, va el código o el texto de la opción.
  3. **Errores por celda.** La celda queda marcada y la fila dice qué pasa: «Fila 3 · Plazo de pago: no es un número entero». No deja guardar.
  4. **Muchas columnas.** La tabla se desplaza de costado dentro de su caja, con la barra a la vista. El menú «Campos» muestra u oculta columnas.
  5. **Pegar e importar.** La plantilla suma las columnas de los campos, reconocidas por su clave; la revisión valida cada celda por tipo, con fila y columna.
- **En las notas, «Ajuste de la evaluación de impacto, 2026-10-21»:** Zona y Rubro eligen de «Zonas» y «Rubros». Un campo nuevo no se carga hasta guardarlo: su columna aparece apagada, con «Guardá para cargar valores». El menú «Campos» muestra u oculta columnas.
- **Abajo, sección por sección:**
  - **La plantilla, la revisión, las celdas por tipo, el campo nuevo, el celular y el inglés.** Los campos se cargan como el resto de la opción: en la tabla, pegando o importando.
    - «La plantilla»: Una columna por campo, reconocida por su clave, con su tipo y sus reglas en la primera fila, y una fila de ejemplo.
    - «Importar o pegar: la revisión por tipo»: El mismo recorrido de siempre (CA2): cada celda se valida según su campo.
    - «Elegir de un catálogo, en la celda»: Las opciones de «Zonas»; con muchas, se busca escribiendo.
    - «Los otros tipos»: Cada tipo con su celda.
    - «El menú «Campos»»: Muestra u oculta columnas en la tabla; acá, Rubro está oculto.
    - «Un campo nuevo, antes de guardar»: Su columna aparece apagada, con «Guardá para cargar valores», hasta guardarlo.
    - «En inglés y quién lo ve»: Las columnas se llaman con el nombre en inglés de cada campo, si lo tiene. Cargar valores, pegar e importar piden «Administrar catálogos»; exportar y bajar la plantilla, «Ver catálogos».
    - «En el celular, a 390 px»: Sin tabla: la opción se edita entera, con sus campos.
- **Reglas del 2026-10-23:**
  - **«Cambios sin guardar»:** el encabezado dice «Activo · Cambios sin guardar». Se saca.
  - **Selector de idioma:** «Textos en», con «Completá el nombre y la descripción en español e inglés. En las opciones, el inglés es opcional.» No va: hay un solo campo por texto, en el idioma del menú.
  - **«El catálogo»:** muestra «De dónde salen · Escritas a mano · No se cambia: para otro origen se arma otro catálogo.» como un campo más. Va así:
    - arriba, un dato de solo lectura, «Escritas a mano · Texto»;
    - debajo, solo Nombre y Descripción, que es opcional.
  - **Textos junto a botones o columnas apagadas, a revisar:**
    - «Guardá para cargar valores», en la columna apagada de un campo nuevo;
    - «La tabla se desplaza de costado: hay más columnas a la derecha.», al lado del botón «Campos».
  - **Falta el ajuste del review de E2, del 2026-10-23:** no está dibujado. Está en el diseño del front, en [«Ajuste del review de E2»](../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md#ajuste-del-review-de-e2-2026-10-23). Incluye las celdas marcadas con su motivo y el valor que el control del tipo nuevo no admite.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CE2-Valores.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce2-cargar-los-valores.png).
  - **Prototipo navegable:** Sí: «Campos extra», recorrido 2.
  - **Maqueta:** Sí, como propuesta: la tabla de opciones, el editor de una opción y la planilla para pegar.

#### CE3-A · Qué viaja a WhatsApp: solo en el catálogo

Archivo `FWA-CE3-A-Catalogo.dc.html` («Campos · CE3 · A · solo en el catálogo»).

- **Estado:** Alternativa no elegida. Sigue en el lienzo porque CE3-B toma de acá la sección «En WhatsApp» del catálogo.
- **Fecha y versión del lienzo:** 2026-10-21, versión 106. Ajuste de claridad, versión 110.
- **Programado:** Solo la sección del catálogo, dentro de CE3-B.
- **Qué muestra:**
  - **El editor de «Proveedores»:** la sección «En WhatsApp», junto a la vista previa. El título, hasta 30, y la descripción, hasta 300, se arman con campos separados por « · ». Debajo, «Por ejemplo» y lo que no sale del sistema.
  - **Abajo:** el título que no entra, la comprobación, agregar un campo, lo de hoy, el inglés, los permisos y el celular.
- **Título y bajada de las notas:** «CE3 · A · Qué viaja a WhatsApp: solo en el catálogo». Variante A. El catálogo elige qué campos arman el título y la descripción de cada opción en WhatsApp, para todos los formularios que lo usan.
- **Notas numeradas:**
  1. **En WhatsApp, junto al desplegable.** Una sección del editor del catálogo. Lo de hoy: el título es el texto y la descripción, el detalle.
  2. **El título, hasta 30.** Uno o más campos, separados por « · ». Si alguna opción no entra, lo avisa con las opciones afectadas, y la comprobación de publicar lo frena.
  3. **La descripción, hasta 300.** Por ejemplo, el CUIT y la zona: «30-71234567-8 · Centro».
  4. **Solo viaja lo elegido.** Los demás campos no salen del sistema. El filtrado por persona se hace en el servidor, antes de mandar.
  5. **La vista previa.** Al costado, el campo cerrado y, al tocarlo, la lista, que se actualiza al cambiarlo. El cambio vale para los envíos nuevos.
- **Para comparar:**
  - **A favor:** un solo lugar: todos los formularios muestran las opciones igual, y alcanza con mirar el catálogo.
  - **En contra:** si un formulario necesita mostrar otra cosa, hay que cambiarlo para todos o hacer otro catálogo.
- **Abajo, sección por sección:**
  - **El título que no entra, la comprobación, el menú, lo de hoy, el celular y el inglés.** Lo elige el catálogo, una vez, para todos los formularios.
    - «El título no entra»: Avisa con las opciones afectadas; se puede guardar, pero no publicar un formulario que lo use.
    - «La comprobación de publicar»: Frena el formulario y dice qué hacer.
    - «Agregar un campo»: Cualquier campo fijo o extra.
    - «Lo de hoy»: Sin campos extra, todo sigue igual: el texto y el detalle.
    - «En inglés»: «Title», «Description», «up to 30 characters» y «Not sent outside the system».
    - «Quién lo cambia»: Elegir qué viaja a WhatsApp pide «Administrar catálogos». Los formularios que usan el catálogo lo toman en el próximo envío, sin publicar de nuevo.
    - «En el celular, a 390 px»: La misma sección, con la vista previa más abajo.
- **Reglas del 2026-10-23:**
  - **La parte que vale (la sección del catálogo):** trae lo mismo que CE1: «Textos en», «El catálogo» con «De dónde salen» y un caso con «Activo · Cambios sin guardar».
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CE3-A-Catalogo.dc.html`.
  - **Evidencia en el front:** [La sección del catálogo, que vale para CE3-B](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce3-seccion-del-catalogo.png).
  - **Prototipo navegable:** La sección del catálogo, en «Campos extra», recorrido 3.
  - **Maqueta:** La sección del catálogo, dentro de CE3-B.

#### CE3-B · Qué viaja a WhatsApp: el catálogo, y la pregunta puede cambiarlo

Archivo `FWA-CE3-B-Pregunta.dc.html` («Campos · CE3 · B · también en la pregunta»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 106.
- **Programado:** Sí: E2, el 2026-10-23.
- **Qué muestra:**
  - **El editor de «Pedir repuesto», publicado en la versión 3, con la pregunta «Proveedores a consultar» abierta:** el catálogo Proveedores, con 1.200 opciones y 4 campos extra, y elegir varias, de 1 a 3.
  - **La sección «En WhatsApp»,** con dos opciones:
    - «Como en el catálogo»: Texto · CUIT · Zona;
    - «Propio de esta pregunta»: Título con el Texto, y Descripción con Rubro y Plazo de pago.
    Debajo, «Por ejemplo» y lo que no sale del sistema. Al costado, la vista previa.
  - **Abajo:** lo del catálogo, el título propio que no entra, la comprobación (dice si el problema es del catálogo o de la pregunta), el inglés, los permisos y el celular.
- **Título y bajada de las notas:** «CE3 · B · Qué viaja a WhatsApp: el catálogo, y la pregunta puede cambiarlo». Variante B. El catálogo pone lo de siempre, como en la variante A, y una pregunta del formulario puede elegir otros campos solo para ella.
- **Notas numeradas:**
  1. **Como en el catálogo.** Lo de siempre: la pregunta toma el título y la descripción del catálogo, y cambia si cambia el catálogo.
  2. **Propio de esta pregunta.** Elige otros campos solo para este formulario. Por ejemplo, el rubro y el plazo de pago para quien pide un repuesto.
  3. **Las mismas reglas.** Título hasta 30 y descripción hasta 300. Si alguna opción no entra, avisa, y la comprobación de publicar lo frena.
  4. **La vista previa.** Muestra cómo se ve en esta pregunta.
  5. **En WhatsApp, dentro de la pregunta.** Solo viaja lo elegido para este formulario; los demás campos no salen del sistema.
- **Para comparar:**
  - **A favor:** cada formulario muestra lo que necesita, sin hacer otro catálogo.
  - **En contra:** hay dos lugares para mirar, y si el catálogo cambia lo que muestra, no llega a las preguntas que tienen lo propio.
- **Abajo, sección por sección:**
  - **Lo del catálogo, el título que no entra, la comprobación, el celular y el inglés.** El catálogo pone lo de siempre; la pregunta, si hace falta, lo propio.
    - «Lo del catálogo»: La sección «En WhatsApp» del catálogo, igual que en la variante A.
    - «El título propio que no entra»: Las mismas reglas, sobre lo que eligió la pregunta.
    - «Agregar un campo»: Los mismos campos que en el catálogo.
    - «La comprobación de publicar»: Dice si el problema es del catálogo o de la pregunta.
    - «En inglés»: «As in the catalog» y «Own for this question».
    - «Quién lo cambia»: En el catálogo, «Administrar catálogos». En la pregunta, «Administrar formularios»; se publica como cualquier cambio de la pregunta.
    - «En el celular, a 390 px»: La pregunta se edita entera, con su sección «En WhatsApp».
- **Reglas del 2026-10-23:**
  - Lo propio no tiene choques visibles. La sección del catálogo que toma de CE3-A sí los tiene.
  - **Falta el ajuste del review de E2, del 2026-10-23:** no está dibujado. Está en el diseño del front, en [«Ajuste del review de E2»](../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md#ajuste-del-review-de-e2-2026-10-23). Al quitar un campo que usa «En WhatsApp» del catálogo, sale del título y de la descripción del borrador, y está «Restaurar el campo».
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CE3-B-Pregunta.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce3-b-tambien-en-la-pregunta.png) · [La sección del catálogo, tomada de CE3-A](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce3-seccion-del-catalogo.png).
  - **Prototipo navegable:** Sí: «Campos extra», recorrido 3.
  - **Maqueta:** Sí, como propuesta: en Catálogos, junto a la vista previa, y en una pregunta de catálogo.

#### CE4-A · Filtrar por persona: datos de la persona

Archivo `FWA-CE4-A-Datos.dc.html` («Campos · CE4 · A · datos de la persona»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 106. Nota del módulo en la 107 y ajuste de la 108.
- **Programado:** Sí: E2, el 2026-10-23.
- **Qué muestra:**
  - **La pregunta «Proveedores a consultar» de «Pedir repuesto», con «Qué opciones ve cada persona»:**
    - «Zona es la zona de quien lo recibe»;
    - agregar un filtro según quien lo recibe, o uno fijo;
    - «Como si lo recibiera: Juan Gómez · Zona Centro → 14 opciones»;
    - «Como mucho 18 por persona».
  - **La vista previa:** los proveedores de Juan, con su CUIT.
  - **Abajo:**
    - «Datos de las personas» en Administración: los datos Zona y Sucursal, y una tabla de 6 personas, 2 sin zona;
    - Juan en «Editar usuario», con la sección «Datos»;
    - la comprobación, con lo que frena y los avisos;
    - alguien sin zona y un filtro fijo;
    - los permisos, el inglés y el celular.
- **Título y bajada de las notas:** «CE4 · A · Filtrar por persona: datos de la persona». Opción A. La zona es un dato propio de cada persona: se define una vez en Administración y se carga en Usuarios. La pregunta muestra a cada uno solo los proveedores de su zona.
- **Notas numeradas:**
  1. **Qué opciones ve cada persona.** Los filtros de la pregunta, sobre los campos de papel «filtro» del catálogo.
  2. **Según quien lo recibe.** Zona del proveedor es la zona de quien lo recibe: un dato de la persona (abajo). Los dos eligen de «Zonas», así que se compara la misma opción.
  3. **Filtro fijo.** No depende de la persona. Por ejemplo, «Rubro es Frío»: todos ven los mismos.
  4. **Como si lo recibiera.** Se elige una persona para probar: Juan Gómez, de zona Centro, ve 14 de 1.200.
  5. **Lo que frena y lo que avisa.** La comprobación frena lo que no puede funcionar: un campo inactivo, un papel o tipo que no corresponde, un título de más de 30. El máximo por persona y quién queda sin el dato son avisos que no frenan.
- **En las notas, «Ajuste de la evaluación de impacto, 2026-10-21»:** La zona de la persona y la del proveedor eligen de «Zonas». «Datos de las personas» muestra la tabla de personas con sus valores y tiene ruta propia (/datos-de-personas); la lista de usuarios no suma columnas. «Datos» se guarda aparte en «Editar usuario». La comprobación separa lo que frena de los avisos, que no frenan.
- **Para comparar:**
  - **A favor:** el dato es de la persona, claro y reutilizable en otros catálogos, y no mezcla permisos con datos.
  - **En contra:** hay que definir los datos y cargárselos a cada persona: una pantalla nueva en Administración.
- **Abajo, sección por sección:**
  - **De dónde sale la zona, la comprobación, sin zona, un filtro fijo, el celular y el inglés.** Lo que cambia entre las dos opciones es de dónde sale la zona de la persona; la pregunta se configura igual.
    - «Datos de las personas, en Administración»: Una pantalla nueva del módulo de formularios, con ruta propia (/datos-de-personas): arriba, los datos que tiene cada persona, que eligen de un catálogo como «Zonas»; abajo, la tabla de personas con sus valores. Sin el módulo, no aparece.
    - «Juan Gómez, en «Editar usuario»»: La sección «Datos» la suma el módulo de formularios por un punto de extensión: el núcleo de usuarios no sabe de formularios. Se guarda aparte del usuario, solo si cambió; si falla, el diálogo queda abierto con el error en la sección. La lista de usuarios no suma columnas.
    - «La comprobación de publicar»: Los avisos no frenan: el máximo por persona y quién queda sin el dato, en una lista aparte.
    - «Lo que frena»: Lo que no puede funcionar: un campo inexistente o inactivo, un papel o tipo que no corresponde, un título de más de 30.
    - «Al mandarle a alguien sin zona»: El envío se bloquea con el motivo; nunca se manda la lista entera.
    - «Un filtro fijo»: No depende de la persona: todos ven las mismas.
    - «Quién lo configura»: Cargar los datos de una persona en «Editar usuario» pide «Administrar usuarios»; definirlos, un permiso propio del módulo de formularios. Los filtros de la pregunta, «Administrar formularios».
    - «En inglés»: «Which options each person sees», «By who receives it», «Fixed filter» y «As if received by».
    - «En el celular, a 390 px»: La pregunta se edita entera, con sus filtros.
- **Reglas del 2026-10-23:**
  - Sin choques visibles.
  - **Falta el diálogo para definir un dato de las personas:** no está dibujado. Con la regla, tiene un solo campo de nombre. El punto 35 del review nombra `PersonAttributeDialog`.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CE4-A-Datos.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce4-a-datos-de-la-persona.png).
  - **Prototipo navegable:** Sí: «Campos extra», recorrido 4.
  - **Maqueta:** Sí, como propuesta:
    - Administración → «Datos de las personas»;
    - «Editar usuario» → «Datos»;
    - la pregunta;
    - la comprobación de publicar;
    - el chat.
    Juan, de zona Centro, recibe 10 proveedores. Pablo y Marta no tienen zona.

#### CE4-B · Filtrar por persona: los roles

Archivo `FWA-CE4-B-Roles.dc.html` («Campos · CE4 · B · roles»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-21, versión 106.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** la zona sale de los roles, un rol por zona, como los catálogos de Filtros de hoy. La pregunta se configura igual que en A.
  - **Abajo:** los mismos casos que A, con los roles en lugar de «Datos de las personas».
- **Título y bajada de las notas:** «CE4 · B · Filtrar por persona: los roles». Opción B. La zona sale de los roles, como hacen hoy los catálogos de Filtros: un rol por zona. La pregunta muestra a cada uno solo los proveedores de la zona de sus roles.
- **Notas numeradas:**
  1. **Qué opciones ve cada persona.** Los filtros de la pregunta, sobre los campos de papel «filtro» del catálogo.
  2. **Según quien lo recibe.** Zona del proveedor es la zona de los roles de quien lo recibe: cada rol de zona dice la suya (abajo).
  3. **Filtro fijo.** No depende de la persona. Por ejemplo, «Rubro es Frío»: todos ven los mismos.
  4. **Como si lo recibiera.** Se elige una persona para probar: Juan Gómez, de zona Centro, ve 14 de 1.200.
  5. **Lo que frena y lo que avisa.** La comprobación frena lo que no puede funcionar: un campo inactivo, un papel o tipo que no corresponde, un título de más de 30. El máximo por persona y quién queda sin el dato son avisos que no frenan.
- **En las notas, «Ajuste de la evaluación de impacto, 2026-10-21»:** La comprobación separa lo que frena de los avisos, que no frenan.
- **Para comparar:**
  - **A favor:** usa lo que ya existe: los roles y su asignación.
  - **En contra:** un rol por zona mezcla permisos con datos: con 72 zonas, 72 roles, y quien tiene dos roles de zona ve las dos.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-CE4-B-Roles.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### CE5 · Las respuestas

Archivo `FWA-CE5-Respuestas.dc.html` («Campos · CE5 · las respuestas»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 106. Ajuste de la 108.
- **Programado:** Sí: E2, el 2026-10-23.
- **Qué muestra:**
  - **La planilla de «Pedir repuesto»,** versión publicada 4, con «Columnas · 2 ocultas». Las subcolumnas «Proveedores a consultar · CUIT» y «· Plazo de pago (días)» muestran «—» en una respuesta de antes de los campos.
  - **Abajo:** el detalle D2, el PDF, lo sellado, la tarjeta del chat, el Excel, Columnas, el inglés, los permisos y el celular.
- **Título y bajada de las notas:** «CE5 · Las respuestas». Los campos extra de la opción elegida se guardan con la respuesta, sellados, y se ven en la planilla, el detalle, el Excel y el PDF.
- **Notas numeradas:**
  1. **Una columna por campo de dato.** Después de la pregunta, «Proveedores a consultar · CUIT» y la del plazo de pago: los campos de papel «dato», con varias opciones en el mismo orden. Solo se muestran: no se ordenan, ni filtran, ni cuentan en las facetas. Acá, la planilla desplazada hasta el final.
  2. **Los de filtro, en «Columnas».** Zona y Rubro están ocultos; se prenden en «Columnas» (abajo). El Excel trae todos; con «Solo las columnas visibles», los que se ven.
  3. **Sellados al responder.** Si después cambia el CUIT en el catálogo, la respuesta conserva el de ese momento. Una respuesta de antes de los campos muestra «—».
- **En las notas, «Ajuste de la evaluación de impacto, 2026-10-21»:** Las subcolumnas solo se muestran: no se ordenan, ni filtran, ni cuentan en las facetas. El detalle muestra todos los campos. El Excel con «Solo las columnas visibles» sigue a las columnas.
- **Abajo, sección por sección:**
  - **El detalle, el PDF, lo sellado, el chat, el Excel, las columnas, el celular y el inglés.** Los campos de la opción elegida, tal como estaban al responder.
    - «El detalle D2»: Cada opción elegida con todos sus campos, sellados.
    - «El PDF»: Los campos de dato, junto a la opción.
    - «Sellados»: Editar el catálogo no cambia lo respondido.
    - «La tarjeta en el chat»: Como hoy: las opciones elegidas. Los campos se ven en el detalle.
    - «El Excel»: Las columnas de la pregunta: una por campo, todos, con «; » entre las opciones; «—» es una respuesta de antes de los campos. Con «Solo las columnas visibles», sigue a las columnas de la planilla.
    - «Los de filtro, en «Columnas»»: Zona y Rubro, ocultos: se prenden acá.
    - «En inglés»: Las columnas con el nombre en inglés de la pregunta y del campo.
    - «Quién lo ve»: Como hoy: la planilla y el detalle piden «Ver respuestas»; el Excel, «Exportar respuestas».
    - «En el celular, a 390 px»: La planilla se desplaza de costado dentro de su caja; acá, hasta los campos.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CE5-Respuestas.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce5-las-respuestas.png).
  - **Prototipo navegable:** Sí: «Campos extra», recorrido 6.
  - **Maqueta:** Sí, como propuesta: la planilla con subcolumnas y «Columnas», el detalle, el Excel y el PDF de «Pedir repuesto».

#### CE6 · El circuito de punta a punta

Archivo `FWA-CE6-Circuito.dc.html` («Campos · CE6 · el circuito de punta a punta»).

- **Estado:** Elegido el 2026-10-21.
- **Fecha y versión del lienzo:** 2026-10-21, versión 106. Ajuste de la 108: arranca creando «Zonas».
- **Programado:** Es un circuito. Sus pantallas están programadas, en E2.
- **Qué muestra:**
  - **Tablero de casos:** el circuito en ocho pasos numerados.
  - **Abajo:** tres notas al pie.
- **Encabezado del tablero:** «CE6 · El circuito de punta a punta». De crear «Zonas» a ver el CUIT del proveedor elegido en la planilla, pasando por el filtro por zona. Ajuste de la evaluación de impacto, 2026-10-21: arranca creando «Zonas», y la zona de Juan se carga en «Editar usuario».
- **Casos numerados (son las notas numeradas de este tablero):**
  1. **Crear «Zonas».** Un catálogo escrito a mano más, con las 72 zonas.
  2. **Definir los campos.** En Proveedores: CUIT, Zona (de «Zonas») y Rubro.
  3. **Pegar 1.200 proveedores.** Con las columnas de los campos.
  4. **Elegir qué viaja.** El título es el texto; la descripción, el CUIT.
  5. **Cargarle a Juan su zona.** En «Editar usuario», sección «Datos».
  6. **Filtrar por zona.** En «Pedir repuesto», cada uno ve los de su zona.
  7. **Juan ve sus 14.** En el celular, con el CUIT debajo.
  8. **La respuesta, con el CUIT.** En la planilla, sellado.
- **Notas al pie:**
  - **Nada fijo en el código.** Los campos, sus tipos y su papel son configuración de cada catálogo; las listas de valores, catálogos como «Zonas».
  - **Solo viaja lo elegido.** A WhatsApp van el título y la descripción; el filtrado por persona se hace antes de mandar.
  - **Sellado al responder.** La respuesta guarda los campos de la opción elegida; editar el catálogo no la cambia.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-CE6-Circuito.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce6-el-circuito.png).
  - **Prototipo navegable:** Sí: los seis recorridos de «Campos extra».
  - **Maqueta:** Sí: «Recorridos» → CE6, en `/propuestas/ce6`, con 1.200 proveedores para pegar.

## Documentos por WhatsApp (DOC1–DOC7)

Dibujados el 2026-10-22, en la versión 116 del lienzo. Se eligieron DOC5-A y DOC7-A, y valen DOC1–DOC4 y DOC6, que tenían un solo concepto. Las versiones siguientes:

- la 117 los marca y suma las correcciones al elegir;

- la 118 agranda DOC5-A para que entren sus notas;

- la 119 deja DOC2 y DOC4 como quedaron en el plan.

DOC5-B y DOC7-B se retiraron el 2026-10-22, en la versión 119.

**Por programar:** el [plan](../2026-10-22-documentos-por-whatsapp.md) dice «sin empezar». Las reglas de lectura están en el [ADR 0022](../../decisions/0022-lectura-de-documentos-con-ia.md).

**Multitenant:** la nota 2 de DOC1 dice que la biblioteca es la misma para todas las organizaciones.

**Diseño congelado en el front:** [documentos.md](../../../../ArquitecturaBaseFront/docs/design/documentos.md#los-tableros).

**Nota guía de la sección «Documentos por WhatsApp · biblioteca, lectura con IA, bandeja y tablero» en el lienzo** (textual):

> Documentos por WhatsApp (parte 1 de tres). DOC5-A y DOC7-A elegidos el 2026-10-22, con DOC1–DOC4 y DOC6; por programar. DOC5-B y DOC7-B, las alternativas que no se eligieron, se sacaron del lienzo el 2026-10-22 para hacer lugar: siguen en el repo lienzo-formularios, en retirados/. Dibujados el 2026-10-22. Las reglas están en ArquitecturaBase/docs/plans/2026-10-22-documentos-por-whatsapp.md: «Conceptos» y «Decisiones tomadas» no se reabren.
>
> DOC1 · La biblioteca: los formularios estándar del producto («Comprobante de transferencia» y los que vienen), «Agregar» y la sección «Lectura de documentos» del editor: «Cómo reconocerlo», qué pregunta recibe cada dato y las validaciones fijas.
> DOC2 · Los tres casos en el celular (seguro, duda y no lo reconoce) y los otros formatos: PDF, Word, Excel con varias filas, ubicación y contacto.
> DOC3 · El formulario prellenado: lo dudoso, marcado; lo no leído, vacío; el comprobante, adjunto; y qué pasa al corregir.
> DOC4 · Los casos difíciles: foto movida, PDF con contraseña, repetido, CBU mal leído, un formulario que no se le puede mandar, formatos viejos, macros y contraseñas, y un número sin cuenta vinculada, que queda en el chat sin leerse.
> DOC5 · La bandeja «Documentos recibidos», en dos conceptos: A, la planilla con panel; B, la cola de revisión. Los descartados y los sin clasificar dicen cuándo se borran: a los 90 días.
> DOC6 · «Armar formulario con la IA», desde los sin clasificar, con el aviso de los parecidos.
> DOC7 · La pestaña «Tablero» de cada formulario, en dos conceptos: A, tarjetas armadas solas; B, un informe por pregunta.
>
> Corregido al elegir: se lee solo lo de cuentas vinculadas y activas que pueden cargar respuestas; si el formulario reconocido no se le puede mandar a la persona, lo dice y deriva; y «para revisar acá» junta los propuestos que vencieron sin que la persona los confirmara y las filas de una planilla que pasaron el tope.
>
> Para recorrerlo tocando, la maqueta viva tiene lo elegido y, en «Recorridos», el circuito DOC: https://claude.ai/artifact/CM56W2Yc4VVzwMmxCKFpdP.
>
> Los comprobantes son de ejemplo: sin logos ni diseños de bancos, el banco solo como texto y la marca «Ejemplo».

#### DOC1 · La biblioteca

Archivo `FWA-DOC1-Biblioteca.dc.html` («Documentos · DOC1 · la biblioteca y la lectura de documentos»).

- **Estado:** Elegido el 2026-10-22.
- **Fecha y versión del lienzo:** 2026-10-22, versión 116. La 117 lo marca.
- **Programado:** No.
- **Qué muestra:**
  - **El panel de Gestión de formularios:** «Documentos recibidos 6» en el panel y «Biblioteca» en las herramientas.
  - **La página «Biblioteca · Formularios estándar del producto»:**
    - el buscador y el filtro «Todos (13) · Disponibles (1) · Próximamente (12)»;
    - la tabla Estándar · Qué lee · Versión · En tus formularios;
    - «Comprobante de transferencia», que ya está en «Pagos a proveedores», con Agregar;
    - doce «Próximamente»: Factura, Nota de crédito o débito, Recibo, Remito, Ticket de gasto, Orden de compra, Presupuesto, Cheque o eCheq, Constancias, Liquidaciones, Carta de porte y DTE.
  - **Abajo:**
    - la sección «Lectura de documentos» del editor, con sus nueve notas;
    - Agregar;
    - un formulario propio, sin estándar;
    - los estados, el celular, el inglés y los permisos.
- **Título y bajada de las notas:** «DOC1 · La biblioteca». Los formularios estándar vienen con el producto, como datos de inicio de cada versión. Una pantalla nueva en «Gestión de formularios», junto a Diseñar formularios.
- **Notas numeradas:**
  1. **«Documentos recibidos», en el panel.** La bandeja nueva (DOC5), debajo de Formularios, con cuántos esperan revisión.
  2. **«Biblioteca», en las herramientas.** Con «Diseñar formularios». En el multitenant, la misma biblioteca para todas las organizaciones.
  3. **Cada estándar dice qué trae.** Qué lee, sus validaciones fijas, su versión y en qué formularios tuyos está.
  4. **Agregar.** Crea un formulario propio, en borrador. Se puede agregar más de una vez: «Pagos a proveedores» es otra copia del mismo estándar.
  5. **Próximamente.** Los estándares de las etapas siguientes, a la vista y sin acción.
  6. **Filtrar.** Todos, disponibles o próximamente, y buscar por nombre. Vive en la dirección, como el resto de las listas.
- **Abajo, sección por sección:**
  - **DOC1 · «Lectura de documentos», en el editor.** La copia propia se edita como cualquier formulario. La sección nueva dice cómo se reconoce el documento, qué pregunta recibe cada dato y qué se valida sin IA.
    - nota 1, **De dónde viene:** Recuerda el estándar, su versión y qué pregunta corresponde a cada dato.
    - nota 2, **Cómo reconocerlo:** La descripción para la IA. La del estándar sirve para cualquiera; lo propio (de quién es la cuenta de destino) separa «Pagos de clientes» de «Pagos a proveedores».
    - nota 3, **Los ejemplos:** Cada corrección de la bandeja se suma como ejemplo. No se entrena ningún modelo.
    - nota 4, **Qué pregunta recibe cada dato:** Se elige de las preguntas del formulario. Cambiarle el nombre a una no corta la lectura; un dato sin pregunta no se lee.
    - nota 5, **Validaciones fijas:** Del estándar, con candado: no se apagan ni dependen de la IA. El banco sale de los primeros dígitos del CBU o del CVU, con la tabla del BCRA cargada como datos.
    - nota 6, **Cruce con catálogos:** Una pregunta propia, como Cliente, se completa con el CUIT de origen buscado en un catálogo.
    - nota 7, **Duplicados:** El mismo archivo, o la misma operación, monto y fecha.
    - nota 8, **Cómo viene leyendo:** La precisión por formulario y por dato: la base de la carga automática, que viene después.
    - nota 9, **Se publica como siempre:** Prender o cambiar la lectura es un cambio del formulario: se guarda en el borrador y sale al publicar.
  - **Agregar, un formulario propio, los estados, el celular, el inglés y los permisos.** La biblioteca no se edita: se agrega una copia, que es tuya. El mismo mecanismo sirve para un formulario armado a mano.
    - «Agregar»: Pide el nombre y el prefijo; lo demás viene del estándar. Abre el editor del borrador nuevo.
    - «En inglés»: «Library», «Add», «Coming soon», «Document reading», «How to recognize it», «Data from the standard», «Question that receives it» y «Fixed validations». Los estándares traen sus textos en los dos idiomas, como cualquier formulario.
    - «Quién lo ve»: La biblioteca y la sección «Lectura de documentos» piden «Diseñar formularios», el permiso del editor. Sin él, «Biblioteca» no aparece en el panel y por la dirección dice «No tenés permiso».
    - «En un formulario propio, sin estándar»: Mismo mecanismo: lo que cambia es que no hay datos del estándar ni candados.
    - «Cargando y sin poder cargar»: Las filas grises mientras carga; el aviso común, con reintento.
    - «La biblioteca, a 390 px»: Una tarjeta por estándar; «Agregar», del ancho de la pantalla.
    - «La lectura, a 390 px»: Un dato por tarjeta: qué pregunta lo recibe y su candado.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-DOC1-Biblioteca.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc1-la-biblioteca-y-la-lectura.png).
  - **Maqueta:** Sí, como propuesta. Gestión de formularios → «Biblioteca», en `/formularios/biblioteca`: «Agregar» crea una copia en borrador. En el editor, «Lectura de documentos».

#### DOC2 · Los tres casos en el celular, y los otros formatos

Archivo `FWA-DOC2-TresCasos.dc.html` («Documentos · DOC2 · los tres casos y los otros formatos»).

- **Estado:** Elegido el 2026-10-22.
- **Fecha y versión del lienzo:** 2026-10-22, versión 116. Correcciones de la 117 y la 119.
- **Programado:** No.
- **Qué muestra:**
  - **Tablero de casos:** ocho casos numerados, cada uno con el celular de Juan y, en los tres primeros, el chat del operador:
    - seguro, duda y no lo reconoce;
    - PDF, Word y Excel con varias filas;
    - una ubicación y un contacto.
  - **Abajo:** el chat del operador a 390 px, el inglés, quién ve qué, dónde se elige la pregunta de la ubicación y el contacto, y lo que no se lee.
  - **Notas al pie:** seis.
- **Encabezado del tablero:** «DOC2 · Los tres casos en el celular, y los otros formatos». Juan cobra en los comercios y manda los comprobantes al WhatsApp de la empresa. Según cuánto encaja el documento, el sistema propone, pregunta u ofrece la lista. También lee PDF, Word y Excel, y usa la ubicación y el contacto que se comparten. Los comprobantes son de ejemplo: el banco, solo como texto.
- **Casos numerados (son las notas numeradas de este tablero):**
  1. **Seguro.** Un formulario encaja y los datos pasan las validaciones: lo propone, prellenado y con el comprobante adjunto.
  2. **Duda.** Dos o tres formularios pueden ser: pregunta con botones y, con la elección, manda ese prellenado.
  3. **No lo reconoce.** Ningún formulario encaja: ofrece la lista de lo que esa persona puede completar, más «Otro».
  4. **Un PDF.** Se lee entero, todas sus páginas hasta el tope, y sigue como la foto.
  5. **Un Word.** Un aviso de pago en Word (.docx): se leen su texto y sus tablas.
  6. **Un Excel con varias filas.** Una planilla con 3 transferencias: propone una carga por fila, hasta 3.
  7. **Una ubicación.** Completa la dirección del formulario que la persona tiene abierto.
  8. **Un contacto.** Completa el nombre y el teléfono del formulario abierto.
- **Notas al pie:**
  - **La IA propone; la persona confirma.** En esta etapa nunca carga sola: lo que llega es una propuesta, que se revisa y se confirma en WhatsApp.
  - **Solo lo que puede completar.** Se lee lo de cuentas vinculadas y activas que pueden cargar respuestas, y se clasifica entre los formularios con lectura. Si el que encaja no se le puede mandar, lo dice y deriva (DOC4). Los botones, de hasta 20 caracteres: un nombre más largo va en la lista.
  - **El documento es el mensaje.** Funciona sin texto; si viene con texto, también se usa. Un PDF de varias páginas se lee entero, hasta el tope.
  - **Todo queda en la bandeja.** Cada documento queda en «Documentos recibidos» con su estado: propuesto, confirmado, sin clasificar o descartado (DOC5).
  - **Una planilla, varias cargas.** Cada fila del mismo tipo es una carga: hasta 3 por mensaje, el tope de acciones. Las demás quedan propuestas en la bandeja.
  - **Ubicación y contacto, sin IA.** Son datos de WhatsApp: completan las preguntas elegidas del formulario que la persona tiene abierto.
- **Abajo, sección por sección:**
  - «El chat del operador, a 390 px»: El documento es un mensaje más; el porqué, debajo de la respuesta, como en AS4.
  - «En inglés»: Los mensajes salen en el idioma de la persona, como los del asistente. Los nombres de los formularios, en su idioma.
  - «Quién ve qué»: En el chat, el documento es un mensaje: se ve con «Ver chats», como un audio. La IA no lo ve fuera de este circuito. Se leen solo los mensajes de cuentas vinculadas y activas que pueden cargar respuestas. Lo demás queda en el chat, sin leerse (DOC4). En la bandeja, con los permisos propios de DOC5. El formulario, cuando se confirma, es una respuesta más: su permiso de siempre. Lo de cada caso se guarda: qué reconoció, qué leyó, qué validó y qué eligió la persona. Es lo que muestra «Por qué lo propuso».
  - **Los otros formatos: PDF, Word, Excel, ubicación y contacto.** Además de la foto, se leen PDF, Word (.docx) y Excel (.xlsx), y se usan la ubicación y el contacto que comparte la persona. Siempre de cuentas vinculadas y activas que pueden cargar respuestas.
    - «Dónde se elige la pregunta»: En el editor del formulario, una vez. La ubicación y el contacto no pasan por la IA.
    - «Registrar entrega · Lectura de documentos · Ubicación y contacto»: Ofrece las preguntas de texto del formulario. Sin una elegida, la ubicación o el contacto quedan en el chat, como hoy.
    - «En inglés»: En el idioma de la persona, como los demás mensajes.
    - «Lo que no se lee»: **Video y stickers** no se leen: quedan en el chat, como hoy. **Formatos viejos** (.doc, .xls), **macros** y **contraseñas**: se pide un PDF o una versión sin contraseña (DOC4). **Un número sin cuenta vinculada:** lo que manda queda en el chat para un operador, sin leerse (DOC4).
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-DOC2-TresCasos.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc2-los-tres-casos-y-los-otros-formatos.png).
  - **Maqueta:** Sí, como propuesta en el celular simulado. «Adjuntar» manda comprobantes ficticios: fotos, un PDF, un Word, un Excel de 3 filas, la ubicación y un contacto. En el chat del operador están el documento y el porqué.

#### DOC3 · El formulario prellenado

Archivo `FWA-DOC3-Prellenado.dc.html` («Documentos · DOC3 · el formulario prellenado»).

- **Estado:** Elegido el 2026-10-22.
- **Fecha y versión del lienzo:** 2026-10-22, versión 116.
- **Programado:** No.
- **Qué muestra:**
  - **Tablero de casos:** el Flow prellenado en cuatro pantallas numeradas.
  - **Abajo:** al confirmar, en el chat del operador; en el detalle de la respuesta; y en inglés.
  - **Notas al pie:** cuatro.
- **Encabezado del tablero:** «DOC3 · El formulario prellenado». El formulario de siempre, por WhatsApp, completo con lo que se leyó del comprobante: lo dudoso, marcado; lo que no se leyó, vacío; el comprobante, ya adjunto en su pregunta. La persona revisa, corrige si hace falta y confirma.
- **Casos numerados (son las notas numeradas de este tablero):**
  1. **Lo que leyó, prellenado.** La primera pantalla: el cliente (por el CUIT, en Clientes), el monto, la fecha y la operación.
  2. **Origen y destino.** Bancos, cuentas, titulares y CUIT. El banco sale de la cuenta, no del texto.
  3. **El comprobante, adjunto.** En su pregunta de documento, el que mandó. Puede cambiarlo; no hace falta volver a subirlo.
  4. **Si corrige.** Lo corregido queda marcado y se guarda junto a lo propuesto. Un dato que no pasa una validación fija no deja seguir.
- **Notas al pie:**
  - **Lo dudoso, marcado.** «Revisá» en el campo, con el borde de atención. No se esconde ni se deja vacío: la persona decide.
  - **Lo que no se leyó, vacío.** «No se leyó» y el campo libre. Si la pregunta es obligatoria, no deja confirmar hasta completarlo.
  - **Las validaciones, de nuevo al confirmar.** CBU, CVU, CUIT y duplicados se vuelven a revisar con lo corregido. Lo que no pasa no se guarda y se dice por qué.
  - **Cada corrección queda registrada.** El dato propuesto, el confirmado y quién lo corrigió. Se suman como ejemplos y miden la precisión (DOC1).
- **Abajo, sección por sección:**
  - «Al confirmar, en el chat del operador»: La tarjeta de siempre (Etapa 1), con el comprobante como archivo de la respuesta (Etapa 2B).
  - «En el detalle de la respuesta»: Lo que leyó, lo que se corrigió y quién: es lo que alimenta los ejemplos y la precisión.
  - «En inglés»: «We filled it in with the receipt you sent. Check what's marked before confirming.» «Check this», «Not read», «Doesn't pass», «Corrected» y «Change document». Los datos leídos, como vienen en el documento: un comprobante en otro idioma u otra moneda se lee igual.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-DOC3-Prellenado.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc3-el-formulario-prellenado.png).
  - **Maqueta:** Sí, como propuesta: el Flow del celular, con lo dudoso marcado, lo no leído vacío y el comprobante adjunto.

#### DOC4 · Los casos difíciles

Archivo `FWA-DOC4-Dificiles.dc.html` («Documentos · DOC4 · los casos difíciles»).

- **Estado:** Elegido el 2026-10-22.
- **Fecha y versión del lienzo:** 2026-10-22, versión 116. Correcciones de la 117 y la 119: Pablo sin zona y Carlos Díaz sin cuenta.
- **Programado:** No.
- **Qué muestra:**
  - **Tablero de casos:** nueve casos numerados. Cada uno muestra lo que ve la persona o el operador.
  - **Notas al pie:** seis.
- **Encabezado del tablero:** «DOC4 · Los casos difíciles». Lo que pasa cuando el documento no se puede leer o abrir, ya se cargó, trae un dato que no pasa una validación fija, es de un formulario que no se le puede mandar a esa persona o llega de un número sin cuenta vinculada.
- **Casos numerados (son las notas numeradas de este tablero):**
  1. **Foto movida, oscura o cortada.** No se puede leer un dato que hace falta: pide reenviarla, diciendo qué falta.
  2. **PDF con contraseña.** No se puede abrir: pide uno sin contraseña o una captura.
  3. **Documento repetido.** El mismo archivo, o la misma operación, monto y fecha: avisa cuándo y en qué respuesta se cargó.
  4. **Un CBU mal leído.** La IA leyó un 3 donde había un 4: el dígito verificador no da, y la cuenta llega marcada para corregir.
  5. **Un formulario que no se le puede mandar.** Pablo manda un pago a un proveedor, pero no tiene zona y «Pagos a proveedores» filtra los proveedores por la zona de quien lo recibe (CE4-A): no se le puede mandar.
  6. **Un formato viejo.** Un Word .doc o un Excel .xls no se lee: pide el archivo en PDF.
  7. **Un Excel con macros o con contraseña.** No se abren: el de macros, por seguridad; el de contraseña, porque no se puede leer.
  8. **Un número sin cuenta vinculada.** Carlos Díaz es un cliente sin cuenta: su comprobante no se lee y queda en el chat de Ana, para un operador.
  9. **En el chat del operador y en inglés.** La derivación, como en AS4: la marca y el motivo. Los mensajes, en el idioma de la persona.
- **Notas al pie:**
  - **Pide lo que falta, no adivina.** Si un dato que hace falta no se lee, lo dice y pide otra foto. Un dato que no hace falta queda vacío (DOC3).
  - **Un repetido no se carga dos veces.** El mismo archivo, o la misma operación, monto y fecha. Lo resuelve el sistema, sin IA.
  - **Lo que no pasa, no se propone como bueno.** Las validaciones fijas mandan sobre lo que leyó la IA: la cuenta llega marcada y no se puede confirmar así.
  - **Si no se le puede mandar, deriva.** Nunca manda un formulario que la persona no puede recibir, ni la lista entera de un catálogo filtrado: lo dice, marca la conversación y lo deja para el equipo.
  - **Nada se ejecuta.** Formatos viejos, macros y contraseñas no se abren ni van a la IA: se pide un PDF o una versión sin contraseña.
  - **Solo cuentas vinculadas.** Lo que manda un número sin cuenta vinculada y activa queda en el chat para un operador, sin leerse.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-DOC4-Dificiles.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc4-los-casos-dificiles.png).
  - **Maqueta:** Sí, como propuesta en el celular:
    - foto movida;
    - PDF con contraseña;
    - repetido;
    - CBU mal leído;
    - Word viejo;
    - Excel con macros o con contraseña;
    - Pablo, sin zona;
    - Carlos Díaz, sin cuenta.

#### DOC5-A · La planilla con panel

Archivo `FWA-DOC5-A-Planilla.dc.html` («Documentos · DOC5 · A · la planilla con panel»).

- **Estado:** Elegido el 2026-10-22.
- **Fecha y versión del lienzo:** 2026-10-22, versión 116. Marcado en la 117 y agrandado en la 118.
- **Programado:** No.
- **Qué muestra:**
  - **La página «Documentos recibidos»:**
    - el aviso de los 5 parecidos, con «Armar formulario con la IA» y «Ver los 5»;
    - la búsqueda por monto, CUIT u operación, y los filtros Formulario, Mandó y Recibido;
    - el segmentado por estado: Todos 27, Propuestos 3, Sin clasificar 5, Confirmados 15 y Descartados 4;
    - la tabla: Documento con su miniatura, Recibido, Mandó, Formulario, Datos leídos, Estado y Respuesta. Los descartados y los sin clasificar dicen «Se borra el 20/01».
  - **El panel a la derecha, con el comprobante de Pablo Sosa:**
    - «5 de 27», con ↑ ↓;
    - Revisar e Historial;
    - el visor, con acercar, girar, página y Descargar;
    - «Propuesto · Venció sin que Pablo lo confirmara…»;
    - el formulario reconocido, con su seguridad, y los datos;
    - Descartar y Pasar al formulario.
  - **Abajo:** buscar un monto sin el panel, el historial de un confirmado, el inglés, la bandeja vacía, sin coincidencias o con error, los permisos y el celular.
- **Título y bajada de las notas:** «DOC5 · A · La planilla con panel». Concepto A. Una planilla como la de respuestas, con los filtros de siempre; el documento se abre en un panel al costado, al lado de sus datos, como la vista rápida de una respuesta (D2).
- **Notas numeradas:**
  1. **En el panel de Gestión de formularios.** «Documentos recibidos», con cuántos esperan a alguien del equipo: los propuestos que vencieron sin que la persona los confirmara y los sin clasificar.
  2. **Los parecidos, agrupados.** Si llegan varios sin clasificar que se parecen, un aviso arriba propone armar un formulario con la IA (DOC6).
  3. **Cada documento, una fila.** Su miniatura, cuándo llegó, quién lo mandó, el formulario y los datos principales que se leyeron.
  4. **Datos leídos.** Los que más sirven para encontrarlo: monto, banco o CUIT.
  5. **Buscar por los datos.** Monto, CUIT o número de operación, también en lo que no se cargó.
  6. **Filtros de la planilla.** Formulario, quién lo mandó y cuándo llegó, con la misma gramática. Viven en la dirección, con la página y el documento abierto.
  7. **Por estado.** Propuesto, sin clasificar, confirmado y descartado, con cuántos hay. Los sin clasificar y los descartados se borran a los 90 días, con su lectura: la fila dice cuándo.
  8. **El panel.** El documento arriba (acercar, girar, páginas, descargar) y sus datos abajo. ↑ ↓ recorren la planilla.
  9. **El formulario.** El que reconoció la IA, con su seguridad. Se puede cambiar: los datos se vuelven a leer para ese formulario.
  10. **Descartar.** Pide confirmación y queda «Descartado», con quién y cuándo. Se puede volver a abrir.
  11. **Pasar al formulario.** Guarda la respuesta con lo revisado y deja registradas las correcciones. El documento pasa a «Confirmado», con el número.
- **Para comparar:**
  - **A favor:** es la planilla que ya se conoce: buscar, filtrar y ver muchos a la vez; el panel no saca de la lista.
  - **En contra:** el documento se ve chico al lado de la planilla; para revisar muchos seguidos, hay que ir y venir con las flechas.
- **Abajo, sección por sección:**
  - **Buscar, el historial, los estados, el celular, el inglés y los permisos.** La bandeja guarda todo lo que llegó: lo que se cargó, lo que espera y lo que se descartó.
    - «Sin el panel, buscando un monto»: La búsqueda encuentra el monto con o sin puntos; también el CUIT y el número de operación. Un descartado o un sin clasificar dice cuándo se borra: a los 90 días, con su lectura.
    - «El historial de un confirmado»: El del Nación, REC-000124: lo que hizo el sistema, la IA y cada persona. Cada corrección: el dato propuesto, el confirmado, quién y dónde.
    - «En inglés»: «Received documents», «Proposed», «Unclassified», «Confirmed», «Discarded», «Pass to the form», «Discard», «Read data», «History» y «5 similar documents arrived. Shall we build a form?». Los datos leídos, como vienen en el documento.
    - «Quién lo ve y quién lo revisa»: Dos permisos nuevos: **«Ver documentos recibidos»** y **«Revisar documentos»** (corregir, pasar al formulario y descartar). Sin el primero, la entrada no aparece y por la dirección dice «No tenés permiso». Con solo «Ver», el documento y sus datos se ven sin poder cambiarlos
    - «La planilla, a 390 px»: La tabla se desplaza de costado dentro de su caja, como todas.
    - «El documento, a 390 px»: Se abre en su pantalla: el documento, los datos y las acciones abajo, fijas.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-DOC5-A-Planilla.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc5-a-la-planilla-con-panel.png).
  - **Maqueta:** Sí, como propuesta. Gestión de formularios → «Documentos recibidos», en `/formularios/documentos`. Pide «Ver documentos recibidos».

#### DOC5-B · La cola de revisión

Archivo `FWA-DOC5-B-Cola.dc.html` («Documentos · DOC5 · B · la cola de revisión»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-22, versión 116.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** cuatro colas, una por estado. «Revisar» abre una pantalla para decidir de a uno, con el documento grande a la izquierda y los datos a la derecha.
  - **Abajo:** las colas, buscar, la cola vacía, el celular, el inglés y los permisos.
- **Título y bajada de las notas:** «DOC5 · B · La cola de revisión». Concepto B. La bandeja muestra cuatro colas, una por estado. «Revisar» abre una pantalla para decidir de a uno: el documento grande a la izquierda y sus datos a la derecha.
- **Notas numeradas:**
  1. **En el panel de Gestión de formularios.** «Documentos recibidos», con cuántos esperan a alguien del equipo.
  2. **Cuatro colas.** Propuestos, sin clasificar, confirmados y descartados: cuántos hay y qué espera cada una. Lo que no necesita a nadie no se mezcla con lo que sí. Los sin clasificar y los descartados se borran a los 90 días: la cola dice cuándo es el próximo.
  3. **Revisar de a uno.** La cola abierta: cuál es y cuántos quedan. Se sale con «Terminar» o volviendo, y se retoma donde se dejó.
  4. **El documento, grande.** Acercar, girar, páginas y descargar. Ocupa media pantalla.
  5. **Los datos, al lado.** Cada dato con su marca: dudoso, no leído o que no pasa. Se corrigen ahí.
  6. **El formulario.** El que reconoció la IA, con su seguridad. Si se cambia, los datos se vuelven a leer para ese formulario.
  7. **Buscar y filtrar.** Monto, CUIT u operación, y los filtros de la planilla, arriba de las colas. El resultado es una lista que se abre igual.
  8. **Los parecidos.** En la cola sin clasificar, «Armar formulario con la IA» (DOC6).
  9. **Las correcciones.** En el historial de cada documento: el dato propuesto, el confirmado y quién.
  10. **Descartar.** Pide confirmación, queda «Descartado» y sigue con el próximo. Se puede volver a abrir.
  11. **Pasar al formulario.** Guarda la respuesta con lo revisado, registra las correcciones y abre el próximo.
- **Para comparar:**
  - **A favor:** para revisar muchos seguidos: el documento se ve grande y se decide de a uno, sin buscar en una planilla.
  - **En contra:** para encontrar algo puntual o comparar varios es menos directo: hay una pantalla entre la lista y el documento.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-DOC5-B-Cola.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

#### DOC6 · «Armar formulario con la IA»

Archivo `FWA-DOC6-ArmarConIA.dc.html` («Documentos · DOC6 · armar formulario con la IA»).

- **Estado:** Elegido el 2026-10-22.
- **Fecha y versión del lienzo:** 2026-10-22, versión 116.
- **Programado:** No.
- **Qué muestra:**
  - **La pantalla «Armar formulario con la IA · Propuesta de la IA»:** Descartar la propuesta y Crear borrador arriba, y el aviso «La IA propone; vos revisás y publicás…».
  - **«El formulario»:** Tickets de combustible, con el prefijo TCB y el botón «Cargar ticket».
  - **Cómo reconocerlo.**
  - **Preguntas · 8:** cada una con su tipo y lo que se leyó en el primero.
  - **Se parece a un estándar:** todavía ninguno.
  - **Los 5 documentos:** y qué pasa al publicar.
  - **Abajo:** el aviso en la bandeja, mientras arma y si falla, si se parece a un estándar, el borrador en el editor, el celular, los permisos y después de publicar.
- **Título y bajada de las notas:** «DOC6 · «Armar formulario con la IA»». Para un documento que no encaja en ningún formulario. Nace en la bandeja, sobre los sin clasificar; la IA propone el formulario entero y una persona lo revisa y lo publica.
- **Notas numeradas:**
  1. **Lo que propone.** El nombre, el prefijo y el texto del botón, sacados de los documentos. Se cambian como en el editor.
  2. **Cómo reconocerlo.** La descripción para la IA, con lo que tienen en común los documentos parecidos.
  3. **Las preguntas, con sus tipos.** Una por dato que se repite, con el tipo que le corresponde y lo que leyó en el primero, para comparar. Se cambian, se quitan o se suman.
  4. **Se parece a un estándar.** Si alguno de la biblioteca se parece, lo sugiere antes de armar uno nuevo: trae validaciones fijas (abajo, el ejemplo).
  5. **Los documentos.** Los parecidos que lo originaron, para mirarlos mientras se revisa.
  6. **Después de publicar.** Los documentos se leen con el formulario nuevo y quedan propuestos en la bandeja. Desde ahí, los próximos se reconocen solos.
  7. **Crear borrador.** Crea el formulario en borrador, con la lectura de documentos prendida, y abre el editor. Se publica como cualquiera, con su comprobación. La IA nunca publica ni aprueba (ADR 0019).
- **Abajo, sección por sección:**
  - **El aviso, mientras arma, el estándar parecido, el borrador, después de publicar, el celular, el inglés y los permisos.** Retoma el pendiente «borrador de formulario armado por la IA a partir de los formularios que faltan»: los documentos «Otro» también se anotan ahí.
    - «El aviso, en la bandeja»: Con tres o más sin clasificar que se parecen. En el concepto A, arriba de la planilla; en el B, en la cola. Desde un solo documento, el mismo botón en su panel.
    - «Mientras arma y si falla»: Tarda unos segundos: se puede salir y volver. Si falla, no queda nada a medias.
    - «Si se parece a un estándar»: Un comprobante de transferencia en una empresa que todavía no lo agregó. Agregar sigue como en DOC1.
    - «El borrador, en el editor»: El editor de siempre, con el aviso de dónde salió y la lectura de documentos prendida.
    - «La propuesta, a 390 px»: Las preguntas, una por tarjeta; las acciones abajo.
    - «En inglés»: «Build a form with AI», «AI proposal», «Create draft», «Discard the proposal», «Looks like a standard», «Add the standard», «Build it anyway with AI» y «5 similar documents arrived. Shall we build a form?». Lo que propone la IA sale en el idioma de quien lo pide; el formulario se completa en los dos, como cualquiera.
    - «Quién lo usa»: Pide **«Revisar documentos»** (para ver la bandeja) y **«Diseñar formularios»** (para crear el borrador). Con uno solo, el botón no aparece. Publicar pide lo de siempre. La IA propone la configuración y nunca la publica ni la aprueba, como dice el ADR 0019. Lo que propone no se guarda hasta «Crear borrador».
    - «Después de publicar»: Los 5 quedan propuestos en la bandeja. Y el próximo ticket que llega por WhatsApp se reconoce solo, como en DOC2.
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-DOC6-ArmarConIA.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc6-armar-formulario-con-la-ia.png).
  - **Maqueta:** Sí, como propuesta, en `/formularios/documentos/armar`, desde el aviso de los parecidos o desde un sin clasificar.

#### DOC7-A · Tarjetas armadas solas

Archivo `FWA-DOC7-A-Tarjetas.dc.html` («Documentos · DOC7 · A · tarjetas armadas solas»).

- **Estado:** Elegido el 2026-10-22.
- **Fecha y versión del lienzo:** 2026-10-22, versión 116. La 117 lo marca.
- **Programado:** No.
- **Qué muestra:**
  - **«Pagos de clientes»:** las pestañas Respuestas 123 y Tablero, y Agregar gráfico.
  - **Los filtros:** los de la planilla, y Día, Semana o Mes.
  - **Los números:** Respuestas, Total, Promedio y Mínimo y máximo, contra septiembre.
  - **Las tarjetas:**
    - por día;
    - el monto por semana;
    - por banco;
    - por cliente;
    - quién cargó;
    - por estado;
    - por moneda.
  - **Abajo:** Agregar gráfico, el menú de una tarjeta, el celular y quién lo ve.
- **Título y bajada de las notas:** «DOC7 · A · Tarjetas armadas solas». Concepto A. La pestaña «Tablero» de cada formulario: arriba los números; abajo una tarjeta por gráfico. Se arma sola con los tipos de las preguntas y se ajusta sumando o quitando tarjetas.
- **Notas numeradas:**
  1. **La pestaña Tablero.** Junto a Respuestas, en la página de cada formulario. Solo con permiso de leer respuestas.
  2. **Los filtros de la planilla.** Recibida, quién cargó, estado y cualquier pregunta, con la misma gramática. Viven en la dirección y se comparten con Respuestas. Día, semana o mes, para lo que es por período.
  3. **Los números.** Cuántas llegaron y, de cada pregunta de número o monto, total, promedio, mínimo y máximo, contra el período anterior. Un monto con moneda, por moneda.
  4. **Por período.** Cuántas llegaron por día, semana o mes. Los días sin actividad, en un tono suave.
  5. **Por opción o catálogo.** El reparto de una pregunta de elegir: transferencias por banco, por cliente.
  6. **Quién y en qué estado.** Quién cargó cuánto; aprobadas, por aprobar y rechazadas.
  7. **Agregar gráfico.** Se elige una pregunta y cómo contarla, entre lo que tiene sentido para su tipo. Cada tarjeta dice de dónde sale y, en su menú, cambia la cuenta, se ve como tabla o se quita.
- **Para comparar:**
  - **A favor:** se ve todo de un vistazo y se arma solo: lo que más importa queda arriba.
  - **En contra:** con muchas preguntas, el tablero crece y hay que elegir qué tarjetas quedan; lo que se quitó no se ve hasta que se agrega.
- **Abajo, sección por sección:**
  - **Agregar, el menú de una tarjeta, los estados, el celular, el inglés y los permisos.** Con el ejemplo de «Pagos de clientes»: 123 transferencias en octubre, de 6 bancos y billeteras.
    - «Agregar gráfico»: Las preguntas que todavía no tienen tarjeta, con lo que se puede contar de cada una. «Volver al automático» lo deja como al principio.
    - «El menú de una tarjeta»: «Ver como tabla» muestra los mismos números: sirve para leerlos y para quien usa lector de pantalla.
    - «En el celular, a 390 px»: Una tarjeta debajo de la otra; los filtros, en un botón.
    - «En inglés»: «Dashboard», «Add chart», «Responses», «Total of Amount», «Average of Amount», «Minimum and maximum», «By day», «By week», «By month», «Who loaded», «Approved», «To approve», «Rejected», «See as table», «Remove from dashboard» y «Back to automatic». Los nombres de las preguntas y las opciones, en el idioma del formulario.
    - «Quién lo ve»: Con el permiso de leer respuestas, el mismo de la planilla. Quien solo aprueba no ve la pestaña: sigue viendo «Por aprobar». Los ajustes son de cada persona, como las columnas de la planilla: sumar o quitar tarjetas no le cambia el tablero a nadie más. No hay tableros en el código: salen de los tipos de las preguntas y usan los valores con tipo que guardan las respuestas (número, fecha, opción).
- **Reglas del 2026-10-23:** Sin choques visibles.
- **Dónde verlo:**
  - **Lienzo:** está en la página «Formularios por WhatsApp» del [Artifact](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-DOC7-A-Tarjetas.dc.html`.
  - **Evidencia en el front:** [Tablero elegido](../../../../ArquitecturaBaseFront/docs/design/evidence/documentos/doc7-a-tarjetas-armadas-solas.png).
  - **Maqueta:** Sí, como propuesta: la pestaña «Tablero» de cada formulario, en `/formularios/:id/tablero`.

#### DOC7-B · Un informe por pregunta

Archivo `FWA-DOC7-B-Informe.dc.html` («Documentos · DOC7 · B · un informe por pregunta»).

- **Estado:** Alternativa no elegida. Se retiró el 2026-10-22, en la versión 119.
- **Fecha y versión del lienzo:** 2026-10-22, versión 116.
- **Programado:** No.
- **Qué muestra:**
  - **Concepto:** un informe que sigue al formulario, con una sección por pregunta y un índice para elegir cuáles se ven.
  - **Abajo:** el final del informe, los permisos y el celular.
- **Título y bajada de las notas:** «DOC7 · B · Un informe por pregunta». Concepto B. La pestaña «Tablero» es un informe que sigue al formulario: una sección por pregunta, en su orden, con el resumen que le corresponde por su tipo. Al final, quién cargó y el estado.
- **Notas numeradas:**
  1. **La pestaña Tablero.** Junto a Respuestas, en la página de cada formulario. Solo con permiso de leer respuestas.
  2. **Los filtros de la planilla.** Recibida, quién cargó, estado y cualquier pregunta, con la misma gramática. Viven en la dirección y se comparten con Respuestas.
  3. **El índice.** Para saltar a una pregunta y elegir cuáles se ven. Es la forma de sumar o quitar gráficos.
  4. **Cuántas llegaron.** Por día, semana o mes, arriba de todo.
  5. **Las opciones y los catálogos.** El reparto, con cantidad o con el total de un monto: transferencias por cliente, por banco.
  6. **Los números.** Total, promedio, mínimo y máximo, y su evolución por período; un monto con moneda, por moneda. Cada sección elige qué muestra.
  7. **Lo que no se suma.** Un texto libre dice cuántos respondieron y lleva a la planilla, sin gráfico.
- **Para comparar:**
  - **A favor:** nadie arma nada: cada pregunta ya tiene su resumen y se recorre como el formulario.
  - **En contra:** lo importante puede quedar abajo: con muchas preguntas el informe es largo y no hay un vistazo de lo principal.
- **Dónde verlo:**
  - **Lienzo:** ya no está: se retiró el 2026-10-22, en la versión 119. El archivo queda en `C:\Users\ezequ\source\repos\lienzo-formularios\retirados\FWA-DOC7-B-Informe.dc.html`.
  - **Evidencia en el front:** no tiene. El front congela solo lo elegido.
  - **Maqueta:** No.

## Otro tablero elegido el 2026-10-17: el recorrido guiado

#### Recorrido guiado · una visita de punta a punta

Archivo `FWA-Recorrido.dc.html`.

- **Estado:** elegido el 2026-10-17.
- **Fecha y versión del lienzo:** 2026-10-17, versión 78. Ese día se le sumó el detalle D2 en la aprobación y la planilla.
- **Programado:** no aplica. Resume tableros elegidos y no define pantallas.
- **Qué muestra:** un reproductor de 1920 × 1080 con 45 pasos en ocho capítulos:
  1. Armar el formulario y sus catálogos.
  2. Publicarlo en WhatsApp.
  3. Mandarlo desde el chat.
  4. Juan responde: fotos o audio.
  5. El asistente.
  6. Reglas, aprobación y PDF.
  7. La planilla y el Excel.
  8. Los formularios que faltan.

  Es una sola visita, VIS-000024. Cada paso dice su etapa y qué hacen la persona, el sistema y la IA.
- **Notas:** cada paso es una imagen con su título y su texto. No tiene notas numeradas ni «Para comparar». Los pasos están en `recorrido/resultados.json` del generador y no se transcriben acá.
- **Reglas del 2026-10-23:** no lo revisé paso por paso. Sus imágenes son de los tableros de entonces, así que repiten lo que se marca arriba (por ejemplo, el detalle D2).
- **Dónde verlo:**
  - **Lienzo:** está en el [Artifact del lienzo](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). La copia local es `C:\Users\ezequ\source\repos\lienzo-formularios\lienzo\FWA-Recorrido.dc.html`.
  - **Evidencia en el front:** [00-recorrido.png](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/00-recorrido.png).
  - **Maqueta:** no lo tiene. Sus «Recorridos» son otros: CA6, CE6, AS6 y DOC.

**Fuera de este anexo:** desde el 2026-10-17 el lienzo también tiene «El sistema hoy». Son los tableros `SIS-*.dc.html` y los índices `Indice-*.dc.html`, con capturas del front real a 1440 y 390 px. No son diseño para elegir, así que no van acá.

## Huecos y lo que no verifiqué

**Lo que no comparé con lo publicado:**
- **El lienzo:** no abrí el Artifact. Los textos salen de la copia local, que según el README es la raíz para publicar. Si otra sesión publicó después del 2026-10-22, puede haber diferencias.
- **El prototipo y la maqueta:** tampoco los abrí. Lo que dice este anexo sobre ellos sale de sus README, de su código y de la lista de marcas «Propuesta · …» en `src/`.

**Lo que no transcribí:**
- los textos de las capturas reducidas dentro de los casos de abajo;
- los pasos del recorrido guiado;
- MF0: son capturas de «El sistema hoy», y solo transcribí sus rótulos y sus notas al pie.

**Lo que sigue abierto:**
- **Los «a revisar» de las reglas** son mi lectura. En particular, no está decidido si «Reglas de la revisión 3» (D2 y DC1 ajustado) cuenta como número de revisión: es la procedencia de las reglas, no un estado de guardado.
- **I2 sin selector:** el review E2 dice que la regla de idioma cambia el editor de I1–I3. No está decidido cómo queda «Completar el inglés».
- **D2 en el lienzo** conserva la navegación de antes de MF1. Para redibujarlo, va la de MF1.
- **El ajuste del review de E2 (CE1, CE2 y CE3-B)** está en el diseño del front, pero no en los tableros.
- **La maqueta va atrasada en E2:** muestra CE1–CE5 como propuestas, aunque E2 ya está en el código del front desde el 2026-10-23, porque usa la copia del front del 22.

**Cosas menores:**
- **Notas guía con planes retirados:** algunas notas guía del lienzo nombran planes que ya se retiraron, como `docs/plans/formularios/dar-de-baja-formularios.md`. Lo vigente está en [`docs/features/formularios.md`](../../features/formularios.md).
- **Planes viejos:** el README del generador dice que, al elegir, se anota una línea en «Ajustes ya decididos» de `docs/plans/formularios/etapa-*.md`. De esos resúmenes hoy solo queda `etapa-2-catalogos.md`; los demás se retiraron al cerrar sus etapas. Lo vigente está en [`docs/features/formularios.md`](../../features/formularios.md) y en los planes del asistente y de documentos.
- **Lo que dicen los planes del back:** solo confirman lo elegido y sus fechas. El de casos del asistente: AS1-A y AS2–AS6, de la versión 98. El de documentos: DOC5-A y DOC7-A. El de catálogos con orígenes externos y `formularios/README.md`: CA1-A/CA2–CA6 y CE1/CE2/CE3-B/CE4-A/CE5/CE6. No agregan tableros que no estén acá.
