# Plan de configuración general

Estado: **HISTÓRICO · completado el 2026-09-30**.
Fecha: 2026-09-30. Diseño funcional: [Configuración general](../../specs/2026-09-30-configuracion-general-design.md).
Diseño autorizado por el pedido de implementación del usuario. Laboratorio retirado al promoverlo.

## Resultado esperado

Quien tiene `settings.manage` puede guardar idioma, zona horaria y tamaño inicial de los
listados y el modo de registro en cuatro destinos del menú, con guardado independiente.
Los valores se persisten sin reiniciar y se usan al
crear cuentas y abrir pantallas. Los perfiles existentes y las fechas UTC se conservan.

Este plan amplía el área Settings; no cambia las dependencias de capas, MVC, el límite de
transacción ni la regla de Redis. No crear un almacén genérico de configuración, un rol nuevo,
Minimal APIs de negocio ni un segundo formateador de fechas.

## Orden de trabajo

1. Revisar y elegir el diseño local y el alcance. Resolver su promoción/sincronización con
   el fundamento visual del front antes de escribir la pantalla productiva.
2. Implementar backend, con tests en rojo antes de cada regla.
3. Implementar consumidores de los valores, con pruebas de precedencia antes del cambio.
4. Implementar la pantalla aprobada y completar sus recorridos.
5. Verificar integración real, actualizar documentación vigente y retirar el laboratorio.

## 1. Modelo y persistencia

- [x] Agregar a `Domain/Settings/SystemSettings` DefaultCulture, DefaultTimeZoneId,
  DefaultPageSize y Revision. Los defaults de nuevas instalaciones son es, Buenos Aires y 20;
  el modo de registro inicial conserva su configuración existente.
- [x] Definir reglas puras para es/en y tamaños 10/20/50/100. La zona IANA se verifica con la
  validación/puerto que usa el perfil; Domain no toma dependencias de sistema operativo o web.
- [x] Agregar errores propios en `Domain/Settings/SettingsErrors` y traducciones es/en.
  La revisión esperada inválida es validación; una revisión válida que quedó vieja es conflicto.
- [x] Extender `SystemSettingsConfiguration` y crear migración siguiendo
  [migracion.md](../../guides/migracion.md). Backfill explícito para la única fila existente;
  no actualizar ningún perfil ni convertir fechas guardadas.
- [x] Mantener IAuditable y sus interceptores. No setear sus columnas a mano.
- [x] El seed crea campos nuevos solamente si no existe la fila; no pisa preferencias del panel.

Pruebas: tamaños e idiomas admitidos/rechazados, valores iniciales, preservación del modo
existente, migración sobre una base anterior y seed repetido sin sobrescrituras.

## 2. Lecturas, Redis y conflicto de edición

- [x] Conservar `ISystemSettingsReader.FindRegistrationModeAsync` y su caché de enum.
  Crear una lectura tipada FindPresentationAsync para cultura, zona y tamaño; no cambiar el
  tipo serializado bajo la clave de registro, porque pueden coexistir réplicas viejas y nuevas.
- [x] Usar una clave lógica nueva `settings:presentation`, TTL de 60 segundos, con
  `RedisCache.GetOrCreateInOwnScopeAsync`. Fuente PostgreSQL, proyección AsNoTracking y token
  respetado. Ausencia de fila: respaldos de presentación; registro sigue cerrado.
- [x] `ISystemSettingsCache` descarta registro y presentación después del commit exitoso.
  El seed descarta ambas después de su commit. Sin caché local ni fallback para una caída Redis.
- [x] Agregar un lock de fila para la configuración en `ISystemSettingsRepository.LockAsync`.
  Dentro de la única transacción, tomarlo **antes** de cargar la entidad y comprobar Revision.
  Usar SELECT FOR UPDATE en la fila singleton; si no existe, responder el NotFound actual.
- [x] En una escritura confirmada incrementar Revision. Dos PATCH con la misma expectedRevision
  no pueden guardar ambos. El segundo responde 409 sin modificar otros campos.

Pruebas en dos hosts con PostgreSQL y Redis real: lectura desde el otro host, descarte, TTL,
rollback sin invalidación, fábrica tardía, seed, commit con descarte fallido y dos ediciones
simultáneas. Seguir [agregar-cache.md](../../guides/agregar-cache.md).

## 3. Servicios y borde HTTP

- [x] Extender `SystemSettingsResponse` con los campos y revision. El panel sigue leyendo la
  fila real por repositorio, sin usar la proyección pública cacheada.
