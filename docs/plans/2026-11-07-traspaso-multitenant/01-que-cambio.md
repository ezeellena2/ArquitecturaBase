# 01 · Qué cambió desde el traspaso 9 (6 y 7 de noviembre)

Rango: en el backend, los commits posteriores a `fa22f878` (traspaso 9); en el front, los posteriores al 2026-11-06
09:56. Están todos en `main`. Los traspasos 6, 7 y 8 se commitearon antes de ese límite: acá no se repite lo que cuentan.
Después del límite sólo se retocaron el 5, el 7 y el 9, con RG10-D, RG12-A y los títulos de sección aceptados por Meta.

## 1. Renglones en los formularios

### Decisiones nuevas

- **RG10-D (06/11), por programar.** El bloque se agrega con «Agregar bloque», al lado de «Agregar pregunta», como una
  pregunta más. Arranca abierto y con «Bloque» de nombre, también cuando lo crea «Se carga varias veces». Con un bloque
  ya armado, el botón se apaga con su motivo. «Tipo de respuesta» deja de ofrecer «Renglones». Se descartaron RG10-A, B y C.
- **RG12-A (07/11), por programar.** Todo texto que ve el usuario dice «ítem» donde decía «renglón» («item» en inglés): el
  editor, publicar, WhatsApp, la carga, las lecturas y las reglas. Por ejemplo, «Bloque · de 1 a 10 ítems · 3 preguntas»,
  «Preguntas de cada ítem», «¿Agregás otro ítem?», «Artículos · ítem 2» y la columna «Ítem» del Excel. El código y los
  documentos siguen diciendo `rows`. Se descartaron RG12-B (el nombre del bloque en la pregunta de WhatsApp) y RG12-C (un
  nombre en singular por ítem). La tabla completa de textos es/en está en el diseño del front.
- **RG13-A (07/11), parte de la Etapa 2.** Con WhatsApp instalado, publicar frena si el mínimo del bloque es mayor que 10:
  «En WhatsApp se cargan hasta 10 ítems; el mínimo es 15.». La captura valida siempre contra el mínimo y el máximo.

### La verificación con Meta (Etapa 1)

- **El `routing_model` (backend `38b22057`).** Responder «No» a «¿Agregás otro renglón?» cortaba: la vista oficial decía
  «Can't perform a transition from [FORM_A] to [FORM_K], because it doesn't satisfy provided routing_model», y el celular,
  «Se produjo un error». Sin `routing_model`, Meta lo deduce de los `navigate` y deja afuera la rama `else` de un `If`.
  Ahora el Flow con bloque declara cada `navigate` de cada pantalla, con las dos ramas. Sin bloque se compila igual que antes.
- **Lo que Meta aceptó.** Tres variantes publicadas y verificadas:
  1. preguntas después del bloque, con dos títulos de sección;
  2. el bloque al final;
  3. mínimo 0, con «¿Agregás un renglón?» antes del primero.

  Acepta el `If` con un `Footer` en cada rama dentro del `Form`, el mismo `name` en las diez pantallas, las referencias a
  renglones que pueden no visitarse, los `TextSubheading` de los títulos, las pantallas terminales que navegan en una rama
  y completan en la otra, y el `routing_model` declarado en un Flow sin endpoint.
- **Lo que llega del celular.** Con un renglón y «No», 7 claves y 415 bytes. Con «Sí», «Sí» y «No», 15 claves y 886 bytes,
  con `_r1` a `_r3` en cada pregunta del bloque y en `field_<bloque>`. **De los renglones no visitados no llega ninguna
  clave**: ni vacía ni con `${…}`.
- **Hoy la respuesta se descarta.** La app la marca `Unrecognized`, porque la proyección no acepta el tipo `group`, y el
  chat dice «Llegó una respuesta de formulario que no pudimos relacionar con un envío». Guardarla es de la Etapa 2, que
  además tiene que cambiar ese aviso.
- **Falta** la respuesta de unos 40 KB.

### Otros cambios

- **La planilla de un formulario con bloque (backend `c9db6d2c`).** Fallaba con «No pudimos cargar las respuestas.»: el
  backend armaba una columna para el bloque y el esquema del front la rechazaba. Ahora sale sin la columna del bloque, y
  las preguntas del bloque conservan la suya. La columna vuelve en la Etapa 3.
- **Planes nuevos, con el visto bueno del usuario:**
  - **Etapa 2, «Cargar renglones»** (`docs/plans/2026-11-07-renglones-etapa-2-implementacion.md`, 10 tareas): `Row` en
    los valores y las referencias, con migración; la captura desde la app y por WhatsApp, con huella y replay; la
    corrección con sus ítems; en el front, la tabla, los recuadros, los errores por ítem y «Mantener». Suma RG13-A y LD3-A.
  - **Etapa 3, «Ver los renglones»** (`docs/plans/2026-11-07-renglones-etapa-3-implementacion.md`, 9 tareas): la columna
    del bloque, «Filtros» por algún ítem con sus conteos, la vista rápida, la pantalla completa, la tarjeta del chat, la
    hoja del Excel y la tabla del PDF. El modelo de lectura se acordó con la Etapa 2: `Row` es un entero de 0 a 99, con
    0 fuera del bloque.

