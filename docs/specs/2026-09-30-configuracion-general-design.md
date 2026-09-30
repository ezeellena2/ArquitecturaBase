# Configuración general del sistema

Estado: **implementado; desarrollo autorizado por el usuario después del diseño y el plan**. Fecha: 2026-09-30.

Pedido: permitir que quien administra la instalación defina sus valores generales desde
Configuración, siguiendo el orden diseño → plan → desarrollo. El acceso depende de
`settings.manage`, que Admin ya recibe; no se agrega un rol Owner ni una comprobación por rol.

## Punto de partida del diseño

| Tema | Comportamiento previo | Evidencia |
| --- | --- | --- |
| Registro | Open o InviteOnly, editable en `/configuracion` | `Domain/Settings/SystemSettings.cs`, `SettingsController` y `SettingsPage` |
| Idioma de la cuenta | Español o inglés, editable en el perfil y el menú del usuario | `ApplicationUser.Culture`, `useLanguagePreference` |
| Idioma público/API | El navegador recuerda una elección; sin ella arranca en español. API lee Accept-Language y tiene español como respaldo | `shared/i18n/index.ts`, `LocalizationExtensions` |
| Zona horaria | Preferencia del perfil; las cuentas nuevas parten de America/Argentina/Buenos_Aires | `ApplicationUser.DefaultTimeZoneId`, `ProfilePage` |
| Paginación | 20 filas por defecto y un máximo técnico de 100; el tamaño explícito vive en la URL | `PagedRequest`, `usePagination` |
| Fechas | Instantes UTC en almacenamiento y API; el front los muestra en la zona del perfil | `UtcDateTimeConverter`, `shared/lib/dateTime.ts` |

Antes de este desarrollo no había campos generales de idioma, zona horaria o tamaño de página en la fila de ajustes.

## Alcance de la primera entrega

| Campo visible | Propiedad | Valores | Valor inicial |
| --- | --- | --- | --- |
| Idioma predeterminado | DefaultCulture | es / en | es |
| Zona horaria predeterminada | DefaultTimeZoneId | Identificadores IANA válidos | America/Argentina/Buenos_Aires |
| Filas por página | DefaultPageSize | 10 / 20 / 50 / 100 | 20 |
| Registro abierto | RegistrationMode, existente | Open / InviteOnly | Lo que ya tenga guardado cada instalación |

El idioma define la traducción de interfaz, errores y validaciones. El formato regional de
fechas y números sigue los formateadores compartidos existentes; no se ofrece en esta entrega
un editor de formatos, moneda, textos o traducciones. La vista previa muestra una fecha realista
para que la diferencia entre idioma y zona horaria sea verificable antes de guardar.

Los identificadores del código y los logs siguen en inglés. UTC, el máximo de 100 filas,
las listas de campos ordenables, las transacciones y la seguridad son reglas técnicas,
no controles de esta pantalla. Elegir una zona horaria cambia la presentación de un instante,
no el instante guardado ni el vencimiento de un código.

## Precedencia y efecto de un cambio

- Sin sesión: elección explícita de idioma en ese navegador → idioma general → español como
  respaldo inicial mientras se carga la configuración. No usar la elección anterior de una
  cuenta como si fuera una preferencia global del sistema.
- API: un Accept-Language compatible prevalece. Sin una selección compatible, usar el idioma
  general. Respetar prioridades y variantes regionales como es-AR; no recortar el header a mano.
- Cuentas nuevas: idioma explícito elegido durante el registro, si existe; en caso contrario el
  idioma general. Zona horaria general al crear la cuenta. El alta administrativa sin una
  preferencia explícita también toma los valores generales.
- Cuentas existentes: conservar Culture y TimeZoneId. Cambiar el sistema no reescribe perfiles.
  Una restauración de cuenta conserva sus preferencias y no se trata como una cuenta nueva.
- Listados: pageSize explícito en URL → tamaño general. El máximo sigue siendo 100. Los filtros,
  orden y página conservan su contrato. El front manda el tamaño resuelto al backend.
- Cambiar el tamaño general no desplaza a quien ya está recorriendo un listado: al abrir un
  listado, fijar su tamaño efectivo en la URL. El valor nuevo rige para nuevas aperturas sin
  pageSize. Agregar un selector de tamaño en el pie compartido; cambiarlo vuelve a la página 1.
- Guardar no cambia el idioma ni la zona horaria del administrador conectado. La vista previa
  sí refleja el borrador. El aviso de éxito se muestra en el idioma de su sesión.

No se agregan campos nulos ni una opción «Usar configuración del sistema» al perfil existente.
Una futura herencia dinámica requeriría su propio diseño; hoy los valores se copian al crear.

## Menú, cabeceras y controles

Conservar navegación, barra superior, Page, Public Sans, controles de 30 px y paleta Jade.
Canvas #fff, navegación #194f3a, cabecera #f2f7f3, texto #202d26, borde #b4cebc y acción #176b4c;
son los tokens compartidos vigentes, no una paleta nueva para esta ruta.

Revisión pedida por el usuario: **un tema por pantalla**, con destinos en el menú existente de
Administración. Se descarta el formulario único que reunía idioma, zona, listados y acceso.

```text
Administración              Inicio / Configuración / Idioma
  Gestión de usuarios       Idioma          Descartar   Guardar cambios
  Configuración
    Idioma                  Idioma predeterminado
    Zona horaria            [Español                              v]
    Listados                Para pantallas públicas y cuentas nuevas.
    Registro
```