- [x] Conservar UpdateSystemSettingsRequest, UpdateAsync y el PUT legado de registro. Agregar
  UpdateSectionAsync y un request tipado para el PATCH nuevo; cada método público que escribe
  tiene su único `ExecuteInTransactionAsync(..., OnSuccess, ...)`.
- [x] PATCH `/api/settings` acepta exactamente un campo entre defaultCulture, defaultTimeZoneId,
  defaultPageSize y registrationMode, más expectedRevision obligatoria. Distinguir omisión, null
  y valor en su contrato HTTP; rechazar null, campos desconocidos, ningún ajuste o más de uno.
  Mapear a mano; no usar JSON Patch genérico ni una bolsa de pares clave/valor.
- [x] El cliente nuevo manda solo el ajuste de su pantalla. El PUT legado sigue aceptando
  `{ registrationMode }` y nunca modifica presentación; también incrementa Revision para que
  un borrador nuevo detecte esa edición. Testear que guardar Idioma no guarda un borrador de Zona.
- [x] Exponer `GetPresentationAsync` en `ISystemSettingsService` y su Response mínimo.
  Agregar GET `/api/settings/presentation` anónimo en MVC, por ese servicio. Mover el atributo
  de permiso del controller a cada acción administrativa GET/PUT/PATCH; marcar el GET público con
  AllowAnonymous sin dejar ninguna acción administrativa expuesta.
- [x] Agregar GET público y PATCH al inventario, OpenAPI y pruebas. Mantener 401/403
  en GET/PUT/PATCH administrativos; el GET público no contiene modo de registro, auditoría ni revision.

Pruebas en rojo: lectura autorizada, 401/403, contrato público mínimo, cada validación,
actualización de un solo tema, conflicto 409, PUT legado que preserva campos nuevos, null explícito,
sin fila y una única invalidación después del commit.

## 4. Precedencia en ingreso, perfiles y API

- [x] Agregar un adaptador HTTP de cultura predeterminada en Api/Localization. El proveedor
  Accept-Language conserva primera prioridad, incluyendo q y variantes regionales soportadas.
  El proveedor posterior obtiene el default por un servicio de Application, sin EF en Api.
- [x] No abrir una transacción al resolver cultura; no consultar el controller público por HTTP
  desde el servidor ni introducir una dependencia circular entre localización y validación.
- [x] Hacer explícita la cultura/zona de una cuenta nueva en los puertos de creación de usuarios
  y `ApplicationUser.Create`; los repositorios no inventan defaults de negocio.
- [x] Aplicar defaults a alta administrativa, registro por código, Google y administrador inicial.
  Respetar el idioma elegido explícitamente en el ingreso. Revisar adaptadores de cada canal
  opcional sin nombrarlos desde el núcleo.
- [x] Restaurar una cuenta preserva sus preferencias; una cuenta existente no se reconfigura
  en su próximo ingreso. Los emails siguen usando el idioma del perfil.
- [x] Separar la elección pública explícita del idioma recordado al sincronizar una cuenta.
  El navegador sin elección pública toma el default del sistema y no una preferencia histórica
  de un usuario que ya cerró sesión. La cuenta vuelve a ganar al iniciar sesión.

Pruebas: default general en un navegador nuevo; selección pública explícita; header es-AR,
preferencias q, header ausente/no compatible; cuentas nuevas por cada camino; perfiles
existentes/restaurados intactos; zona IANA válida/rechazada y fechas UTC sin cambios.

## 5. Paginación coherente

- [x] Crear consumo compartido de presentación en `shared/api`, no importar settings desde
  otra feature. Su consulta puede ejecutarse sin sesión y sin settings.manage.
- [x] Extender `usePagination` para recibir/resolver el default general. Esperar esa lectura
  antes de lanzar un listado para no hacer una primera consulta de 20 seguida de otra distinta.
- [x] Fijar el tamaño efectivo en URL al abrir una colección, con una actualización replace.
  El tamaño explícito gana y no cambia cuando se refresca la configuración general.
- [x] Agregar selector de tamaño al pie Pagination compartido; `setPageSize` actualiza tamaño y
  reset de página en una sola llamada de `useQueryUpdate`. Conservar sort, search y filtros.
- [x] Aplicar a usuarios y roles paginados. No paginar el catálogo GET `/api/roles`.
- [x] API sin pageSize también usa el default general: conservar la presencia del parámetro
  en el controller y resolverlo mediante ISystemSettingsService antes de mapear el pedido validado por Application. Un tamaño
  explícito de 1..100 conserva la compatibilidad del API, aunque la UI ofrezca cuatro opciones.
  No invalidar silenciosamente valores del API que hoy son válidos.
