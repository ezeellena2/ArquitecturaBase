# Solicitudes de ingreso con registro cerrado

**Estado:** propuesta para revisión; no implementada. **Fecha:** 2026-09-30.

Permitir que una persona pida acceso a la empresa cuando el registro está cerrado y que un administrador decida si la incorpora. Solicitar acceso no crea una cuenta ni permite entrar. El proyecto actual tiene una sola instalación y no modela empresas: el pedido corresponde a esa instalación, sin introducir multitenencia.

## Problema actual

`GET /account/login-methods` ya informa `registrationOpen`. La portada oculta Crear cuenta cuando está cerrado, pero `Auth.Account.NotRegistered` todavía puede sugerir registrarse. Con Google, el rechazo ocurre después de comprobar el correo. En el ingreso por código, InviteOnly no envía códigos a destinos desconocidos; por eso no alcanza con agregar un formulario que reutilice el envío de login.

Las invitaciones del administrador y las solicitudes de una persona son recorridos distintos. Una cuenta ya invitada entra con su contacto exacto y verifica su identidad. Una solicitud pendiente todavía requiere una decisión del administrador. Una cuenta deshabilitada conserva ese estado: presentar una solicitud no la reactiva.

## Recorrido propuesto

1. **Ingreso cerrado.** Mostrar: «El acceso a esta empresa requiere aprobación. Si todavía no tenés acceso, solicitá ingresar». Los usuarios existentes conservan Google, correo y WhatsApp según la configuración. El mensaje no depende de detectar si un correo existe.
2. **Solicitar ingreso.** Pedir nombre y correo. Verificar el correo con un código específico para esta solicitud, o con Google si está habilitado. No reutilizar un código de login ni crear la cookie de Identity para una persona sin acceso.
3. **Enviar la solicitud.** Después de verificar el contacto, crearla y mostrar «Tu solicitud está pendiente de aprobación». Enviar una confirmación al solicitante. Solicitudes repetidas para el mismo correo muestran el pedido existente, sin duplicar ni repetir avisos.
4. **Revisión.** En Administración → Gestión de usuarios → Solicitudes de ingreso, listar pendientes primero. El administrador abre el detalle y aprueba o rechaza mediante un modal. El listado usa columnas y acciones con íconos y tooltips; evita agregar etiquetas junto al nombre.
5. **Aprobación.** Crear una cuenta con el contacto verificado y el rol User. La asignación de otros roles queda en la edición habitual del usuario. Mandar un enlace que lo lleve al ingreso con su correo precargado; iniciar sesión vuelve a requerir comprobar identidad.
6. **Rechazo.** Registrar quién decidió y cuándo; avisar al solicitante. No crear una cuenta. Propuesta inicial: permitir otro pedido después de 24 horas, sujeto a los límites de envío y de solicitudes.

Para la primera versión se propone correo y Google. WhatsApp se agrega mediante los puertos del módulo opcional, sin que el núcleo lo nombre. El ingreso actual por WhatsApp permanece disponible para cuentas existentes.

## Configuración y estados

- Conservar los modos `Open` e `InviteOnly`. Agregar `AllowAccessRequests` como ajuste independiente, inicialmente apagado en instalaciones existentes. El administrador puede habilitarlo cuando quiera recibir pedidos.
- Publicar `accessRequestsOpen` en `login-methods`. Con registro cerrado y solicitudes apagadas, explicar «Para ingresar, pedile una invitación a un administrador»; no dibujar un botón sin destino real.
- Estados de negocio: `Pending`, `Approved`, `Rejected`. La verificación previa tiene su propia caducidad y no aparece como una solicitud pendiente para el administrador.
- Guardar correo normalizado, nombre, idioma, verificación, fecha de solicitud, decisión y administrador que la resolvió. Las fechas terminan en `Utc`; los cambios tienen auditoría.
- El estado completo solo lo puede consultar el solicitante después de verificar su contacto o alguien con permiso de lectura. Una dirección escrita en un formulario no da acceso al pedido de otra persona.

## Contratos y controles

Rutas propuestas, para fijar al implementar:

| Acción | Contrato propuesto | Acceso |
|---|---|---|
| Pedir verificación | `POST /account/access-requests/code` | Anónimo, con límites por IP y destino |
| Verificar y presentar el pedido | `POST /account/access-requests` | Código de propósito específico o comprobante de Google |
| Consultar el pedido propio | `GET /account/access-requests/status` | Comprobante temporal restringido al pedido |
| Listar y consultar detalle | `GET /api/access-requests`, `GET /api/access-requests/{id}` | `access-requests.read` |
| Aprobar o rechazar | `POST /api/access-requests/{id}/approve`, `/reject` | `access-requests.manage` |

