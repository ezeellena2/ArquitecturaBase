# 06 · Arquitectura, forma de trabajo y reglas del usuario

Lo que cambió en las reglas de la plantilla, en cómo se revisa y se valida, y en lo que el usuario pidió sobre la forma
de trabajar. Las reglas del usuario anteriores al 7/11 están en el 03 del traspaso 10 y siguen valiendo.

## 1. La arquitectura canónica (`docs/architecture/backend.md`)

- **Validación con texto del proyecto (2026-11-13).** Cada regla de FluentValidation dice un texto de `ValidationMessages`
  o del `<Entidad>Errors` de su área, también en los módulos, en el idioma de cada pedido. Sin eso respondía con el texto
  de FluentValidation. Lo controla un test de arquitectura (`fb76366a`; arreglos `3e0ea469` y `e98d27a3`).
- **Fechas y números para una persona.** Las fechas con números y `InvariantCulture`; los números con `DisplayNumbers`, la
  única pieza del servidor (región es-AR o en-US, el porcentaje pegado). Detalle en el 05, §2.
- **Paginado con tope propio.** Una consulta que lee menos filas por vez (las opciones de una pregunta, 50) le pasa su
  tope a `PagedRequestValidator`, y el error dice ese tope («entre 1 y 50»).
- **Una restricción que EF no modela va en SQL dentro de la migración generada**, con un comentario en la migración y en
  la configuración: las claves únicas diferidas de los catálogos y la clave foránea diferida de la revisión activa de
  reglas, el caso de dos filas nuevas que se apuntan entre sí en un solo guardado (un formulario que se agrega desde la
  biblioteca y su primera revisión de reglas). Salió de la regla «lo más robusto y correcto» (§5).
- **La cola de entregas** sigue sin reintentar lo reservado, con una excepción: lo que el worker devuelve porque la Api se
  apagaba mientras esperaba su turno vuelve a su lugar para otra copia (H8, 03).
- **Dos puertos neutrales nuevos** entre módulos: `IStructuredRecordIssuer` (una respuesta pide emitir un comprobante) e
  `IStructuredCatalogFeed` (un módulo mantiene catálogos, como las tablas de ARCA). Los cuentan el 01 y el 04.
- **Caché.** Si la fábrica puede devolver la falla de un proveedor externo, no se guarda (`keep`, en
  `docs/guides/agregar-cache.md`); lo usa el padrón de ARCA.

## 2. ADR

- **Nuevos:** 0026 (el robot de delegación de ARCA y la clave fiscal sólo en memoria), 0027 (la delegación guiada de
  ARCA), 0028 (la emisión de comprobantes de ARCA) y 0029 (la biblioteca de estándares, que aportan los módulos como
  datos). Los cuentan el 04 y el 01. El índice está en `docs/decisions/README.md`; el último número es 0029.
- **Enmiendas:** 0018 (2026-11-13: la corrida completa va una sola vez, al cerrar el desarrollo; §3) y 0021 (las
  respuestas redactadas del asistente con agente; 03, §3).

## 3. Cómo se revisa y se valida

- **La verificación (2026-11-13).** Al terminar una tarea o una etapa: el build sin advertencias, los unitarios y
  `ArchitectureTests` enteros y la integración de lo que cambió. La corrida completa (`dotnet test` con Docker, más build,
  lint, vitest y las pruebas visuales del front) va una sola vez, al cerrar el desarrollo, y sin ella no se cierra. Con
  varios chats a la vez la corre una sola sesión, con la PC quieta, cuando terminan todos. Antes se corría al cerrar cada
  paso: la suite pasó de 2.605 tests y 22 minutos a unos 10.600 casos y más de una hora. Está en `AGENTS.md`, en
  `docs/guides/validar-un-cambio.md` y en la enmienda del ADR 0018.
- **Lo que más se repite en las revisiones (2026-11-08).** `validar-un-cambio.md` suma doce preguntas antes de cerrar:
  comparar con el tablero, textos ciertos en todos los estados, un solo dueño para cada error en pantalla, invalidar todo
  lo que muestra un dato, ofertas con la misma condición que el servidor, cada falla con su clase y su test, topes de una
  sola constante medidos antes de pagar, uno malo no frena al resto, andar con los datos que ya existen, tests que fallan
  sin el arreglo, el mismo error en otros caminos y contratos exportados que el front acepta tal cual.