- [x] Preservar PagedRequest.MaxPageSize=100, campos ordenables, desempate único y PagedResult.
  El constante de 20 queda como respaldo técnico, no como fuente del default de la instalación.

Pruebas: URL sin tamaño, con tamaño explícito, apertura/recarga/Atrás, cambio general mientras
se recorre página 3, selector que vuelve a 1, cero resultados y tamaños inválidos. Backend y
front deben producir el mismo tamaño cuando no se especifica.

## 6. Pantallas de cada tema

- [x] Configuración se convierte en grupo del menú con Idioma, Zona horaria, Listados y Registro.
  Definir sus rutas /configuracion/{idioma,zona-horaria,listados,registro}, redirigir el acceso
  anterior a Idioma y conservar settings.manage en rutas y destinos. Verificar migas y móvil.
- [x] Cada Page tiene título del tema, estado de cambios y acciones; el cuerpo contiene solo su
  ajuste. Selectores para idioma/zona/tamaño; RadioGroupField para las opciones del registro.
  Usar recursos settings es/en y tokens comunes, sin tarjeta alrededor de un único campo.
- [x] Borrador del tema, estado de cambios, Descartar, guardado independiente y relectura confirmada.
  No guardar el borrador en URL ni localStorage.
- [x] Confirmación del cambio de registro y guarda compartida al salir con cambios.
- [x] Vista previa con `formatDateTimeInZone`; no cambiar el idioma/zona de la sesión al editar
  los defaults. Selector de zona completo, manteniendo opciones reconocidas por el backend.
- [x] Carga, lectura fallida, fila ausente, errores de campo, sin permiso, guardando, éxito,
  conflicto y error de guardado ambiguo. Conservar el borrador hasta una elección explícita.
- [x] Éxito, error operativo, warning e info al Toaster global abajo a la derecha. Validaciones
  junto al campo; decisiones en ConfirmDialog; carga fallida como estado no operable. Elegir
  un único dueño del toast para no duplicar el manejador global de QueryClient. Duraciones,
  cierre y acciones siguen las premisas comunes del front; no usar Banner para esos resultados.
- [x] Tras 409 mostrar valores actuales y el borrador para revisar diferencias. Tras un 500
  releer y comparar antes de volver a enviar; no suponer rollback por una falla de Redis.

Pruebas con MSW: recorridos completos en es/en, cancelación, doble envío, estado conservado,
conflicto y 500 con escritura ya confirmada; guardado que solo afecta el tema activo y navegación
entre temas con borrador. Revisar teclado, foco de diálogos, toasts y 320/390/1440 px.

## 7. Cierre del desarrollo

- [x] `dotnet build ArquitecturaBase.slnx` sin advertencias y `dotnet test` completo con Docker.
- [x] `npm run build`, `npm run lint` y `npm run test` limpios.
- [x] Si se levantó la aplicación con Aspire, `aspire stop` al terminar de probar.
- [x] Actualizar administración, identidad, localización/paginación de backend.md y la fuente
  visual versionada; registrar alcance y valores elegidos. No presentar esta propuesta como vigente.
- [x] Eliminar el laboratorio cuando se promueva la decisión; conservar evidencia de revisión
  y diseño en los documentos de la implementación. No crear rama ni push sin pedido explícito.

## Verificación previa de la etapa de diseño

Tablero aislado: TypeScript y lint sin advertencias, exportación portátil y paridad de 69 claves
de español/inglés. Revisado en navegador a 1440 y 320 px: navegación por tema, guardado en memoria,
salida con cambios, confirmación de registro, error en Toaster con Revisar y comparación del valor
guardado con el borrador. El contenido y la notificación no desbordan los 320 px.

El build, lint y los 700 tests existentes del front pasaron durante aquella etapa. El backend
y su integración se implementaron después de la autorización del usuario; el cierre figura abajo.


## Cierre verificado

Backend: 2015 pruebas, incluidas integración con Docker, migración, autorización, precedencia,
conflicto entre dos escrituras y observación desde otro host con Redis real; build sin advertencias.
Frontend: build, lint y 714 pruebas, con recorridos de guardado por tema, campos, conflictos,
500 después de commit, navegación y resultados tardíos. Capturas de componentes productivos a
1280 y 320 px, con API simulada solo para la revisión visual. No se inició Aspire.

Implementación reutiliza Page, NativeSelect, FormField, RadioGroupField, ConfirmDialog,
formatDateTimeInZone, el único Toaster y la guarda compartida. El emisor común y la fábrica de
QueryClient ya existen; esto no cierra la migración de mensajes de las pantallas heredadas.