## 2. WhatsApp: vista oficial, estado de la publicación, nombre del Flow y mensaje

- **La vista oficial interactiva (VO1-A).**
  - El enlace a la vista de Meta arranca en modo interactivo, en la primera pantalla y con opciones de ejemplo. Lleva las
    primeras 20 opciones activas de cada pregunta de catálogo, en orden y en la cultura pedida, con los filtros fijos y
    sin los filtros por persona ni por otra respuesta. Antes las listas llegaban vacías y «Continuar» no avanzaba.
  - El enlace no pasa de 6000 caracteres escapados: si no entra, cada lista lleva menos opciones.
  - Meta decodifica `flow_action_payload` dos veces, así que el `%` de una opción («2 4 D 100%») hay que escaparlo dos veces.
  - La política de seguridad suma `https://business.facebook.com` a `frame-src`. Sin eso, el iframe está bloqueado en
    producción; en desarrollo se veía porque Vite no aplica la política.
  - El marco mide 480 × 840 (30 × 52,5 rem): por debajo, Meta muestra sus propias barras. La página se achica entera, sin
    barras, para entrar en la ventana.
  - El front usa el enlace del backend tal cual.
  - Commits: backend `da1aa3e4`, `94b20c30`, `6661851c` y `ff19c9c5`; front `cc2d09b5`, `142bc557`, `235efed0`, `879338a4`
    y `de3f6dd3`.
- **El estado, una vez (EP1-A).**
  - El estado de la publicación queda en el encabezado del editor. Se va la zona «Publicando · versión N».
  - «Publicar en WhatsApp» apagado dice su motivo: en curso, sin confirmar, en revisión o sin permiso.
  - «Consultar publicación» aparece en el encabezado sólo cuando hace falta, y «Repetir el mismo intento», cuando WhatsApp
    no recibió el intento.
  - La regla vale para toda la app. En Información del negocio, la explicación pasa a ser el motivo de «Guardar» apagado;
    en la propuesta de un catálogo, el aviso dice quién la pidió y cuándo; en la baja de un formulario, «Lo dio de baja
    Ana el …».
  - Commits: front `fae44ed1`, `884d14a1`, `620b15a7` y `de3f6dd3`.
- **El nombre del Flow en Meta (backend `40beb3b6`).**
  - Los Flows nuevos se crean como «Nombre del formulario · v2 · es · ab63a1f9», en lugar de `form_<operación>_<idioma>`.
    Llevan el nombre en el idioma del Flow (o el original), la versión, el idioma y el final del id de la publicación,
    porque Meta exige un nombre único en la cuenta.
  - Hasta 80 caracteres, cortados con «…»; sólo letras, dígitos, espacios y puntuación común, en NFC.
  - La versión viaja fuera de la serialización del pedido, así que las huellas anteriores no cambian, y los Flows ya
    publicados conservan su nombre.
  - Confirmado con Meta.
- **El mensaje que acompaña un formulario.**
  - Sin texto del operador, el Flow sale con «Completá el formulario «<nombre>».», con el nombre en la cultura de la sesión
    (hasta 120 caracteres).
  - Las ofertas del asistente dicen «Te preparé el formulario «<nombre>» para que lo completes.».
  - En el diálogo «Enviar formulario», el campo propone ese texto apenas se elige un formulario, sin pisar lo que el
    operador ya escribió.
  - Commits: backend `f06ff28b` y `06bfd286`; front `ffae0f36`.

## 3. Elegir primero por un campo (LD1-A a LD3-A), en programación

Decididas el 2026-11-07, con su plan en `docs/plans/2026-11-07-elegir-primero-por-un-campo.md` y su diseño en
`ArquitecturaBaseFront/docs/design/carga-desde-la-app.md`.

- **LD1-A.** Una pregunta de catálogo pide primero el valor de un campo del mismo catálogo (por ejemplo «Rubro») y lista
  sólo sus opciones, como «75 opciones de Herbicidas». Es un ajuste de la pregunta: no hace falta otro catálogo ni otra
  pregunta, y se guarda sólo la opción. Reglas por defecto:
  - sirven los campos activos de texto u opción de catálogo, uno por pregunta;
  - no se combina con «Permitir elegir varias»;
  - las opciones sin dato van al final como «Sin <campo>»;
  - un solo valor viene elegido;
  - si el valor cambia, la opción se limpia.
- **LD2-A.** En WhatsApp, las dos listas van en la misma pantalla, y la acción `update_data` (Flow JSON 6.0 o más) llena la
  segunda en el teléfono, con hasta 200 opciones por valor.
- **LD3-A.** Con renglones, si las dos preguntas están en el bloque, se usa la respuesta del mismo renglón; una pregunta de
  afuera vale para todos. Publicar frena si una pregunta de afuera depende de una del bloque.