- **Revisar la arquitectura de un desarrollo (2026-11-12).** La guía `docs/guides/revisar-arquitectura.md` revisa un
  desarrollo entero contra 30 criterios en 8 bloques: que no se mezclen los pedidos, varias copias, volumen, terceros,
  seguridad, datos, lo que ve el usuario y operación. Cada criterio se juzga en tres horizontes (hoy, varias copias, gran
  escala) y se arreglan los de «hoy»; la gran escala sólo si el usuario elige ese horizonte (ADR 0018). La primera corrida
  fue sobre WhatsApp (03, §1). Desde el 14/11 corren las de ARCA y el núcleo; IA y Formularios van después.
- **Contra el código, no contra los reportes (2026-11-11).** El 11/11 se comparó el código de cada desarrollo con sus
  decisiones (aparecieron nueve fallas que se iban a ver en la prueba) y con las premisas escritas: lo grueso se cumplía
  (capas, errores declarados, paginado, paridad es/en, fechas, contratos) y aparecieron 57 desvíos, 14 importantes, casi
  todos en la factura de ARCA. Se corrigieron en la misma tanda.
- **La revisión adversarial va según el riesgo (2026-11-13).** Completa, con su corrección antes del commit, cuando la
  tarea toca datos (migraciones, persistencia, cifrado), permisos o efectos fuera del sistema (Meta, correo, archivos,
  LLM). En documentos, textos, tests y arreglos de una línea no hace falta.
- **Las guías por carpeta, por debajo de 30 KB.** `InstructionChainSizeTests` (`88f73990`) mide cada cadena de `AGENTS.md`
  desde la raíz. El detalle de las carpetas planas pasó a documentos de su feature (`e24e4bc4`):
  `docs/features/formularios-servicios.md`, `formularios-pruebas-unitarias.md`, `formularios-pruebas-de-integracion.md`,
  `formularios-persistencia.md`, `formularios-borde-http.md` y `whatsapp-servicios.md`.
- **Paridad de recursos (H17).** Cada `.resx` de Application tiene su `.en.resx` con las mismas claves, descubierto solo,
  sin listas a mano (`64d878a0`).

## 4. Despliegue y dependencias

- **Aspire 13.6.5** por el aviso GHSA-f7vh-h5g3-7jrh (`e3fdb3ee`). La CLI local se actualiza aparte, con el visto bueno
  del usuario.
- **Respaldo y restauración de PostgreSQL (H15, `54b213a4`).** `docs/deploy/azure-setup.md` crea la base con
  `--backup-retention 7` y `--geo-redundant-backup Disabled`; la copia en otra región se decide antes de crear la de
  producción, porque no tiene vuelta atrás. Restaurar crea otro servidor. Una Api levantada sobre una copia restaurada
  vuelve a mandar lo que en ese momento estaba pendiente, así que la primera vez se levanta con los workers apagados
  (`WhatsApp:ProcessOutgoingInBackground`, `WhatsApp:ProcessFlowsInBackground`,
  `BusinessForms:ActionWorker:ProcessInBackground` y `Arca:Invoicing:Worker:ProcessInBackground` en `false`) y se prenden
  después de revisar la base. Las colas de correo y de acceso de WhatsApp no tienen interruptor.
- **Encabezados de un proxy.** Mal configurados, con el registro abierto todas las personas comparten el tope diario de
  códigos de WhatsApp de una dirección (H10, `docs/guides/despliegue.md`).

## 5. Reglas del usuario nuevas (del 7 al 14 de noviembre)

Para trabajar con él desde el primer día, como las del 03 del traspaso 10.

- **Terminar todo y probar una sola vez (11/11).** Pidió que cada chat termine todo lo que se le pidió, back y front, para
  hacer una sola prueba integral de punta a punta con todo terminado. Los traspasos y el retiro de los planes iban después
  de esa prueba; este traspaso se adelanta a su pedido, y por eso cuenta lo programado como «sin la prueba integral».
- **Un «terminado» se comprueba contra el código**, no con los commits ni los reportes de un chat (11/11). Al listar lo que
  falta, se separa lo que el usuario decidió de lo que sólo está propuesto en un plan.
