# Solicitudes de ingreso con registro cerrado

Estado: **propuesta sin implementar** (2026-09-30; revisada contra el código el 2026-10-12).
No hay entidad, rutas, permisos ni pantallas de solicitudes. Antes de programar, el usuario
elige el alcance y los tableros.

## Qué resuelve

Con el registro en `InviteOnly`, una persona sin cuenta no tiene cómo pedir acceso. La propuesta:
que pueda solicitarlo y que un administrador decida. Solicitar no crea una cuenta ni permite
entrar. Una instalación por empresa; no introduce multitenencia.

## Recorrido propuesto

1. **Ingreso cerrado:** «El acceso a esta empresa requiere aprobación. Si todavía no tenés acceso,
   solicitá ingresar». El mensaje no depende de si el correo existe. Con solicitudes apagadas:
   «Para ingresar, pedile una invitación a un administrador», sin botón sin destino.
2. **Solicitar:** nombre y correo, verificados con un código de propósito propio o con Google.
   No reutiliza el código de login ni crea la cookie de Identity.
3. **Presentar:** recién después de verificar; «Tu solicitud está pendiente de aprobación» y
   confirmación por correo. Repetir para el mismo correo muestra el pedido existente, sin avisos
   duplicados.
4. **Revisar:** Administración → Gestión de usuarios → Solicitudes de ingreso; pendientes primero,
   detalle y modal de aprobar/rechazar; acciones con íconos y tooltips.
5. **Aprobar:** crea la cuenta con el contacto verificado y rol User; manda un enlace al ingreso con
   el correo precargado. Ingresar vuelve a exigir verificar identidad.
6. **Rechazar:** registra quién y cuándo, avisa y no crea cuenta. Propuesta: nuevo pedido a las 24 h.

Primera versión con correo y Google; WhatsApp después, por los puertos del módulo.

## Decisiones propuestas

- Ajuste nuevo `AllowAccessRequests`, independiente de `Open`/`InviteOnly`, apagado en
  instalaciones existentes; `login-methods` publica `accessRequestsOpen`.
- Estados `Pending`, `Approved`, `Rejected`. La verificación previa tiene su caducidad y no
  aparece al administrador.
- Datos: correo normalizado, nombre, idioma, verificación, fechas `Utc`, decisión y quién resolvió.
- Permisos `accessrequests.read` y `accessrequests.manage`. Rutas a fijar al implementar:
  pedir código y presentar en `/account/access-requests…` (anónimas, con límites por IP y
  destino); listar, ver, aprobar y rechazar en `/api/access-requests…`.
- Respuesta pública uniforme para destinos desconocidos o registrados. Una cuenta deshabilitada
  conserva su mensaje y no se reactiva con una solicitud.
- Códigos y comprobantes de un solo uso, con propósito, caducidad e intentos; nunca en logs.
  Consultar el propio pedido usa un comprobante corto, sin permisos ni tokens de sesión.
- Aprobar y rechazar toman lock del pedido; gana una sola resolución. Cuenta y estado en un solo
  límite. Al aprobar se vuelve a comprobar si el contacto ya tiene cuenta (invitación o registro
  durante la espera): no duplicar, no cambiar roles, no reactivar.
- Avisos por la cola transaccional de correo ([ADR 0015](../decisions/0015-colas-transaccionales-cifradas.md)):
  se encolan en el mismo límite y salen después del commit; un envío fallido no revierte la
  aprobación y queda visible.
- Google/OIDC conservan una intención explícita de solicitud, separada de ingreso y registro, con
  destino de retorno validado.

## Antes de implementar

- Corregir el recorrido actual, que no requiere esta funcionalidad: con `InviteOnly`, un ingreso
  con Google de un correo sin cuenta devuelve `Auth.Account.NotRegistered`
  (`ExternalLoginService`) y el front muestra «Elegí Crear cuenta para registrarte»
  (`login.errors.notRegistered`), aunque el registro esté cerrado.
- Tableros de solicitud, verificación, resultado, listado/detalle y modales, con sus estados
  (carga, error, vacío, duplicado, rechazo, cuenta existente o deshabilitada, sin permiso, móvil
  y teclado), elegidos por el usuario.
- Decidir la retención de pedidos y el tratamiento de avisos fallidos antes de producción.

Se implementa con [agregar un área](../guides/agregar-un-area.md), [permiso nuevo](../guides/permiso-nuevo.md)
y [migración](../guides/migracion.md); al cerrar, las reglas pasan a [identidad](../features/identidad.md)
y [administración](../features/administracion.md).
