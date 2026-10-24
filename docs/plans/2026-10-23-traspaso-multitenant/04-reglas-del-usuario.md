# 04 · Reglas nuevas del usuario

Las fijó el usuario usando la aplicación, el 2026-10-21 y el 2026-10-23. Valen para todo lo que se construya, en ArquitecturaBase y en el multitenant. Si chocan con una decisión anterior de la hoja de ruta del multitenant, se ven en [02](02-que-cambia.md).

## 1. Interfaz: botones sin explicaciones (2026-10-23)

> «Hay que dejar de mostrar los botones que dan explicaciones, ningún sistema tiene esto. No podés estar modificando botones por cada acción que ocurre: o aparece grisado o aparece habilitado. Y llevar los botones al borde, están muy al medio.»

Quedó como regla en la [guía visual del front](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), en «Pantalla» y en «Formularios y diálogos» (commit `bb98bf2`):
- **Un botón está habilitado o deshabilitado, sin textos al lado ni debajo.**
  - Nada de «No hay cambios para guardar», «Revisá los datos marcados antes de guardar», «Guardá el catálogo antes» ni «Primero guardá los cambios».
  - Los botones no cambian de lugar ni de forma con cada acción.
  - Si el motivo de un deshabilitado no es obvio, va como tooltip al pasar el mouse o con el foco, también para el lector de pantalla.
- **Sin «Cambios sin guardar» ni números de revisión** en los encabezados ni al lado de los botones. Que Guardar esté habilitado ya dice que hay cambios. El estado del título es solo el del objeto, como «Activo» o «Dado de baja». Los números de revisión quedan en los historiales, y se conserva el aviso al salir con cambios sin guardar.
- **Las acciones de una superficie van al borde derecho de su encabezado,** a la altura del título.
- **Se valida al intentar guardar.** Después del primer error, en vivo mientras se corrige. Nunca se marca en rojo una fila recién agregada.
- **Lo elegido en un paso anterior que ya no se cambia se muestra como dato de solo lectura,** no como un campo deshabilitado con explicación. Por ejemplo, el origen y el tipo de un catálogo van como «Escritas a mano · Texto».

**Causa del error.** La guía visual decía «al guardar, el botón se deshabilita y explica el estado» y ponía «Cambios sin guardar» como ejemplo de estado. Codex lo repitió en todas las pantallas. **Revisen sus propias guías de diseño buscando lo mismo.**

## 2. Idioma del contenido (2026-10-23)

> «No tenemos que obligarle a escribir los catálogos y formularios en inglés o español. Le van a aparecer según el idioma seleccionado en el menú. Nadie le obliga a crearlos en inglés.»

- **Lo que se crea en la web se escribe en el idioma elegido en el menú:** formularios, catálogos, opciones, campos, datos de las personas y lo que venga. Ese es su **idioma original y el único obligatorio**.
- **El otro idioma es una traducción opcional.** Se muestra en el idioma de la pantalla y, si falta, en el original.
- **No hay selector de idioma en las pantallas** (se sacan «Textos en: Español | Inglés» y los pares «Nombre» / «Nombre en inglés»):
  - para traducir, se cambia el idioma del menú y se edita lo mismo;
  - un campo sin traducción muestra el original como referencia, sin copiarlo.
- **Para mandar y publicar sigue la regla del 19/10:** cultura común completa, sin mezclar idiomas, y respaldo al original. La comprobación de publicación avisa lo que falte del original.

**Qué había antes.** En ArquitecturaBase los catálogos seguían bilingües obligatorios, por una excepción explícita de la regla del 19/10. Los campos, las opciones y los datos de las personas exigían español. El editor de formularios tenía un selector «Textos en». Todo eso se corrige en el punto 35 del [review E2](../../reviews/2026-10-23-code-review-catalogos-e2.md).

## 3. El producto es genérico (2026-10-21 y 2026-10-22)

> «Olvidate específicamente de OCHA, yo necesito que este proyecto sea totalmente genérico.»

- Ningún cliente, tipo de documento ni respuesta va escrito en el código.
- Lo particular de un cliente es configuración por organización: casos y perfil del asistente, formularios estándar, catálogos y campos.
- Los ejemplos de los documentos (OCHA, Macrosistemas) son ilustrativos.

## 4. Soluciones simples

> «Data Factory es un lío terrible. Hacelo simple.»

- **Ante dos caminos, el más simple que cumpla las reglas.** Por ejemplo, para conectar con la base de un cliente se propuso una VPN con un usuario de solo lectura y no un conector propio.
- **No se agregan mecanismos «por las dudas».** La robustez es proporcional al efecto (ADR 0018): durable solo para lo que sale del sistema.

## 5. Navegable antes que dibujado

> «Necesito poder ir paso a paso… navegable.»

- Los tableros estáticos no alcanzan: el usuario quiere recorrer la app como si fuera real.
- **La maqueta viva** cumple eso: el front real con un backend falso, personas, idiomas, un celular simulado y las propuestas marcadas.
- Antes de programar una etapa, sus pantallas tienen que poder recorrerse.

## 6. Topes y límites que fijó el usuario

| Límite | Valor |
| --- | --- |
| Opciones por catálogo | Hasta **10.000**, sin configuración. WhatsApp muestra hasta 200 en una lista y 20 en «elegir varias» |
| Documentos sin usar o descartados | Se borran a los **90 días** |
| Quién puede mandar documentos para leer | Solo **cuentas vinculadas** |
| Tipos de documento que se leen | Foto, PDF, Word (`.docx`), Excel (`.xlsx`), ubicación y contacto. Formatos viejos (`.doc`, `.xls`), con macros o con contraseña, no |
| Carga automática de un documento | **Después, con datos medidos.** Al principio, siempre confirma una persona |