- La respuesta pública al pedir verificación conserva una forma uniforme para destinos desconocidos o ya registrados; no publica nombres, roles ni estados internos. La excepción vigente para cuentas deshabilitadas conserva el mensaje de deshabilitación y no permite crear un pedido para eludirla.
- Los códigos o comprobantes son de un solo uso, con propósito de solicitud, caducidad e intentos limitados. Un comprobante de Google verifica la dirección; no concede acceso a la empresa. No transportar códigos, enlaces ni tokens en logs.
- Un pedido nuevo requiere verificación reciente. Consultar el propio necesita un comprobante corto y limitado a esa operación; no recibe permisos ni tokens de sesión de la aplicación.
- Aprobar y rechazar toman un lock del pedido. Solo una resolución puede ganar; el resto recibe el resultado vigente o un conflicto traducido. La creación de la cuenta y el cambio de estado se confirman en una sola transacción.
- Al aprobar, volver a comprobar si el contacto pertenece a una cuenta, incluso si el registro se abrió o llegó una invitación durante la espera. No duplicar cuentas, cambiar roles existentes ni reactivar cuentas deshabilitadas.
- Los avisos salen después del commit. Una falla de envío no revierte una aprobación ni permite aprobar otra vez; debe quedar visible y admitir reintento. Reutilizar la infraestructura de avisos cuando cubra estas garantías.
- Revisar las rutas de Google y OIDC para conservar una intención explícita de solicitud, separada de ingreso y registro. El servidor valida el destino de retorno; no aceptar redirects arbitrarios.

## Orden de implementación

### 1. Corregir el recorrido actual

- [ ] Adaptar los textos de portada, login, registro cerrado, callback de Google y verificación de código al ajuste vigente. Con registro cerrado nunca sugerir Crear cuenta como solución.
- [ ] Cuando todavía no existe el formulario de solicitudes, mostrar el pedido de invitación al administrador. Publicar Solicitar ingreso recién cuando su recorrido completo esté disponible.
- [ ] Cubrir los cambios de modo con un código o callback en vuelo y el fallo de lectura de la configuración; no ofrecer una acción que el servidor no permite.

### 2. Dibujar las pantallas antes de programarlas

- [ ] Preparar los tableros del sistema visual para la solicitud, su verificación/resultado y el listado/detalle de revisión.
- [ ] Incluir carga, error, vacío, duplicado, pendiente, rechazo, cuenta existente o deshabilitada, sin permiso, móvil, teclado y los modales de decisión. Elegirlos con el usuario antes de implementarlos.

### 3. Implementar el backend con TDD

- [ ] Agregar modelo y errores en Domain; servicios, contratos, validadores y puertos en Application; repositorios, configuración EF y migración en Infrastructure; controllers y contratos HTTP en Api.
- [ ] Usar un límite de `IUnitOfWork` por caso de uso; guardar consumo de comprobantes e intentos fallidos cuando corresponda. No anidar transacciones ni mover EF a los servicios.
- [ ] Seguir las guías de área nueva, permiso nuevo y migración. Registrar los permisos y sus textos en ambos idiomas; actualizar seed, inventario de rutas y OpenAPI.
- [ ] Probar propósito/caducidad, intentos, deduplicación, solicitud verificada, consultas ajenas, doble resolución, invitación o registro concurrente, cuenta deshabilitada, idioma y envío fallido después del commit.

### 4. Implementar el front y los avisos

- [ ] Integrar los tableros elegidos con componentes compartidos, formularios y tooltips. Conservar PKCE, retorno OIDC y navegación dentro del SPA donde corresponde.
- [ ] Declarar las rutas y los ítems del menú con sus permisos. Mantener filtros y paginado en la URL e invalidar usuarios, solicitudes y conteos después de una decisión.
- [ ] Traducir toda la interfaz en `auth`, `accessRequests` y `settings`, en español rioplatense e inglés. Backend: errores/validaciones en sus dos `.resx`; correos de confirmación y resolución en sus recursos y plantillas.
- [ ] Cubrir con MSW el recorrido completo, permisos, decisiones canceladas, cambios de idioma y fallas de red. Verificar con navegador que ninguna acción recargue innecesariamente la página.

### 5. Cerrar la entrega

- [ ] Build sin advertencias, todas las pruebas del backend incluyendo integración con Docker, y build/lint/test del front.
- [ ] Revisar a 320 px y escritorio, con teclado y en ambos idiomas. Detener Aspire al terminar las pruebas.
- [ ] Actualizar las reglas de identidad y administración y la documentación de despliegue. Definir la retención de pedidos y el procedimiento para tratar avisos fallidos antes de producción.

## Decisiones propuestas para la primera versión

Una instalación, correo/Google, solicitud verificada antes de aparecer en administración, aprobación manual con rol User y avisos por correo. Las solicitudes se habilitan aparte del registro abierto. La pantalla de revisión será la bandeja del administrador; no se requiere mandar un correo a todos los administradores por cada pedido. Integrar WhatsApp y notificaciones adicionales puede ser una etapa posterior.
