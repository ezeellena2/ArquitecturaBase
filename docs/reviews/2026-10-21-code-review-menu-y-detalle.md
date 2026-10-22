# Code review: menú y detalle completo, y aislamiento del asistente — 2026-10-21

Revisión de solo lectura de backend `29d3bc64..HEAD` y front `d040fda..HEAD`, sin los commits de documentación de planes ni de diseño. Cubre dos partes:
- **A:** el aislamiento por conversación de los turnos manuales del asistente.
- **B:** la etapa Menú y detalle ([procedencia del plan](../history/README.md#formularios-menú-y-detalle-completo--2026-10-21)), con el cierre en [formularios](../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21).

**Lo que está bien:**
- **Aislamiento:**
  - el índice por actor, canal y conversación coincide con la consulta;
  - ningún chat espera el turno de otro y cada uno tiene su conversación;
  - las cuotas, el replay y la cancelación se conservan;
  - la migración es segura con turnos en curso, y su `Down` no pierde trabajo;
  - el test concurrente retiene la primera llamada y comprueba destino, contexto y que no se mezclen.
- **Permisos:**
  - «leer o aprobar» se combinan bien;
  - las tres personas ven lo que corresponde en el menú, la tabla, «Por aprobar» y la pantalla completa;
  - 401 sin sesión y 403 sin permiso.
- **Overview:**
  - los pendientes cuentan con roles vigentes y sin las respuestas propias;
  - el orden es único;
  - usa el índice parcial nuevo de tareas.
- **Navegación:**
  - usa los mismos filtros y el mismo orden que la planilla;
  - va por POST, sin datos privados en la URL;
  - el 404 es el mismo para una respuesta ausente o ajena.
- **PDF por acción:** el vínculo ya existía (`RunId`), así que los históricos lo tienen.
- **Retiro limpio:** la bandeja global, `api/forms/navigation` y `approvals/options` no tienen consumidores, y el inventario de rutas, OpenAPI y los ejemplos están actualizados.
- **El panel lateral no perdió entradas:** la captura con solo «Formularios (4)» sale de un usuario de prueba del arnés visual con dos permisos.
- **Front:**
  - contratos zod estrictos contra los cuerpos exportados;
  - sin importaciones entre features, storage ni MutationCache;
  - `CollapsibleSection` es accesible.

Se corrige en un solo chat, back y front juntos, cada arreglo con su test y commits chicos. Las decisiones no cambian. Después va el piloto con Meta junto con el de dar de baja ([verificación](../plans/verificacion-y-produccion.md)).

## Arreglar ahora

**Alta**

1. **El modelo de la base no coincide en Linux, y el despliegue falla.**
   - **Dónde:** el filtro del índice de `AiAssistantTurns` está escrito como texto de varias líneas (`AiAssistantTurnConfiguration.cs:28-31`). En Windows, la migración `20261021180953_ScopeAssistantActiveTurnsByConversation` y el snapshot grabaron el salto de línea como `\r\n` (`ApplicationDbContextModelSnapshot.cs:559`); antes era `\n`.
   - **Qué pasa:** en Linux (CI y despliegue), el modelo arma el filtro con `\n` y el snapshot tiene `\r\n`. EF ve cambios pendientes y `MigrateAsync` falla, igual que los tests de migración de IA y el `efbundle`. En Windows pasa: por eso la corrida local dio verde.
   - **Arreglo:**
     - escribir el filtro en una sola línea;
     - regenerar el snapshot y el `Designer` con el texto sin `\r`;
     - un test de arquitectura que falle si un filtro o un SQL del snapshot o de las migraciones contiene `\r`.
   - Sumar un `.gitattributes` queda para evaluar aparte: no se renormaliza el repo en este arreglo.

**Media**

2. **La tabla de Formularios lee y descifra todas las versiones publicadas en cada carga** (`FormOverviewReader.cs:16-19`, que usa `FormRecordReader.Versions.cs:12-24`).
   - **Qué pasa:** trae los esquemas completos y descifra el manifiesto de cada formulario publicado, aunque solo necesita el nombre y los idiomas. Ahora es la pantalla de entrada.
   - **Arreglo:** una proyección liviana (id, nombre, cultura y culturas publicadas) sin el esquema ni el descifrado.
3. **«Con algo por aprobar» mezcla formularios dados de baja sin marcarlos** (`FormOverviewPage.tsx:119,128-136`).
   - **Qué pasa:** con leer y aprobar, el filtro incluye los dados de baja con pendientes, pero la fila no lo dice. Además, el segmentado sigue con «Activos» marcado y el aviso de arriba se repite.
   - **Arreglo:**
     - la etiqueta «Dado de baja» junto al nombre, como en MF1;
     - con el filtro activo, sin aviso y sin segmento marcado.
4. **El vacío de la tabla dice algo falso** (`FormOverviewPage.tsx:139`).
   - **Qué pasa:** quien solo aprueba y no tiene pendientes ve «Todavía no hay formularios publicados». Pasa lo mismo en «Dados de baja» sin ninguno.
   - **Arreglo:** un texto para cada caso.

**Baja**

5. **Cada flecha trae todos los ids del conjunto filtrado** (`FormResponseReader.Navigation.cs:9-13`). Con 100.000 respuestas son varios MB para quedarse con tres.
   - **Arreglo:** `ROW_NUMBER`, `LAG` y `LEAD` en SQL, y devolver una sola fila.
6. **«Por aprobar» se recorre por páginas con offset** (`FormApprovalReader.cs:17-22`, `useFullResponseNavigation.ts`). Si otro aprobador decide tareas de páginas anteriores, las flechas se saltean tareas pendientes. Es plausible, no se verificó.
   - **Arreglo:** recorrer por clave (después de `RequestedAtUtc` e `Id`), o con un ancla como la de las respuestas.
7. **«Formularios» no queda marcado en el menú** dentro de un formulario ni en la pantalla completa (`Sidebar.tsx:680`, con `exact`).
   - **Arreglo:** que cubra también `/formularios/:id/respuestas…`.
8. **Las migas de D2 suman un «Respuestas» genérico** (`Breadcrumbs.tsx:42,106`): queda «Formularios / Respuestas / Registrar visita».
   - **Arreglo:** tomar como detalle solo la ruta `/completa`.
9. **Queda marcada una fila vieja al cerrar D2** (`FormResponsesPage.tsx:102,318`). Abrís A, bajás con ↓ a B y cerrás: el foco va a B, pero A queda seleccionada.
   - **Arreglo:** actualizar la selección al navegar.
10. **En el celular, «Ver PDF» llega a la pantalla completa con el PDF escondido** (`FormResponsesPage.tsx:157`, `FormResponseFullPage.tsx:51`): Actividad arranca plegada.
    - **Arreglo:** pasar la intención y abrir Actividad al entrar por ese atajo.

**Tests que faltan:**
- el panel con todos los permisos, con las cuatro entradas, y una escena visual de la persona que lee y aprueba;
- la marca de dados de baja con «Con algo por aprobar»;
- las migas de D2;
- ↑ ↓ con teclado en la pantalla completa entrando por «Por aprobar»;
- la tabla de Formularios excluye un formulario eliminado, un borrador sin publicar y una publicación en curso;
- el filtro sin `\r` del punto 1.

## Después, sin bloquear

- **El número del menú** sale como «Formularios (4)», en texto. El tablero dibuja una insignia.
- **Detalles de MF1:**
  - el «(N)» junto a «Con algo por aprobar»;
  - el resumen «N formularios»;
  - los conteos del segmentado con el filtro activo.
- **Resúmenes de las secciones plegadas de DC1:** que digan preguntas, fotos y documentos, y «N momentos · estado».
- **El bloque «Por aprobar» vacío** arriba de la planilla, para quien lee y aprueba. El tablero no lo define.
- **El ancla de navegación** puede devolver 404 si, después de aprobar, la respuesta deja de cumplir un filtro privado. Plausible.
- **El contador del menú** no declara su propio manejo de errores y podría avisar dos veces. Ya pasaba antes de esta etapa.
- **Otros detalles:**
  - `FindRecordNavigationAsync` devuelve 404 antes de revalidar el permiso, como `ListAsync`;
  - la tabla ordena con un `OrderBy` fijo, coherente con no tener columnas ordenables;
  - un test invoca un método privado por reflexión.

## Contraste y corrección — 2026-10-21

Se contrastaron los diez puntos con sus recorridos antes de corregirlos. Los puntos 2 y 5 son costes de lectura/transferencia reales; no alteraban el resultado funcional. Los tres casos negativos de Overview y las cuatro entradas del menú son cobertura faltante, sin una pérdida de datos o entradas comprobada. El resto tiene un recorrido concreto que contradice la interacción o la portabilidad esperadas. «Después, sin bloquear» queda fuera de esta tanda.

- **P1:** confirmado por dos tests en rojo sobre modelos y operaciones compilados. Filtro en una línea; snapshot y ambos Designers regenerados con EF conservando identificadores y operaciones históricas. El guard inspecciona filtros y SQL efectivos, incluidos Up/Down y metadatos, con controles positivos/negativos y coincidencia con el modelo sin base. Los 19 SQL multilínea de 14 migraciones anteriores normalizan sólo su argumento a LF, con autorización explícita y preservando el resto de cada archivo; no se agregó `.gitattributes` ni se renormalizó el repositorio. Portabilidad **7/7** verde; la integración se reserva al cierre conjunto.

- **P3/P4:** los tests reprodujeron la baja sin marca, el segmento/aviso incoherentes y los vacíos falsos. El filtro de pendientes desmarca los segmentos y retira avisos duplicados; cada baja se identifica en la fila. Los vacíos distinguen pendientes, bajas y búsqueda en español e inglés. Cortes `604305b` y `71cd169`; Overview y paridad **41/41** verde.
- **P7/P8:** reproducidas la navegación sin entrada activa y la miga extra de D2. Formularios queda activo en la familia de respuestas; Diseñar, Catálogos y Faltantes mantienen sus entradas independientes. Sólo `/completa` agrega la rama de detalle. Cortes `36e54d0` y `bf0820b`; Sidebar **28/28** y migas **13/13** verdes. Se agregó cobertura de las cuatro entradas con todos los permisos y se ampliaron las escenas existentes de lectura y decisión, sin ejecutar visuales durante los cortes.
- **P9:** dos regresiones reprodujeron la selección vieja al cambiar por flechas, tanto en la misma página como entre páginas. La selección y la página recordadas siguen al destino confirmado; Escape devuelve el foco a esa misma fila. Corte `a9f8a6f`; detalle/panel **53/53** verde.
- **P10:** el test del atajo móvil reprodujo Actividad plegada. Una intención interna efímera abre esa sección sólo al entrar por «Ver PDF»; las entradas ordinarias y compartidas mantienen la carga diferida, y volver retira la intención. Corte `2da26a1`, tres casos nuevos y detalle/panel **56/56** verde; build sin advertencias y arquitectura front **49/49**. Peer independiente limpio.
- **P2:** confirmado como coste evitable de la pantalla de entrada. `PublishedCultures` se sella desde los perfiles verificados; migración nullable `text[]` y backfill en el seeder existente, sin otra inicialización. Overview proyecta nombre/culturas en SQL desde la activa y no depende del lector de esquemas o del protector. Cortes `b3ea9253` y `61fc7ed6`; rojo previo de metadata y query, luego Domain **11/11**, Overview **4/4**, Application BusinessForms **1.025/1.025** y arquitectura **170/170** verdes; build sin advertencias y modelo sin pendientes. Quedaron preparados para Docker los casos de aceptación/backfill en ambos originales, lectura sin protector y exclusión de borrador, primera publicación abierta y eliminado, con control positivo de activa publicada durante un reemplazo abierto.
- **P5:** confirmado como transferencia proporcional al total para obtener sólo vecinos. El anclaje usa ventanas SQL y devuelve como máximo una fila, después de aplicar el mismo filtro/orden de la planilla. El adaptador local acepta sólo esa proyección, conserva subconsultas, nulos, desempates, parámetros Npgsql y llaves literales al reingresar al pipeline EF. Corte `0c63a8ab`; rojos de consulta y llaves, luego Query **13/13**, SQL **8/8**, servicio **12/12** y arquitectura **170/170** verdes, solución sin advertencias. Peer independiente limpio; combinaciones de nulos/empates y casos HTTP existentes preparados para Docker.