| Destino | Ruta | Contenido y control |
| --- | --- | --- |
| Idioma | /configuracion/idioma | Un selector es/en y una ayuda sobre su alcance |
| Zona horaria | /configuracion/zona-horaria | Selector del catálogo IANA y una vista previa de fecha/hora |
| Listados | /configuracion/listados | Selector 10/20/50/100 y ayuda sobre el tamaño inicial |
| Registro | /configuracion/registro | Dos radios con consecuencia breve: abierto o solo por invitación |

`/configuracion` redirige a Idioma. Todas requieren settings.manage. Las migas muestran el área;
Page muestra solo el título del tema, estado de borrador y acciones. El cuerpo va alineado a la
izquierda, sin tarjeta alrededor de un único campo ni repetir el título. Una tabla corresponde
a una colección o comparación, no a un único ajuste. Registro usa radios porque sus
opciones necesitan una explicación; el guardado es explícito, no un switch de efecto inmediato.

En móvil las acciones pasan a otra línea y el menú usa el cajón existente; no se agrega
una tercera navegación. La selección de zona usa el catálogo completo validado por el backend.

El diseño local usó las piezas compartidas y el layout real. Después del pedido explícito
de implementación se promovió a las rutas productivas y se retiró el laboratorio. La fuente
visual versionada conserva las reglas y capturas; no se declara una sincronización del Artifact privado.

## Guardado y estados

- Cada pantalla guarda **solo su ajuste**, con un PATCH tipado, una transacción y revisión esperada.
  Descartar devuelve ese borrador a la última lectura confirmada, sin escribir otros temas.
- En Registro, Guardar abre una confirmación que explica abrir o cerrar
  el registro. Cancelar conserva el borrador; confirmar guarda ese ajuste. Las preferencias
  de presentación solas no piden una confirmación adicional.
- Mientras se guarda, deshabilitar edición y doble envío. Guardado correcto: releer la fila,
  quitar el estado de borrador y mostrar toast.success abajo a la derecha.
- Si se intenta salir con cambios, usar la guarda y el diálogo compartidos. Recarga/cierre
  de pestaña conserva la protección nativa del navegador.
- Carga: esqueleto y acciones deshabilitadas. Fallo de lectura: mensaje con Reintentar.
  Fallo de validación: mensajes junto al campo y borrador conservado.
- Error al guardar: toast.error abajo a la derecha, conservando el borrador. Si pudo haber commit, releer antes de reintentar:
  un 500 por descarte de Redis no significa que la escritura se deshizo.
- Revisión desactualizada: 409 y toast.error persistente con Revisar; comparar guardado y borrador del tema sin sustituir
  silenciosamente el borrador. El reintento requiere revisar la diferencia.
- Sin sesión: ingreso. Sin settings.manage: ocultar menú y denegar ruta/API; no mostrar
  controles deshabilitados con valores administrativos.
- Fila ausente: error administrativo, no formulario aparentemente guardado. La lectura
  pública de presentación tiene respaldos es/Buenos Aires/20 y el ingreso sigue cerrado.
- No hay búsqueda ni colección en esta pantalla: vacío de listado y «sin coincidencias»
  no aplican. No guardar el borrador ni sus campos en la URL. La URL productiva sigue siendo
  las cuatro rutas de la tabla; los parámetros de escenarios pertenecen solo al laboratorio.

Los warning e info también usan el Toaster abajo a la derecha. La validación de un campo queda
junto al control. Un fallo de carga sustituye el contenido con estado no operable y Reintentar;
el detalle de su causa tiene un solo toast, sin Banner ni cartel encima de la botonera.
Confirmar una decisión sigue usando ConfirmDialog. Ubicación, duración y ausencia de duplicados
siguen [las premisas del front](../../../ArquitecturaBaseFront/docs/guides/premisas-de-desarrollo.md#mensajes-de-error-y-notificaciones).

## Lectura pública y compatibilidad

GET `/api/settings/presentation`, anónimo, devuelve solo defaultCulture,
defaultTimeZoneId y defaultPageSize. La inicialización pública y los listados lo necesitan sin
settings.manage. No exponer RegistrationMode, auditoría ni la revisión administrativa ahí.

GET/PUT `/api/settings` mantienen settings.manage. El GET suma campos y revision; el PUT existente
conserva `{ registrationMode }` y solo modifica el registro. PATCH `/api/settings`, también
con permiso, para actualizar exactamente un campo conocido y expectedRevision. Ejemplo:
`{ "defaultCulture": "en", "expectedRevision": 1 }`. No es un almacén de claves arbitrarias.
Un campo null, desconocido o inválido, ningún campo o varios ajustes en el mismo PATCH responden
400. Las cuatro pantallas tienen guardado independiente y un cliente anterior sigue funcionando;
el PUT legado incrementa la revisión para que una edición nueva detecte conflictos.

## Criterios de aceptación

Configuración general persistida y auditable, protegida por permiso, sin reiniciar la Api;
valores de respaldo seguros; preferencias existentes preservadas; traducciones en es/en;
vista previa correcta en varias zonas; paginación coherente en usuarios y roles;
rollback y conflictos verificables; actualización observada por dos hosts con Redis real;
teclado y 320 px cubiertos. La persistencia, autorización, concurrencia y caché se verifican con PostgreSQL y Redis reales. La revisión visual aislada usa los componentes productivos y una API simulada.


## Evidencia de implementación

El [plan cerrado](../history/plans/2026-09-30-configuracion-general.md) registra el cierre.
El laboratorio se retiró al promover el diseño; las reglas visuales y sus capturas están en
[la fuente versionada del front](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#configuración-general-2026-09-30).
La unificación de mensajes del resto del front conserva su propio plan de adopción pendiente.
