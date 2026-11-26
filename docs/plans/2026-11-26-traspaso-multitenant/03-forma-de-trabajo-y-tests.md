# Traspaso 19 · 03 · La forma de trabajo y los tests

Sigue al [04 del 18](../2026-11-23-traspaso-multitenant/04-forma-de-trabajo.md). Para cerrar la prueba, la suite completa tenía
que pasar en verde, y ya no entraba en un día: la integración del backend tardaba 2 h 10 min en un hilo y el Vitest del front,
unos 100 minutos en serie (6.436 tests). Del 25 al 26/11 cambió la regla de cuándo se corre todo y las dos suites pasaron a
correr en paralelo. Nada de esto cambia el producto.

## 1. Cuándo se corre cada cosa (25/11, a pedido del usuario)

- **Al terminar una tarea, sólo lo que el cambio afecta.** En el backend no cambia: el build sin advertencias, los unitarios y
  `ArchitectureTests` enteros, y la integración de lo que cambió. En el front, `vitest related` (los tests que importan, directa
  o indirectamente, los archivos tocados) y las escenas visuales de las pantallas tocadas. Tocar la configuración de los tests
  o una pieza compartida pide la suite entera de eso en la misma tarea.
- **La corrida completa va una vez por día,** o antes de publicar, y cierra todo lo que entró. Si falla, se repite lo que falló
  y la siguiente lo confirma.
- Es la enmienda del 25/11 del ADR 0018 (`f769adea`, más corta en `95557f16` para que las cadenas de guías sigan bajo 30 KB), y
  el detalle está en [validar un cambio](../../guides/validar-un-cambio.md#verificación-y-cierre).

## 2. La integración del backend, en paralelo

Medido siempre en una copia, por el carril y con la app levantada:

- **32 colecciones, cada una con su `ApiFactory`** (`17375423`). Las clases de integración se reparten en 32 colecciones fijas:
  cada una tiene su Postgres, su Redis, su reloj y sus correos, y adentro los tests siguen en serie. Con el algoritmo
  conservador de xUnit, cada colección toma un hilo hasta terminar y recién ahí crea su factory, así que nunca hay más
  factories vivas que hilos. Corre la mitad de los hilos: 8 en la PC y 2 en el CI. Pasó de 2 h 10 min a 23,5 min.
- **Se parte la clase que marca el final** (`c91a3833`): la de los borradores de formularios tenía 380 casos y una colección
  sola tardaba 23 min. Ahora son cuatro clases por tema, con la ayuda común en una base que recibe la factory de cada clase.
- **El orden por tiempo medido** (`0a599eaa`): cada colección pesa lo que tardaron sus clases en la última medición (un JSON que
  se regenera desde el informe de una corrida) y arrancan primero las más lentas. Quedó en 22,1 min con la CPU al 100 % en el
  p90: el límite ahora es el procesador, no el orden.
- **Lo que rompió el paralelo, y cómo se arregló:**
  - un `ActivityListener` oye a todos los hosts del proceso: un test de trazas elige su span por su operación, nunca con
    `Assert.Single` por nombre (`fbdc1332`);
  - un test que mide memoria del proceso (`GC.GetTotalAllocatedBytes`) suma lo de los demás: va en una colección que corre sola,
    al final (`ded021a7`);
  - un cupo que se libera en el servidor se espera con reintentos, no apenas el cliente ve el corte (`fbdc1332`);
  - una cola compartida por la colección: el test procesa hasta que sale lo suyo y cuenta sólo lo suyo (`2366fccc`, `5b36dbcf`);
  - el reloj compartido por la colección: un test que depende de la fecha la fija él, porque otro puede haberlo llevado cerca
    del fin de mes (`76bca76d`).
- **Las cohortes de contrato del front** salen de una misma corrida, la primera con la integración en paralelo (front
  `1ea34870`): cambia el contenido (cada colección tiene su base y su reloj), no el contrato.

## 3. El Vitest del front, en paralelo

- **Los plazos de toda la suite subieron** a 10 s para la espera (`findBy`) y a 20 s para cada test y gancho (front `6c8702cf`):
  el primer test de cada pantalla paga su carga diferida y, bajo carga, cortaba sin nada roto. Van en `src/test/setup.ts` y no
  en `vite.config.ts`, para que cambiarlos no reinicie el Vite de la app.
- **La suite corre con la mitad de los hilos** (`maxWorkers: "50%"`, `1f0f1454`): 6.445 de 6.445 en 19,9 min, contra unos 100 en
  serie. El mismo commit sacó una espera inútil: un test que buscaba primero un diálogo que nunca aparece esperaba los 10 s
  enteros de `findBy`.
- **StrictMode:** el arreglo de O60 ([02](02-cierre-de-la-prueba-integral.md) §1) suma dos tests con el diálogo dentro de
  StrictMode, para que un efecto que manda un pedido no salga dos veces.

## 4. Para el multitenant

Leído en tu `main` del 26/11 (`b2334284`).

- **Cuándo se corre:** tu regla «Cada verificación pasa completa una sola vez por etapa» (`AGENTS.md`) y tu guía de la etapa
  en paralelo (`docs/guides/etapa-en-paralelo.md`) ya corren sólo lo que toca un arreglo. Lo nuevo de acá es la cadencia diaria de la corrida completa y
  `vitest related` en el front. No hace falta pregunta: lo decide quien coordine tu etapa, con el usuario.
- **La integración:** tus tests de integración comparten una sola colección (`tests/ArquitecturaBaseMultitenant.Api.IntegrationTests/Support/ApiTestGroup.cs`).
  Si tu suite crece como la de acá, el reparto en colecciones con su propia factory, la mitad de los hilos y el orden por tiempo
  medido bajaron el tiempo a la sexta parte. Antes, revisá lo que rompe el paralelo (§2): en tu modelo suma la organización,
  porque cada colección necesita sus propias organizaciones, sus roles de runtime y su RLS sobre su base.
- **El front:** los plazos en `setup.ts` y `maxWorkers` sirven igual cuando tu suite pase los mil tests.