- **La prueba con Meta anduvo (backend `598a0f5e`).** `update_data` llena la lista desde el mensaje, y cambiar el valor
  limpia la opción. Un desplegable obligatorio no puede ir oculto, porque `required` va atado a `visible`. El `navigate`
  pasa los datos de la pantalla de renglón. Un mensaje de 34,7 KB, con 331 opciones, salió y anduvo en el celular.
- Al escribir esto, el código estaba en curso, sin commitear.

## 4. Rendimiento: la revisión de 31 mejoras

El usuario revisó la app contra 31 consejos de rendimiento, con un chat por consejo.

| Veredicto | Mejoras |
| --- | --- |
| **Ya estaba** | Llamadas duplicadas, debounce, paginado, cola de trabajos, Redis, imágenes diferidas, pedidos en paralelo, JavaScript por página, sólo las columnas necesarias, pool de conexiones y fuentes. |
| **Aplicadas ahora** | Compresión y caché del navegador; consultas N+1 de permisos; índices; varias instancias (probes obligatorios); esqueletos de carga; celulares y redes lentas (con una guía). |
| **Por elegir** | Dependencias sin usar del front (devtools de React Query y Leaflet); plazo propio del correo y tope total de la vista oficial; re-renders del chat de WhatsApp y formateadores de fecha y número. |
| **Para producción** | Exportador de trazas (OpenTelemetry), región y red privada del despliegue, y medir a los usuarios reales. |
| **No valen la pena** | WebP/AVIF, CDN, IA token a token, interfaz optimista, render en el servidor, scripts de terceros y listas virtualizadas. |

Lo aplicado:

- **Compresión y caché (backend `7d5b4906`).**
  - La Api comprime con Brotli o gzip el SPA y el JSON, también por HTTPS (`AddResponseCompression` con `EnableForHttps`).
  - Lo de `/assets`, con los nombres con hash de Vite, se cachea un año como `immutable`. El resto va `no-cache`, incluido
    `silent-renew.html`, que se abre en cada carga.
  - Un proxy o CDN delante tiene que respetar `Cache-Control` y `Vary: Accept-Encoding`.
- **Permisos (backend `68c385d7`).** `PermissionAuthorizationHandler` lee los permisos una sola vez por requisito, en lugar
  de hacer una consulta por cada alternativa de `HasAnyPermission`.
- **Índices.** La migración `20261107004318_AddRecordAndLoginAuditIndexes` (backend `68c385d7`) agrega tres:
  - `IX_LoginAudits_UserId_OccurredAtUtc`, que reemplaza a `IX_LoginAudits_UserId`, para el último ingreso de `GET /api/me`;
  - `IX_BusinessFormRecords_Current_DefinitionId_Number`, filtrado por `"ReplacedByRecordId" IS NULL`, para la tabla, el
    resumen y la última respuesta;
  - `IX_BusinessFormRecords_DefinitionId_ActorUserId_Number`, para quien sólo carga y la faceta por persona.
- **Probes obligatorios (backend `6f9f2bc3`).** El deploy exige Startup y Liveness en `/alive` y Readiness en `/health`, y
  corta antes de subir la imagen si falta alguno. Readiness lleva 5 s, porque Redis puede tardar 3, y el Startup cubre el
  seed. Falta cargarlos en la Container App real.
- **Esqueletos de carga (front `d79f8f35`).** El diseño está en `visual-baseline.md`, «Esqueletos de carga — 2026-11-06».
  - **ES1-A:** `DataTable` en su primera carga dibuja el encabezado de siempre y el aro centrado en el alto de ocho filas,
    para que el alto no cambie cuando llegan.
  - **ES2:** `PageLoading` es la espera de las rutas que se cargan aparte y de los permisos pendientes: la banda de `Page`
    con su alto, el título como bloque de carga y tres barras.
  - **ES3:** `FormSkeleton` dibuja las zonas del formulario en Configuración, Apariencia, Inteligencia artificial e
    Información del negocio.
  - Se descartaron las filas esqueleto (ES1-B) y la línea que corre (ES1-C).
- **Celulares y redes lentas (backend `4fea6bef`).** La guía nueva `docs/guides/probar-en-el-celular.md` abre la app en un
  celular Android por reenvío de puertos USB y limita la red y la CPU desde DevTools. El túnel sólo publica la Api para
  Meta: no sirve para abrir la app.

## 5. Documentación

- `docs/features/formularios.md` y `docs/features/whatsapp.md`: la vista oficial interactiva, el nombre del Flow y el mensaje
  que nombra el formulario.
- `docs/architecture/backend.md`: la compresión y la caché del SPA.
- `docs/guides/`: la guía nueva del celular; `despliegue.md`, con los probes y la caché; y `validar-un-cambio.md`.
- `docs/deploy/azure-setup.md` y `azure-dev.md`: los probes dejan de ser opcionales.
- No hay ADR nuevo.