- **Lo más robusto y correcto (11/11).** Ante tres formas técnicas de resolver algo, contestó: «hagamos lo mejor y más
  profesional, si tenemos que romper una regla específica para este caso lo hacemos, pero siempre tiene que ser lo más
  robusto posible y profesional y correcto». Una excepción a una regla vale sólo si es lo más robusto, acotada y escrita
  (enmienda o nota), nunca por comodidad ni para cambiar una decisión de diseño ya confirmada. Convive con el ADR 0018.
- **Menos tiempo de proceso (13/11).** Preguntó por qué cada cosa tardaba tanto y aprobó tres recortes: la corrida
  completa una sola vez al cerrar, la revisión adversarial según el riesgo y tres o cuatro chats programando a la vez,
  sin abrir sesiones nuevas en paralelo salvo que lo pida. Los chats quietos se archivan sólo con su aprobación.
- **Subagentes cortos (12/11).** Una tarea de más de unos 150 pasos se parte en dos subagentes nuevos, y las correcciones
  de una revisión van a uno nuevo, no al que programó: el costo crece con el contexto que arrastra cada paso.
- **El cupo de la semana (12/11).** Antes de que se acaben los tokens, terminar las tareas; si no alcanza, dejar escrito
  qué falta, tarea por tarea. Se mira el cupo antes de empezar: con 85 % se escribe lo que falta y con 95 % no se empieza
  nada nuevo.
- **Revisar la arquitectura de cada desarrollo (12/11)** contra los 30 criterios, con los arreglos de «hoy» primero (§3).
- **Anexar antes de crear un plan (10/11).** Si un tema ya vive en otro plan, se anexa ahí como sección; un plan aparte sólo
  si no se parece a nada. Lo marcó como lo que más le importaba: había más de 50 filas en el índice.
- **Diseño:**
  - la lista de chequeo antes de mostrar un tablero y antes de programarlo (05, §1);
  - los tableros se dibujan copiando las medidas y las piezas de las capturas reales del front
    (`src/test/visual/screenshots/`), y un revisor los compara con esas capturas antes de mostrarlos (12/11). Dibujados de
    memoria salían con anchos mezclados, botones repetidos y líneas que la app no tiene;
  - una tarea secundaria de una pantalla va en un modal que se abre con un botón, con un resumen de una línea en la
    pantalla; un lienzo de un flujo abre en el mapa y cada tablero tiene «Siguiente» (12/11);
  - fechas sólo con números (10/11) y números con la región de cada idioma (12/11).

## 6. Advertencias técnicas nuevas

- **La PC cargada parece un bug.** Con muchos chats abiertos (en especial con Remote Control), la CPU queda al 100 % y los
  tests del front vencen a los 5 s o las corridas cortan con timeouts de Redis. Antes de culpar al código, medir los
  procesos y las corridas de otras sesiones, y repetir con la PC tranquila.
- **Una corrida con un proveedor real** (IA, Meta, ARCA) necesita el visto bueno del usuario y la máquina sin suites del
  backend corriendo: el arnés de calificación corta en la primera falla y cada intento cuesta créditos.
- **Si la app de Claude se reinicia**, las corridas en segundo plano mueren y los chats que las esperaban se quedan quietos
  sin avisar: hay que revisarlos y pedirles que retomen.

## 7. Para el multitenant

- **La cadencia de verificación** (§3) choca con lo que dicen tus fichas si piden la suite completa por etapa: es una
  pregunta para el usuario, con este recorte como recomendado, porque lo aprobó acá.
- **La guía de revisión de arquitectura** se puede adoptar con un bloque más para tu modelo: que no se mezclen las
  organizaciones (RLS, 404 de lo ajeno, el fuzz de aislamiento).
- **Las doce preguntas de revisión** se parecen a tu «puerta» de doce puntos: conviene juntarlas en una sola lista.
- **Los mensajes de validación con texto propio** y la paridad de todos los `.resx` son controles baratos para sumar a tus
  tests de arquitectura.
- **La restauración** de una copia con los workers apagados vale igual con varias organizaciones, y los interruptores son
  de la instalación, no de cada organización.
