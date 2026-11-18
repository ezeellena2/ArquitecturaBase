# Barrido documental del 2026-11-18: lo que falta hacer

**Fecha:** 2026-11-18.

**Alcance.** Cada funcionalidad que quedó a medias, sin terminar, diseñada sin programar o propuesta sin decidir, en el
backend y en el front, con su evidencia y sus pasos. No entran la prueba integral de todo el desarrollo, las pruebas a mano
de lo ya programado ni la puesta en producción (el despliegue, el antivirus y el respaldo del almacén, la política de
privacidad publicada y los términos de Google Places). Las pruebas a mano sin criterio escrito, las verificaciones con
servicios reales y la arquitectura para más adelante van [aparte](#aparte), sólo listadas.

**Fuentes.** Nueve informes del barrido del 18/11, uno por área, guardados en una carpeta temporal de la sesión (no se
versionan): núcleo, formularios (el núcleo del módulo), formularios (la parte técnica), documentos por WhatsApp, WhatsApp,
IA, ARCA, front, y propuestas y memoria. Cada uno separa pendientes (A), documentación (B) y planes (C). Lo que cada informe
corrigió en su área ya está en Git (backend `0a139574` a `24f74810`, front `4e090049`); sus propuestas de documentación fuera de
su área no entran acá. Acá quedan los pendientes, sin repetir.

**Commits.** Backend `24f74810` y front `4e090049`. Los informes se hicieron sobre `5356b282` y `cf247979`: entre esos
commits y los de arriba sólo cambiaron documentos y `AGENTS.md`, no código.

**Última corrida completa en verde:** la del 17/11 (backend 11.656/11.656 sobre `02c60e91`; front, 6.022 tests sobre
`93eb8eb0`).

**Lo comprobado en el código.** Lo que generaba dudas se miró antes de escribirlo: el lector de permisos (N-1), las capturas
sin lector (P-1), el porqué de un documento en el chat (D-1), lo validado sin IA en el detalle (D-2), la columna del listado
de catálogos (A-1), la zona del Excel (F-13), la aprobación de quien corrigió (F-14), los códigos de Formularios en WhatsApp
(W-6), la mezcla de acciones del agente (W-1), los límites del robot (A-2), el esquema de la IA (I-1), el contrato del
asistente (I-2), el «+18 %» (P-4), los imports de `cn` (P-5), el reloj del compositor (P-18) y la falta del documento del
CRM (Pr-4). Tres datos de los informes no coinciden con el código y van corregidos:

- Las capturas `app-only`, `folders`, `document-reading`, `library` y `location-contact` no tienen lector en el front: sólo
  se verifican por nombre y hash (`front:src/features/business-forms/api/httpContract.test.ts:58`). El informe de propuestas
  y memoria decía lo contrario; `multipleChoiceHttpContract.test.ts` sólo las nombra en un comentario.
- «Entrar con WhatsApp sin código» (W-17) contaba con el enlace de ingreso que emite el bot. El enlace se retiró el
  2026-11-16 (B4): `LoginLinkIssuer` ya no existe en `src` y sólo queda en un comentario del `.csproj` de Application.
- Los dos tests lentos del front (P-10) figuran abiertos en el plan de arreglos, pero el front `1e877620` (17/11) ya les dio
  10 s para la primera aparición. Hay que confirmarlo.

Dos informes no coinciden en quién decide: CA1 (A-1) y el «+18 %» (P-4). En los dos queda la lectura más simple y se anota la
otra.

**Cómo se lee cada pendiente.** Número estable por área: N (núcleo), F (formularios), D (documentos por WhatsApp), W
(WhatsApp), I (IA), A (ARCA), P (front, lo transversal) y Pr (propuestas sin programar). Los estados son cuatro:

- **a medias:** hay código y falta una parte que se decidió;
- **sin terminar:** se arrancó y quedó cortado;
- **diseñado sin programar:** hay decisiones o tablero, y no hay código;
- **propuesto sin decidir:** hay una propuesta y el usuario no eligió.

«Heredado por confirmar» se suma al estado cuando nadie lo comprobó en el código. Los tamaños (chico, mediano, grande) son
los de los informes. «Lo nombran» aparece cuando el punto junta repetidos de varias áreas.

## Resumen

Quedan **97 pendientes** después de juntar los repetidos: 26 a medias, 16 sin terminar, 8 diseñados sin programar y 47
propuestos sin decidir. 32 se programan sin una decisión nueva y 63 esperan una decisión tuya; dos (I-10 y P-9) tienen una
parte de cada lado. Lo acordado el 16/11 (la lista congelada) deja el trabajo nuevo para después de la prueba integral.

| Área | A medias | Sin terminar | Diseñado sin programar | Propuesto sin decidir | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| [Núcleo](#núcleo) | 3 | 2 | 0 | 5 | 10 |
| [Formularios](#formularios) | 4 | 5 | 1 | 8 | 18 |
| [Documentos por WhatsApp](#documentos-por-whatsapp) | 3 | 0 | 1 | 5 | 9 |
| [WhatsApp](#whatsapp) | 7 | 1 | 1 | 10 | 19 |
| [IA](#ia) | 2 | 0 | 3 | 5 | 10 |
| [ARCA](#arca) | 2 | 0 | 1 | 6 | 9 |
| [Front (lo transversal)](#front-lo-transversal) | 5 | 8 | 0 | 5 | 18 |
| [Propuestas sin programar](#propuestas-sin-programar) | 0 | 0 | 1 | 3 | 4 |
| **Total** | **26** | **16** | **8** | **47** | **97** |

### Los diez primeros para decidir o hacer

1. **[N-1](#n-1-una-cuenta-borrada-conserva-los-permisos-de-sus-roles-b2): una cuenta borrada conserva los permisos de sus
   roles.** Es un error confirmado por un test, en permisos. Es chico y no pide decisión.
2. **[N-5](#n-5-borrar-o-anonimizar-a-una-persona-y-la-retención-de-cada-módulo): borrar a una persona y la retención de
   cada módulo.** Se decide antes de producción, con asesoría legal. Lo nombran el núcleo, Formularios y Documentos, y se cruza
   con IA y ARCA.
3. **[Pr-4](#pr-4-crm-y-módulos-activables): CRM y módulos activables.** La decisión MO1-B sólo está en la memoria de un
   chat: hay que escribirla antes de que se pierda.
4. **[P-1](#p-1-lectores-de-contrato-de-las-cohortes-del-1711): lectores de las cohortes del 17/11.** Más de 80 capturas
   sin lector: esos contratos no tienen control y traban el retiro de dos planes.
5. **[F-14](#f-14-f4-quien-corrigió-una-versión-anterior-puede-aprobar): quien corrigió una versión anterior puede aprobar.**
   Completa una decisión de permisos ya tomada (F4).
6. **[F-16](#f-16-ocho-menores-de-la-revisión-aceptar-o-corregir): ocho menores de la revisión de Formularios.** Cuatro
   tocan migraciones y la convivencia de versiones: conviene decidirlos antes del primer despliegue.
7. **[W-1](#w-1-un-formulario-junto-con-otras-acciones-en-una-respuesta-del-agente): un formulario junto con otras
   acciones.** El código se aparta de lo decidido el 21/10 sin visto bueno, y eso traba el cierre de los casos del asistente.
8. **[D-1](#d-1-doc2-el-porqué-de-un-documento-en-el-chat-del-operador): el porqué de un documento en el chat.** Según el
   informe del front, es lo único elegido que quedó a medias en una pantalla. Es chico.
9. **[P-11](#p-11-el-lienzo-sistema-visual-es-de-otra-cuenta): el lienzo «Sistema visual».** Es de otra cuenta y traba el
   responsive dibujado y el pase de los tableros.
10. **[W-10](#w-10-plantillas-de-meta): plantillas de Meta.** Hoy un aviso fuera de las 24 horas falla, y las necesitan
    Jira (Pr-1) y la API pública (Pr-3).

## Núcleo

Identidad, administración, archivos privados y lo común. Las pantallas del núcleo que siguen abiertas en el front están en
[Front](#front-lo-transversal) (P-6, P-7, P-12, P-15 y P-16).

### N-1. Una cuenta borrada conserva los permisos de sus roles (B2)

- **Estado:** sin terminar.
- **Evidencia:** `PermissionReader.cs:14-21` lee los permisos por `UserRoles` sin mirar la cuenta, y `UserRepository.cs:247-256`
  borra lógico y deja los roles. El test de la tarea 6 del plan de arreglos de formularios falló el 15/11 (B2 confirmado) y no
  se commiteó.
- **Qué falta:**
  1. Commitear el test de integración: una cuenta con un rol, borrarla y pedir sus permisos.
  2. Que el lector no conceda nada a una cuenta borrada, y ver si tampoco a una inactiva.
  3. La revisión adversarial completa, porque es de permisos.
- **Depende de:** nada. Borrar ya revoca las sesiones, pero el lector no debe conceder nada.
- **Tamaño:** chico.
- **Decide:** nadie: lo decidió la tarea 6.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md` (hasta el 2026-11-18, la tarea 6 del plan de arreglos de Formularios, ya retirado).
- **Lo nombran:** Formularios (parte técnica) y el plan de arreglos de formularios. El plan del núcleo no lo tenía.

### N-2. La historia administrativa en pantalla (N13)

- **Estado:** propuesto sin decidir.
- **Evidencia:** el backend escribe la historia (`IAdministrativeChangeRepository` sólo escribe; `AdministrativeChangeRecorder`;
  migración `20261116222056_AddAdministrativeChanges`, `7b5d24eb`). Ningún controller ni lector la lee y el front no tiene
  nada. `docs/features/administracion.md:38`: verla «sería una pantalla nueva, que se dibuja antes».
- **Qué falta:**
  1. El tablero: lista con filtros por cuenta, rol, tipo y fecha, y el detalle con lo agregado, lo quitado y el antes y después.
  2. El permiso: `users.read` y `roles.read`, o uno nuevo con la receta de permiso nuevo.
  3. Un lector paginado (`PagedRequest`, `ApplySort` con desempate y el índice por `TargetId` y fecha si hace falta).
  4. Servicio, contrato `*Query`, controller `GET /api/…`, inventario de rutas, OpenAPI y ejemplos exportados.
  5. La pantalla en Administración, con textos es/en, y sus tests de unidad, integración y front.
- **Depende de:** el tablero y el permiso.
- **Tamaño:** mediano.
- **Decide:** el usuario (tablero y permiso).
- **Dónde vive:** sin plan propio: `docs/features/administracion.md` y la revisión del núcleo (N13).

### N-3. Solicitudes de ingreso con el registro cerrado

- **Estado:** propuesto sin decidir, sin tableros.
- **Evidencia:** `docs/plans/2026-09-30-solicitudes-de-ingreso.md`. No hay entidad, rutas ni permisos (`AccessRequest` sólo está
  en `WhatsAppAccessEventKind.cs`, y el `accessRequests.ts` del front es del OIDC).
- **Qué falta:**
  1. Elegir el alcance (correo y Google primero, WhatsApp después; reintento a las 24 horas; retención; avisos fallidos) y los
     tableros (solicitar, verificar, resultado, listado, detalle, modales, estados y celular).
  2. El ajuste `AllowAccessRequests`, su migración y `accessRequestsOpen` en `login-methods`.
  3. La entidad (`Pending`, `Approved`, `Rejected`), sus errores en Domain y sus textos.
  4. Un propósito de código propio, dentro de los topes del correo (N7) y con plazo en la retención (N16).
  5. Los permisos `accessrequests.read` y `accessrequests.manage`.
  6. Las rutas anónimas `/account/access-requests…`, con límites por IP y destino, y las de `/api/access-requests…`.
  7. Aprobar y rechazar con lock, una sola resolución, «nadie da lo que no tiene» (N1), la historia (N13) y el aviso por la cola.
  8. Google con una intención de solicitud propia.
  9. Las pantallas, el inventario de rutas, OpenAPI, ejemplos y tests.
- **Depende de:** los tableros y las decisiones de retención y avisos.
- **Tamaño:** grande.
- **Decide:** el usuario.
- **Dónde vive:** `docs/plans/2026-09-30-solicitudes-de-ingreso.md`.

### N-4. Dispositivos y sesiones

- **Estado:** propuesto sin decidir (heredado del diseño inicial).
- **Evidencia:** `verificacion-y-produccion.md`, «Propuesta heredada por confirmar»; front `visual-baseline.md`, «Pantallas por
  completar». No hay rutas de sesiones.
- **Qué falta:**
  1. Confirmar el alcance: ver y cerrar las sesiones propias, o también las de otra cuenta.
  2. Diseñar el manejo de las autorizaciones y los tokens de OpenIddict por dispositivo.
  3. Tableros, rutas, permisos y tests.
- **Depende de:** el alcance. Cerrar las pestañas del mismo navegador ya existe (N12), y desactivar o borrar una cuenta ya
  revoca todo.
- **Tamaño:** mediano a grande.
- **Decide:** el usuario.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md`, «Propuesta heredada por confirmar».

### N-5. Borrar o anonimizar a una persona y la retención de cada módulo

- **Estado:** propuesto sin decidir.
- **Evidencia:** `verificacion-y-produccion.md`, «Privacidad de la instalación de prueba»; `docs/features/identidad.md:86`
  («borrar a una persona que lo pide es H14»); la fila «Formularios: retención de respuestas» de `docs/plans/README.md:26` (F10:
  hoy todo se conserva sin plazo). El puerto del núcleo no existe en `Application/Interfaces/Channels`.
- **Qué falta:**
  1. Decidir los plazos con el negocio: cuentas borradas, contactos y las respuestas de formularios (acciones, tareas,
     resultados, PDF fijos, documentos confirmados, datos de las personas, pedidos faltantes y las ubicaciones, que pedía sumar
     el plan de documentos). No heredan los 90 días de WhatsApp.
  2. Diseñar el puerto del núcleo para el borrado definitivo de una persona y la fuente de cada módulo (WhatsApp, Formularios,
     IA y ARCA).
  3. El procedimiento: cuenta, roles y logins, códigos, auditorías, invitaciones, la historia N13 (sólo ids), OpenIddict y
     módulos, en el orden de las FK.
  4. Una acción administrativa o un procedimiento escrito, con su prueba.
  5. Ampliar `PersonalDataRetentionTests` y la política de privacidad.
  6. Sumar las ubicaciones a la fila del índice de planes (es una línea).
- **Depende de:** la decisión de negocio y la revisión legal. Se cruza con la retención del asistente (I-5), el plazo fiscal de
  ARCA (A-6) y la de integraciones (Pr-3, decisión 12).
- **Tamaño:** grande.
- **Decide:** el usuario, con asesoría legal.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md` («Privacidad de la instalación de prueba») y la fila de retención de
  `docs/plans/README.md`.
- **Lo nombran:** Núcleo (A4), Formularios (F10, la retención de respuestas), Documentos (A9, las ubicaciones) y Propuestas y
  memoria (A8).

### N-6. El health check del almacén de archivos privados

- **Estado:** sin terminar.
- **Evidencia:** tabla «Pendientes» de `docs/plans/2026-10-02-almacenamiento-de-archivos.md`. Los `IHealthCheck` son el del correo,
  el del anillo de IA y el de WhatsApp: no hay uno del almacén.
- **Qué falta:**
  1. Un health check que compruebe el acceso al contenedor sin escribir, en `Degraded`.
  2. Su test.
- **Depende de:** nada. El antivirus y el respaldo del contenedor van con la puesta en producción; la retención por propósito,
  con N-5; los consumidores sin alcance (fondo del chat, documentos desde la web, volumen) esperan al producto.
- **Tamaño:** chico.
- **Decide:** nadie: lo fijan las decisiones vigentes del plan.
- **Dónde vive:** `docs/plans/2026-10-02-almacenamiento-de-archivos.md`.

### N-7. Privacidad en el ingreso y las invitaciones

- **Estado:** propuesto sin decidir.
- **Evidencia:** `verificacion-y-produccion.md`, «Privacidad de la instalación de prueba».
- **Qué falta:**
  1. Enlazar la política desde el ingreso, el registro y el pie (pantallas que se dibujan antes).
  2. Un canal humano de contacto en los dos idiomas.
  3. La exclusión «no me invites», con evidencia del consentimiento.
  4. Confirmar el contacto al invitar.
  5. Decidir si `users.read` ve «leída» o sólo «entregada».
- **Depende de:** los tableros y la revisión legal.
- **Tamaño:** chico a mediano cada uno.
- **Decide:** el usuario.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md`, «Privacidad de la instalación de prueba».

### N-8. Observaciones menores de la revisión del núcleo

- **Estado:** a medias: anotadas, sin plan.
- **Evidencia (comprobada el 18/11):**
  - O1: `ControllerResultExtensions.cs` no pone `Retry-After` en los 429 de negocio; el front lee el cuerpo.
  - O2: el seed realinea el cliente `web` en cada arranque, y dos versiones con redirect URIs distintas se pisan.
  - O5: `DeliveryRepository.cs:19` borra todos los vencidos sin tope.
  - O7: `IUnitOfWork.cs:23-25` promete traducir la clave única, y un diferido que choca al confirmar da 500.
  - O8, lo que queda: comentarios viejos en `LoginCodeService.cs:39-41`, `AccountCreationPolicy.cs:11-12`, `RoleRow.cs:3` y
    `EmailOptions.cs:27-30`.
  - O10: las configuraciones de `LoginAudit`, `LoginCode` y `StoredFile` no dicen por qué no tienen FK a la cuenta.
- **Qué falta:** una o dos líneas cada una, con su test donde cambia el comportamiento (O1, O5 y O7).
- **Depende de:** nada. Se arreglan al tocar esos archivos.
- **Tamaño:** chico.
- **Decide:** nadie.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md`, «Núcleo: lo que quedó fuera de los arreglos del 2026-11-14».

### N-9. La limpieza de lo que dejó el retiro del enlace de ingreso (B4)

- **Estado:** a medias.
- **Evidencia:** siguen `Domain/Authentication/LoginLink.cs`, `ILoginLinkRepository.cs` y `PhoneNumberLinker.cs:70`
  (`VoidPendingLinksAsync`), que ya no cambia nada porque no se emite ningún enlace.
- **Qué falta:**
  1. Una migración que borre `LoginLinks`, en un despliegue posterior por la convivencia de versiones.
  2. Quitar la entidad, su configuración, los métodos de enlaces y la invalidación.
  3. Conservar `LockAccountAsync` (`login-link:`) con su texto, porque el texto es el lock, y `LoginMethod.WhatsAppLink` por las
     auditorías.
  4. Tests y documentación.
- **Depende de:** que la retención vacíe la tabla (30 días desde el retiro del 16/11).
- **Tamaño:** chico.
- **Decide:** nadie.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md`, «Núcleo: lo que quedó fuera de los arreglos del 2026-11-14».

### N-10. El JSON del cliente de Google en `docs/`

- **Estado:** a medias (uno de los dos pendientes de la Etapa 0).
- **Evidencia:** `docs/client_secret_*.json` sigue en la carpeta, ignorado por `.gitignore` (nunca entró al historial).
  `id de cliente.txt` ya no está.
- **Qué falta:** moverlo fuera de la carpeta del repo.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** el usuario: es su archivo y un agente no lo toca.
- **Dónde vive:** sólo en la memoria de los chats (plan de la plantilla estándar).
- **Lo nombran:** Propuestas y memoria (A19).

## Formularios

Carga desde la app, publicación, catálogos, carpetas, el Excel, «sólo en la app» y lo que dejaron los arreglos de la
revisión. Los lectores del front de sus capturas están en P-1, sus escenas visuales en P-2 y P-3, y el lienzo en P-11.

### F-1. Copiar formularios

- **Estado:** propuesto sin decidir.
- **Evidencia:** fila «Formularios: copiar» de `docs/plans/README.md:28` y la lista de `docs/plans/formularios/README.md`.
  `FormDefinitionsController` no copia; la única copia es la de un estándar (`FormLibraryController.cs:27`,
  `POST api/forms/library/{key}/copies`).
- **Qué falta:**
  1. El tablero: dónde se copia y qué se lleva (preguntas, reglas, sugerencias, diseño del PDF y carpeta).
  2. Elegir. La fila del índice adelanta el criterio: identidades y prefijo nuevos, sin numeración ni vínculos con Meta, como
     borrador sin publicar.
  3. Un plan con TDD.
  4. Backend, front y pruebas.
- **Depende de:** nada técnico.
- **Tamaño:** mediano.
- **Decide:** el usuario.
- **Dónde vive:** la fila del índice de planes y `docs/plans/formularios/README.md`.
- **Lo nombran:** Formularios (núcleo, A4), Propuestas y memoria (A7) e IA (A15). Copiar o cambiar con la IA queda fuera de
  alcance por decisión y no cuenta.

### F-2. Importar respuestas desde un Excel

- **Estado:** propuesto sin decidir.
- **Evidencia:** «Fuera de alcance» del plan de la carga y `docs/plans/formularios/README.md`. No hay ruta ni tablero. Por
  WhatsApp ya existe la carga por planilla (VC1-A).
- **Qué falta:**
  1. El tablero.
  2. Decidir la plantilla por versión, los renglones, los archivos, si numera y si corre las reglas.
  3. Backend (puede reutilizar el lector acotado de `Import/` y la captura común), front y pruebas.
- **Depende de:** nada.
- **Tamaño:** grande.
- **Decide:** el usuario.
- **Dónde vive:** `docs/plans/formularios/README.md`.
- **Lo nombran:** Formularios (núcleo, A5) y Propuestas y memoria (lo único del caso de prueba que sigue sin existir).

### F-3. Descargar todas las fotos juntas desde el detalle

- **Estado:** propuesto sin decidir.
- **Evidencia:** `docs/plans/formularios/README.md`. `FormFilesController` sólo da `thumbnail` y `content`, de a un archivo.
- **Qué falta:**
  1. El tablero.
  2. Decidir si el ZIP se arma al vuelo o en segundo plano, y su tope de tamaño.
  3. Backend, front y pruebas.
- **Depende de:** nada.
- **Tamaño:** chico a mediano.
- **Decide:** el usuario.
- **Dónde vive:** `docs/plans/formularios/README.md`.

### F-4. Listas dependientes (CW7) por WhatsApp

- **Estado:** propuesto sin decidir.
- **Evidencia:** «Para después» de `docs/plans/2026-11-07-elegir-primero-por-un-campo.md`. La decisión 4 de la carga las dejó
  sólo en la app: hoy la preparación devuelve `Forms.Record.CatalogDependsOnAnswer` y el formulario no se manda. Elegir primero
  por un campo (LD2-A, perfil r11) ya anda por WhatsApp.
- **Qué falta:**
  1. Elegir el camino: el `update_data` de LD2-A, cuando las dos preguntas van en la misma pantalla, o el intercambio cifrado de
     F-5, para cualquier pantalla.
  2. Decidir qué pasa si quedan en pantallas distintas: se fuerzan juntas o sigue sin mandarse.
  3. El tablero.
  4. Un perfil nuevo del compilador, la oferta con las opciones por respuesta (tope de 200 por respuesta), el chequeo y la
     prueba con Meta.
- **Depende de:** el compilador de Flows; con el intercambio, de F-5.
- **Tamaño:** grande.
- **Decide:** el usuario.
- **Dónde vive:** el plan de elegir primero («Para después») y `docs/plans/formularios/README.md`.
- **Lo nombran:** Formularios (núcleo, A7), Front (A20) y Propuestas y memoria (A10).

### F-5. Intercambio cifrado con Meta: búsqueda en vivo y listas largas

- **Estado:** propuesto sin decidir, por diseñar.
- **Evidencia:** fase 3 de `docs/plans/2026-10-21-catalogos-con-origenes-externos.md` y la lista de
  `docs/plans/formularios/README.md`. Ya alivian los catálogos grandes el filtro por persona (E2) y elegir primero por un campo.
- **Qué falta:**
  1. El diseño: el endpoint de intercambio de datos de Flows, sus claves y dónde se publica.
  2. Un ADR, el tablero y la decisión.
  3. El código y las pruebas con Meta.
- **Depende de:** una URL pública estable para Meta y el módulo de WhatsApp.
- **Tamaño:** grande.
- **Decide:** el usuario.
- **Dónde vive:** la hoja de ruta de catálogos (fase 3).
- **Lo nombran:** Formularios (núcleo, A8, y parte técnica, 18) y Propuestas y memoria (A10).

### F-6. Catálogos desde bases externas y cómo se llega a la red del cliente

- **Estado:** propuesto sin decidir.
- **Evidencia:** fases 5 y 6 de la hoja de ruta de catálogos («Los orígenes», «Esquema de base propuesto» y «Preguntas
  abiertas») y la fila «Conexión a bases de datos de clientes» de `docs/plans/README.md:43`. Las fuentes no coinciden:
  - la hoja recomienda un conector en la red del cliente;
  - la fila del índice (2026-10-22) lo descarta y propone una VPN de sitio a sitio de Azure con un catálogo «Desde una base
    externa»;
  - el 2026-11-02 el usuario pidió pensarlo genérico y en autoservicio, y después dudó de que valga la pena;
  - el chat recomendó no construir el conector por ahora: el Excel con la IA que mapea columnas, una API chica para recibir
    catálogos cuando un cliente la pida, y el agente sólo si varios clientes lo piden y lo pagan.

  Ya existe la copia local de un origen, por ahora sólo para listas de un módulo (`External`, `IStructuredCatalogFeed`,
  `FormCatalogFeedService`, `08c83c19`).
- **Qué falta:**
  1. Decidir si se construye y cómo se llega: conector, VPN, recepción por API o la recomendación del chat.
  2. Contestar las preguntas abiertas 1 a 3: el motor de la base del primer cliente, la primera instalación y si se ocultan o
     renombran opciones de afuera.
  3. Un ADR que amplíe el 0019, si se conecta a la base del cliente.
  4. Los tableros de «Conexiones externas» y del catálogo de origen externo.
  5. Un plan de tareas.
  6. Poner de acuerdo la hoja y la fila del índice con lo que se elija.
- **Depende de:** el primer cliente con base propia; con la VPN, de la red en Azure.
- **Tamaño:** grande. Chico si se elige la recomendación del chat (la API chica sería mediana).
- **Decide:** el usuario.
- **Dónde vive:** `docs/plans/2026-10-21-catalogos-con-origenes-externos.md` y la fila del índice.
- **Lo nombran:** Formularios (núcleo, A9, y parte técnica, 3) y Propuestas y memoria (A4). Lo necesita D-9.

### F-7. El catálogo «Con una consulta» (K5)

- **Estado:** propuesto sin decidir, postergado el 2026-10-11.
- **Evidencia:** ADR 0019 (SQL de configuración acotado a catálogos) y front `docs/design/formularios-whatsapp.md`: sólo si una
  lista no puede armarse con filtros.
- **Qué falta:**
  1. Un caso real que no se resuelva con `Filters` ni con `External`.
  2. Después, el tablero, la decisión y el código.
- **Depende de:** ese caso.
- **Tamaño:** sin estimar aparte (el informe lo junta con F-6).
- **Decide:** el usuario, cuando aparezca el caso.
- **Dónde vive:** el ADR 0019 y el diseño del front.
- **Lo nombran:** Formularios (núcleo, A9) y Front (A19).

### F-8. Completar otras preguntas con un dato de la opción

- **Estado:** propuesto sin decidir.
- **Evidencia:** hoja de ruta de catálogos, «Campos extra» y pregunta abierta 4. «Completar con ARCA» (CA1) hace algo parecido,
  sólo para los campos del catálogo.
- **Qué falta:**
  1. El tablero y la decisión.
  2. Backend, front y pruebas.
- **Depende de:** nada.
- **Tamaño:** mediano.
- **Decide:** el usuario.
- **Dónde vive:** la hoja de ruta de catálogos.

### F-9. Llamar a otro formulario (etapa 1b)

- **Estado:** diseñado sin programar (planeado el 2026-11-12).
- **Evidencia:** `docs/plans/integraciones/estudio-2026-10-22.md`, sección G: es la etapa 1b de Jira y no depende de Jira.
  `docs/plans/formularios/README.md` la ubica ahí. En el backend no existe «Se carga sola».
- **Qué falta:**
  1. «Se carga sola»: la respuesta sugerida llega precargada y queda a nombre de quien cargó la primera.
  2. «Preguntar siempre» también en la app, con la zona «Qué sigue».
  3. Fotos y documentos en «Datos que pasan».
  4. Decidir si crear una respuesta en nombre de otra persona pide un ADR.
  5. El tablero con estados, un plan con TDD, backend, front y pruebas.
- **Depende de:** «Qué sugerir después», que ya existe.
- **Tamaño:** mediano.
- **Decide:** el usuario (cuándo, y el ADR).
- **Dónde vive:** el estudio de integraciones, sección G.
- **Lo nombran:** Formularios (núcleo, A10) y Propuestas y memoria (A1, Jira).

### F-10. R3: la versión, el idioma y la señal de purga en la tarjeta del chat

- **Estado:** sin terminar (heredado de la Etapa 1).
- **Evidencia:** «Pendientes que se arrastran» del resumen de la Etapa 2 de catálogos, ya retirado ([procedencia](../history/README.md#barrido-documental-del-2026-11-18)): la versión y el idioma de la
  respuesta, y una señal explícita de que el mensaje se purgó, sin inferirla de `Body=null`. No hay esa señal en
  `Application/Modules/WhatsApp/Models`.
- **Qué falta:**
  1. Confirmar con el tablero R3 si sigue haciendo falta.
  2. Si sí, el dato en la lectura del chat, su texto es/en y su test.
- **Depende de:** la lectura del chat de WhatsApp.
- **Tamaño:** chico.
- **Decide:** el usuario confirma si sigue; después se programa sin decisión.
- **Dónde vive:** `docs/plans/formularios/README.md`, en «Después de la Etapa 5».

### F-11. La prueba de integración del retiro de Flows al pasar a la app

- **Estado:** sin terminar.
- **Evidencia:** el diseño técnico de `docs/plans/2026-11-09-formularios-solo-en-la-app.md` («El retiro de Flows») dice «Se
  agrega una prueba de integración», y no está. `FormUnusedVersionQueryTests.cs` sólo traduce el SQL, y ningún test de
  `FormAppOnlyPublicationTests.cs` cruza `AppOnly` con `IFormUnusedVersionReader`.
- **Qué falta:**
  1. Con PostgreSQL, publicar v1 por un canal de prueba y después v2 «sólo en la app».
  2. Comprobar que `IFormUnusedVersionReader` devuelve v1 como no usada, y nada mientras haya una publicación abierta.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie.
- **Dónde vive:** el plan de «sólo en la app».

### F-12. Repetir en una copia las pruebas de quitar un módulo

- **Estado:** sin terminar.
- **Evidencia:** el corte 3 del plan de «sólo en la app» pide volver a probar «Quitar WhatsApp» después de SA5, y
  `docs/guides/quitar-whatsapp.md:229` acredita sólo la prueba de la Etapa 6. El test
  `Without_a_form_channel_every_form_publishes_only_in_the_app` cubre el comportamiento sin el canal, no una copia sin el
  módulo. La parte del front de `docs/guides/quitar-formularios.md` no se repite desde `94b11e5f` (tiene criterio: los cuatro
  comandos de la guía).
- **Qué falta:**
  1. En una copia descartable, seguir `quitar-whatsapp.md` entera: build sin advertencias, la migración de baja,
     `has-pending-model-changes` y los tests del núcleo.
  2. En otra copia, la parte del front de `quitar-formularios.md`.
  3. Anotar las dos en «Comprobaciones registradas» de cada guía.
- **Depende de:** nada.
- **Tamaño:** mediano.
- **Decide:** nadie.
- **Dónde vive:** el plan de «sólo en la app» y las dos guías.
- **Lo nombran:** Formularios (núcleo y parte técnica).

### F-13. El Excel cae a UTC y no a la zona general (XL2)

- **Estado:** a medias.
- **Evidencia:** XL2 de `docs/plans/2026-11-09-excel-de-respuestas-en-una-hoja.md` pide la zona del perfil y, si no tiene, la de
  la configuración general. `FormExportResponsePolicy.cs:22-27` cae directo a UTC. El tablero sí cae a la del negocio y después a
  UTC (`FormDashboardZonePolicy.cs:13-14`).
- **Qué falta:**
  1. Usar la misma cadena que el tablero: perfil, zona general y UTC.
  2. Su test.
- **Depende de:** nada. Sólo importa si la zona de una cuenta no se lee.
- **Tamaño:** chico.
- **Decide:** nadie: lo fija XL2.
- **Dónde vive:** el plan del Excel.

### F-14. F4: quien corrigió una versión anterior puede aprobar

- **Estado:** a medias.
- **Evidencia:** `FormApprovalReader.cs:21-23` mira sólo el `CorrectedByUserId` de la respuesta de la tarea y el
  `ReviewedByUserId` del documento que apunta a ella. No recorre `ReplacesRecordId`: el archivo no lo nombra.
- **Qué falta:**
  1. Recorrer la cadena de reemplazos (una CTE) o guardar en la tarea quiénes fijaron valores.
  2. Usarlo en la decisión, la bandeja, el conteo y la continuación.
  3. Tests de integración con la corrección de una corrección y con un documento indirecto.
  4. La revisión adversarial completa, porque decide quién puede aprobar.
- **Depende de:** nada.
- **Tamaño:** chico a mediano.
- **Decide:** nadie: completa la decisión de F4.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md` (los menores de Formularios); el hallazgo, en la revisión de
  Formularios.

### F-15. B1: el toque viejo contesta de más

- **Estado:** a medias.
- **Evidencia:** `FormIssueConfirmationService.cs:45-48`: `NoLongerIssuable` contesta a cualquier botón viejo de una respuesta
  anulada sin `DecisionTapId`, también a una ya emitida y revertida, con un texto levemente inexacto.
- **Qué falta:** afinar la condición y su test.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md` (los menores de Formularios).

### F-16. Ocho menores de la revisión: aceptar o corregir

- **Estado:** sin terminar. Son de la revisión adversarial 2 (16/11), contrastados con el código el 18/11.
- **Evidencia y opciones:**
  1. **F2, la auto-consulta** (`FormActionEffectService.cs:86-100`): si `channel.GetAsync` falla sin nada enviado, la reconsulta
     no encola y termina «destinatario no disponible», reintentable a mano. Opciones: reenviar, porque no hubo efecto, o dejarla
     «En duda». Las dos llevan su test con `FakeTimeProvider`.
  2. **2b-2 (F15):** `20261116111033_AddFormRecordCatalogFields.cs:60-69` inserta `->> 'key'` y `-> 'name'` en columnas NOT NULL,
     sin `COALESCE` ni filtro. En `appdb` no hay casos (85 campos, 16/11). Opciones: corregir el SQL antes del primer despliegue o
     dejarlo verificado.
  3. **2b-1 (F15):** sólo la migración y la captura escriben la tabla, y un nodo viejo que captura durante el despliegue deja
     campos fuera hasta otra captura. Opciones: un relleno idempotente en el despliegue siguiente o aceptarlo por escrito.
  4. **2b-3 (F31):** el «no encontrado» espera siete días y el revisor ve «leyendo» hasta el vencimiento (heredado, sin
     comprobar). Opciones: un plazo menor o dejarlo.
  5. **2c-6 (F47):** `FormFolderTree.cs:22` aplica el tope de 500 carpetas sólo al crear una; las del sistema que crea
     «Agregar» no cuentan. Opciones: contarlas o dejarlo.
  6. **`NotApplicable`:** una copia vieja no toma el valor nuevo como terminal en el reintento de un aviso a un rol (heredado).
     Opciones: aceptarlo (ADR 0018) o sellar el estado en el run.
  7. **Tarea 10** (`SkiaFormPdfRenderer.cs:507-508`): un snapshot sin `TimeZoneId` sale en UTC, y un `WriteOnce` con un
     `Uncertain` pendiente podría ver otra hora que el original. Sin producción, sólo afecta datos de desarrollo. Opciones:
     aceptarlo o tratar el caso.
  8. **2b-5** (`20261116114811_FormActionRunStallNotice.cs:20`): la migración marca como avisados los runs que ya había, y lo que
     un nodo viejo deje fallido después lo anuncia el nuevo. Opciones: aceptarlo por escrito (la recomendada: el informe lo da
     por esperable) o tratarlo.
- **Qué falta:** elegir en cada uno. Lo que se corrija lleva su test; lo que se acepte, una línea en la regla.
- **Depende de:** nada. Los cuatro de migraciones y versiones (2, 3, 6 y 8) conviene cerrarlos antes del primer despliegue.
- **Tamaño:** chico cada uno.
- **Decide:** el usuario.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md` (los menores de Formularios); hasta el 2026-11-18, «Qué falta» del
  plan de arreglos de Formularios, ya retirado.

### F-17. Partes de tareas que se dejaron al programar los arreglos

- **Estado:** a medias. Cada una se aceptó al programar.
- **Evidencia:** la revisión de Formularios y el plan de arreglos:
  - F1: la ayuda «sin separador de miles» por defecto en la app (se dejó el error);
  - F13: recalcular la repetición al leer el detalle (hoy se busca sólo al pasar);
  - F17: reusar unos minutos la verificación de Meta (pide un caché en Redis, ADR 0009);
  - B3: el aviso en el editor de reglas (hoy sólo falla con `Forms.Rule.RoleWithoutMembers`);
  - F8: una copia vieja sigue guardando el recibo de una versión que no entiende (lo cubre el pedido nuevo del front);
  - F24: el `RequestId` de crear un catálogo quedó opcional (unos 69 tests crean sin él).
- **Qué falta:**
  1. Decidir si se completa alguna; cada una, con su test.
  2. Anotar en la revisión, junto a cada hallazgo, el commit y lo que se hizo distinto del plan.
- **Depende de:** nada.
- **Tamaño:** chico cada una.
- **Decide:** el usuario.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md` (los menores de Formularios) y la revisión de Formularios.

### F-18. F6-4: el escáner de PDF hostil

- **Estado:** sin terminar.
- **Evidencia:** `docs/reviews/2026-11-14-arquitectura-formularios.md:403`: el escáner no ve variantes raras de la sintaxis de
  `stream` ni un `/Filter` por referencia.
- **Qué falta:**
  1. Decidir si se amplía.
  2. Si sí, los casos en el PDF hostil de prueba y en el escáner.
- **Depende de:** nada.
- **Tamaño:** chico a mediano.
- **Decide:** el usuario.
- **Dónde vive:** la revisión de Formularios y `docs/plans/verificacion-y-produccion.md` (los menores de Formularios).

## Documentos por WhatsApp

El plan `2026-10-22-documentos-por-whatsapp.md` está programado entero (tareas 1 a 30, la pregunta «Ubicación» y el asistente
con ubicación) y calificado con OpenAI real. Lo que sigue son dos huecos de pantalla y propuestas para después. Sus lectores
del front están en P-1, y la ubicación en la retención, en N-5.

### D-1. DOC2: el porqué de un documento en el chat del operador

- **Estado:** a medias.
- **Evidencia:** front `ChatReceivedDocument.tsx` no lee `why` (comprobado: el archivo no lo nombra), y `ChatTimeline.tsx:63-72`
  dibuja sólo el porqué del asistente y el de las planillas. El guard de `api/chat.ts:38-49` sí acepta `why`, y
  `lib/threadPrivacy.ts:5-6` lo recorta. El backend lo manda desde la tarea 9. DOC2 eligió «Por qué lo propuso» o «Por qué
  preguntó» debajo del documento, como en AS4.
- **Qué falta:**
  1. Dibujar los dos títulos y su texto debajo del documento, en gris, con textos es/en en `whatsapp.json`.
  2. Respetar lo que ya recorta `threadPrivacy`.
  3. Un test de componente.
  4. La escena `chat-document` de `test:visual`, con su referencia.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie: DOC2 fija los títulos.
- **Dónde vive:** front `docs/design/documentos.md` (DOC2) y el mapa de tableros de `visual-baseline.md`.
- **Lo nombran:** Front (A1). El informe de documentos lo daba por programado.

### D-2. DOC3: lo que se validó sin IA, en el detalle de una respuesta

- **Estado:** a medias, a confirmar.
- **Evidencia:** DOC3 pide que el detalle diga qué reconoció, qué se corrigió o completó y qué se validó sin IA (front
  `docs/design/documentos.md`, DOC3). «Cómo empezó» dice de qué documento salió, los datos propuestos y «Corregido al completar»
  (front `src/locales/es/forms.json:1346`). No encontré un texto de lo validado sin IA en el detalle: la única mención es la
  ayuda de la lectura (`forms.json:14`). La diferencia no está anotada en el diseño.
- **Qué falta:**
  1. Confirmarlo en `ResponseDetailContent`.
  2. Sumarlo, o anotarlo como diferencia en `documentos.md` (lo más simple).
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie, si se anota.
- **Dónde vive:** front `docs/design/documentos.md` (DOC3).
- **Lo nombran:** Front (A9).

### D-3. «Bancos y billeteras» no tiene billeteras

- **Estado:** a medias.
- **Evidencia:** `docs/features/formularios.md:1422-1426` y `Resources/Standards/bank-entities.json` (2026-11-11): sólo los 59
  bancos del registro de cheques del BCRA. Ningún proveedor de CVU, porque no se encontró una lista oficial, y tampoco los bancos
  digitales que no compensan cheques. Un comprobante con un CVU (Mercado Pago y las demás billeteras) deja vacío y marcado «banco
  o billetera», y el reparto por banco del tablero pierde esas transferencias. El piloto incluye Mercado Pago y Brubank.
- **Qué falta:**
  1. Una fuente oficial de los códigos de proveedor de CVU (los asigna COELSA) y de los bancos que faltan.
  2. Sumarlos a `bank-entities.json` con su fecha y su fuente. `BankAccountKey` ya saca el código de un CVU (dígitos 4 a 7).
  3. Decidir qué pasa con una instalación que ya tiene el catálogo: «Agregar» crea «Bancos y billeteras» sólo si no existe.
  4. Un test con un CVU real de cada proveedor cargado.
- **Depende de:** conseguir la fuente.
- **Tamaño:** chico.
- **Decide:** el usuario, si acepta una fuente no oficial y si se actualizan las copias existentes. Con una fuente oficial, se
  programa sin decisión.
- **Dónde vive:** el plan de documentos, «Lo que falta».

### D-4. El aviso de «no se le puede mandar» no dice el monto (DOC4)

- **Estado:** diseñado sin programar. La diferencia se aceptó por lo más simple al programar la tarea 9.
- **Evidencia:** el tablero DOC4 dice «Gracias, recibí tu comprobante de $412.000. No lo puedo cargar desde acá…» (front
  `docs/design/documentos.md:33`). El código dice «Gracias, recibí tu documento para «{0}»…» (`AssistantTerminalTexts.resx:65`,
  `DocumentNotSendable`), porque el puerto no trae el monto.
- **Qué falta:**
  1. Que `IStructuredDocumentChannel` devuelva el monto leído y su moneda cuando el estándar marca cuál es.
  2. El texto con el monto en es y en, con la pieza de números por región.
  3. El test del texto y del caso sin monto (un formulario propio), que sigue con el texto de hoy.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** el usuario: reabrir una diferencia aceptada es suyo.
- **Dónde vive:** el plan de documentos, «Diferencias con los tableros».

### D-5. El resumen por WhatsApp no dice que el borrador va a leer documentos

- **Estado:** propuesto sin decidir.
- **Evidencia:** el plan, tarea 9: el resumen de IA3-A no dice que el borrador va a leer documentos (tensión con el punto 20).
  `docs/features/whatsapp.md`: «Crear borrador» deja la lectura prendida si la propuesta la trae. `FormAuthoringSummaryPolicy` no
  la menciona.
- **Qué falta:**
  1. Decidir si el resumen suma un renglón, por ejemplo «Va a leer documentos como este.».
  2. Si se suma: el texto en `Resources/Assistant` es/en, `FormAuthoringSummaryPolicy` y su test. No hace falta recalificar: el
     resumen es un texto fijo.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** el usuario.
- **Dónde vive:** el plan de documentos, «Lo que falta».

### D-6. La carga automática «Cargar solo si es seguro»

- **Estado:** propuesto sin decidir. Se decidió que viene después, con datos.
- **Evidencia:** el plan, «La carga automática (etapa posterior)» y «Decisiones tomadas». No hay código: ni `AutoLoad` ni «Cargar
  solo si es seguro» en `src/`.
- **Qué falta:**
  1. Un historial real de cargas y correcciones (el piloto). «Cómo viene leyendo» ya lo mide por formulario y pregunta.
  2. Fijar qué es un historial suficiente por formulario y por campo.
  3. Los tableros: el ajuste en el editor, el mensaje «Cargué tu transferencia…», la marca «Cargada automáticamente» en la
     bandeja y cómo se deshace.
  4. El plan técnico: un modo por formulario sellado en la versión y la captura sin confirmación.
  5. Revisar el ADR 0022, que dice que en la primera etapa la IA nunca carga sola; si cambia, un ADR nuevo.
- **Depende de:** los datos del piloto.
- **Tamaño:** grande.
- **Decide:** el usuario.
- **Dónde vive:** el plan de documentos y el ADR 0022.

### D-7. Los estándares siguientes de la biblioteca

- **Estado:** propuesto sin decidir. La biblioteca los muestra como «Próximamente», sin acción.
- **Evidencia:** `Resources/Standards/upcoming.json` (`BusinessFormStandardSource.cs:16`) y
  `docs/features/formularios.md:1432-1435`: nota de crédito o débito, recibo, remito, ticket de gasto, orden de compra,
  presupuesto, cheque o eCheq, constancias, liquidaciones, carta de porte y DTE. La factura recibida con el QR de AFIP, que nombra
  el plan, no está en la lista ni en el código: la «Factura» de ARCA es para emitir.
- **Qué falta, por estándar:**
  1. Elegir el orden.
  2. Las preguntas y sus tipos, «Cómo reconocerlo», las validaciones fijas, los datos que marcan un repetido y la moneda de cada
     monto.
  3. El resumen por idioma y su catálogo, si lo necesita.
  4. Documentos ficticios y su calificación.
  5. Para la factura con QR, además, un lector de QR, que no existe.
- **Depende de:** el orden que se elija. Se cruza con A-8 (constatar comprobantes recibidos con ARCA).
- **Tamaño:** mediano cada uno.
- **Decide:** el usuario.
- **Dónde vive:** el plan de documentos y `upcoming.json`.

### D-8. Varias fotos de un mismo documento, o varios documentos en un mensaje

- **Estado:** propuesto sin decidir.
- **Evidencia:** el plan, última fila de «Casos a contemplar» y «Alcance». Hoy cada conversación usa su último archivo y uno
  nuevo reemplaza al anterior (`docs/features/inteligencia-artificial.md:187`).
- **Qué falta:**
  1. Los tableros: cómo se juntan las fotos, la espera de la ráfaga y cuántas cargas salen.
  2. Una capacidad que lea varias lecturas en una carga (DOC6 ya junta hasta cinco).
  3. El tope de acciones de los casos del asistente.
- **Depende de:** el diseño.
- **Tamaño:** grande.
- **Decide:** el usuario.
- **Dónde vive:** el plan de documentos.

### D-9. Tableros que cruzan formularios o suman datos externos

- **Estado:** propuesto sin decidir.
- **Evidencia:** la fila «Tableros con datos propios y de sistemas externos» de `docs/plans/README.md:44`.
- **Qué falta:** el alcance, el tablero y el código, después de F-6.
- **Depende de:** la conexión con bases externas (F-6).
- **Tamaño:** grande.
- **Decide:** el usuario.
- **Dónde vive:** la fila del índice de planes.

## WhatsApp

El chat de atención, el asistente, los casos y lo que dejaron los arreglos de la revisión. Las «Varias copias» (H23, H24 y H26)
y la «Gran escala» (H27 a H34) van [aparte](#arquitectura-para-más-adelante).

### W-1. Un formulario junto con otras acciones en una respuesta del agente

- **Estado:** a medias: el código se aparta de lo decidido, sin visto bueno.
- **Evidencia:** lo decidido el 21/10 («hasta 3, en orden… un texto y un formulario, o dos formularios», plan de casos,
  «Decisiones tomadas») y el tablero AS3, donde una ráfaga se responde con un texto y dos formularios. El código deriva con
  `replyNotAllowed` si un formulario va con otra acción (`WhatsAppAssistantReplyPolicy.cs:159-160`, comprobado). En el front, el
  «Por qué lo mandó» de un formulario no tiene lugar ni caso (front `docs/design/asistente-casos.md`, AS4, punto 7).
- **Qué falta:**
  1. Aceptar la diferencia o pedir la mezcla.
  2. Si se pide: aceptar formulario más texto, y dos formularios, en `PrepareAsync` y `ApplyAsync`, con el cupo de dos ofertas
     bajo lock.
  3. Lugar y caso para el porqué de un formulario.
  4. Tests con PostgreSQL y la cohorte del agente.
- **Depende de:** el cupo de ofertas y la salida en orden (H6, hecha).
- **Tamaño:** mediano.
- **Decide:** el usuario.
- **Dónde vive:** `docs/plans/2026-10-21-casos-del-asistente.md`.

### W-2. H25: lo que todavía no espera a otra versión

- **Estado:** a medias (decidido el 13/11).
- **Evidencia:** `docs/features/whatsapp.md`, «Dos versiones a la vez» («Todavía no…»). Sólo el tipo de un mensaje se lee con
  tolerancia (`WhatsAppMessageConfiguration.cs:29,124-125`); los demás enums van con `HasConversion<string>()` sin respaldo
  (`WhatsAppMessageConfiguration.cs:26-32`, `WhatsAppOutgoingMessageConfiguration.cs:21,32`, `WhatsAppContactConfiguration.cs:23`,
  referencias y sesiones).
- **Qué falta:**
  1. Un Flow preparado con otro perfil espera a la copia que lo conoce, en vez de quedar fallido.
  2. Una respuesta cuyo payload sellado o contenido del asistente escribió otra versión queda pendiente, como ya pasa con la
     proyección.
  3. Un Flow que preparó otra versión no falla al despacharse.
  4. Un valor nuevo de un enum guardado como texto no rompe la lectura ni frena la salida.
  5. Tests con una fila de otra versión.
- **Depende de:** nada. Va antes de desplegar con dos versiones a la vez o de volver a una imagen anterior.
- **Tamaño:** mediano.
- **Decide:** nadie.
- **Dónde vive:** `docs/plans/2026-10-01-bandeja-whatsapp.md` («Lo que queda de los arreglos») y `docs/features/whatsapp.md`.

### W-3. H19: la traza no sigue en la captura de Flows ni en la cola de acceso

- **Estado:** a medias.
- **Evidencia:** `docs/features/whatsapp.md`: la captura de los Flows (con su rechazo y su reenvío) y la cola de acceso todavía no
  continúan la traza. `WhatsAppTelemetry` sólo se usa en `WhatsAppInboundProcessor.cs:140` y
  `WhatsAppOutgoingBackgroundService.cs:32`.
- **Qué falta:**
  1. Continuar la traza en `WhatsAppFlowReplyBackgroundService`, con el rechazo y el reenvío.
  2. En `WhatsAppSenderBackgroundService`, guardar y continuar el contexto de cada entrega de acceso.
  3. Tests.
- **Depende de:** la entrega durable del núcleo, si el contexto se guarda en ella.
- **Tamaño:** chico (la captura) a mediano (la cola de acceso).
- **Decide:** nadie.
- **Dónde vive:** el plan de la bandeja, «Lo que queda de los arreglos».

### W-4. Una oferta del asistente cuyo Flow cae después de la ubicación

- **Estado:** a medias (el hueco conocido de H33 con W1-A).
- **Evidencia:** `docs/features/whatsapp.md`, «Envío durable de formularios»: una oferta del asistente ya anunciada cuyo Flow cae
  así no tiene plan B.
- **Qué falta:**
  1. Elegir qué se le contesta a la persona.
  2. Cuando el worker deja fallido (`AccountUnavailable` o `AccountMismatch`) el Flow que sigue al pedido de ubicación de una
     oferta, derivar con un código, como H5, o contestar la frase del equipo.
  3. Su test.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** el usuario (qué contestar); H5 da el patrón.
- **Dónde vive:** `docs/features/whatsapp.md`, sin plan propio.

### W-5. La retención de un día contra los reintentos de un día

- **Estado:** sin terminar, heredado por confirmar.
- **Evidencia:** el plan de la bandeja, etapa 4: con `MessageRetentionDays` en 1, un archivo que reintenta sale de la retención
  antes de abandonarse. `WhatsAppMessageRetentionOptions.cs:19` (`Range(1, 3650)`) y `WhatsAppAcquisitionRetry` (cinco fallas y un
  día).
- **Qué falta:**
  1. Confirmarlo con un test.
  2. Si pasa, subir el mínimo o no vaciar lo que todavía reintenta.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie, si se confirma.
- **Dónde vive:** el plan de la bandeja, «Lo que queda de los arreglos».

### W-6. F22: WhatsApp nombra códigos de Formularios

- **Estado:** a medias.
- **Evidencia:** `WhatsAppAssistantAuthoringPolicy.cs:49-50` compara como texto `Forms.Definition.NameTaken`,
  `Forms.Catalog.NameTaken`, `Forms.Definition.CatalogUnavailable` y `Forms.Record.CatalogUnavailable` (comprobado), y cualquier
  otro `Forbidden` dice que la cuenta no puede armar. Los módulos sólo se cruzan por puertos neutrales.
- **Qué falta:**
  1. Que el puerto de autoría devuelva un motivo neutral.
  2. Que WhatsApp elija el texto por ese motivo.
  3. Su test, y sacar la nota de `docs/guides/quitar-formularios.md`.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md` (los menores de Formularios).
- **Lo nombran:** Formularios (parte técnica, menor 12 y desvío 1).

### W-7. Comentarios viejos del bot retirado y de «Colas en memoria»

- **Estado:** a medias.
- **Evidencia:** `WhatsAppInvitationChannel.cs:18` (dice que el bot manda el enlace), `WhatsAppOptions.cs:71,88` e
  `IWhatsAppInboundSignal.cs:4` hablan del bot retirado; hoy el toque de «Quiero entrar» sólo orienta a `/login`.
  `WhatsAppOptions.cs:72` cita la sección «Colas en memoria» de `backend.md`, que ya no existe.
- **Qué falta:** corregir los comentarios.
- **Depende de:** nada.
- **Tamaño:** trivial.
- **Decide:** nadie.
- **Dónde vive:** la fila «Deuda menor del backend» de `docs/plans/README.md`.
- **Lo nombran:** Núcleo (B.2) y WhatsApp (B).

### W-8. El estado del listado del chat, fuera de la URL

- **Estado:** a medias: un desvío con la decisión pendiente.
- **Evidencia:** front `WhatsAppChatPage.tsx:46-48`: la búsqueda, el filtro y la página van en `useState` (comprobado). La
  conversación ya va en `?conversacion=` y la búsqueda usa `SearchInput`.
- **Qué falta:**
  1. Elegir entre `usePagination` y `useFilters`, o una excepción escrita.
  2. Registrarlo en la feature del front.
  3. Un test.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** el usuario.
- **Dónde vive:** el plan de la bandeja.

### W-9. Contactos

- **Estado:** propuesto sin decidir (maqueta sin elegir).
- **Evidencia:** el plan de la bandeja, «Contactos», y la maqueta `design-lab/whatsapp-chat?screen=contactos` (front
  `whatsapp-chat-propuesta.md`). No hay rutas `api/whatsapp/contacts`, y el front sólo tiene `/whatsapp/chat` y
  `/whatsapp/asistente`.
- **Qué falta:**
  1. El tablero elegido.
  2. El permiso `whatsapp.contacts.read`.
  3. `GET /api/whatsapp/contacts` paginado, con conteos, y `GET /api/whatsapp/contacts/{id}`, con su lector.
  4. El listado y la ficha, con «Abrir chat».
  5. Inventario, OpenAPI, integración y visuales.
- **Depende de:** nada.
- **Tamaño:** mediano.
- **Decide:** el usuario.
- **Dónde vive:** el plan de la bandeja.
- **Lo nombran:** WhatsApp (A6) y Front (A14).

### W-10. Plantillas de Meta

- **Estado:** propuesto sin decidir.
- **Evidencia:** el plan de la bandeja, «Plantillas de Meta», y la maqueta `?screen=plantillas`. Hoy un aviso fuera de la ventana de
  24 horas falla: «esta etapa no agrega plantillas de Meta» (`docs/features/whatsapp.md`, avisos de reglas).
- **Qué falta:**
  1. El tablero.
  2. Un cliente de gestión con `WabaId`: crear, listar, editar y borrar, y consultar antes de reintentar.
  3. El permiso `whatsapp.templates.manage`.
  4. Decidir aparte si el chat o los avisos mandan una plantilla fuera de la ventana: una por formulario o una genérica, su
     categoría, quién paga y el registro del opt-in (decisión 9 del estudio de integraciones).
- **Depende de:** nada técnico. La necesitan la etapa 2 de Jira (Pr-1) y la API pública (Pr-3).
- **Tamaño:** grande.
- **Decide:** el usuario.
- **Dónde vive:** el plan de la bandeja.
- **Lo nombran:** WhatsApp (A7), Front (A14 y A21) y Propuestas y memoria (A1 y A3).

### W-11. La conexión del canal en pantalla

- **Estado:** propuesto sin decidir.
- **Evidencia:** el plan de la bandeja y la maqueta `?screen=conexion`. El permiso reservado `whatsapp.configure` no está declarado:
  `WhatsAppPermissions.cs` tiene `read`, `send` y `configureassistant`.
- **Qué falta:**
  1. El tablero.
  2. Un diagnóstico de sólo lectura, sin secretos.
  3. Su permiso.
- **Depende de:** nada.
- **Tamaño:** mediano.
- **Decide:** el usuario.
- **Dónde vive:** el plan de la bandeja.
- **Lo nombran:** WhatsApp (A8) y Front (A14).

### W-12. Un tope diario de invitaciones por WhatsApp

- **Estado:** propuesto sin decidir.
- **Evidencia:** `docs/features/administracion.md:33` («Las invitaciones por WhatsApp no los miran») y
  `verificacion-y-produccion.md:231`. No hay opción de tope.
- **Qué falta:**
  1. Decidir el tope y su costo.
  2. Una guarda como `EmailQuotaGuard` en el canal de WhatsApp.
  3. Textos y tests.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** el usuario.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md`, «Privacidad de la instalación de prueba».
- **Lo nombran:** Núcleo (A10).

### W-13. La forma de las burbujas del chat

- **Estado:** propuesto sin decidir (tercera ronda abierta).
- **Evidencia:** front `docs/design/whatsapp-bibliotecas-propuesta.md` («maquetas para evaluar, sin elección ni adopción»), sólo
  en `design-lab/whatsapp-chat/libraries`, y `visual-baseline.md`, «Propuestas abiertas».
- **Qué falta:**
  1. Elegir Actuales o Redondeadas, o retirar la comparación.
  2. Si cambia, el radio y la punta de `MessageBubble`, y sus referencias.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** el usuario.
- **Dónde vive:** el diseño del front.
- **Lo nombran:** WhatsApp (A15) y Front (A15).

### W-14. «Sugerir con IA» a pedido

- **Estado:** propuesto sin decidir.
- **Evidencia:** front `visual-baseline.md`, «Propuestas abiertas». El chat ya muestra la sugerencia del asistente con «Usar como
  borrador» (AS4, front `whatsapp.json:270-271`), pero sin un botón para pedirla. El plan del backend
  de sugerencias de texto quedó reemplazado por AS4 y se retiró el 2026-11-18.
- **Qué falta:**
  1. Decidir si se descarta o se programa el botón.
  2. Si se programa: el tablero, el botón y sus tests.
- **Depende de:** nada.
- **Tamaño:** mediano, si se programa.
- **Decide:** el usuario.
- **Dónde vive:** front `docs/design/visual-baseline.md`, «Propuestas abiertas».
- **Lo nombran:** Front (A16) y WhatsApp (C).

### W-15. Casos con tipo de entrada foto o documento

- **Estado:** diseñado sin programar, a medias.
- **Evidencia:** el plan de casos, «Etapa siguiente», punto 3 («cuando manda una foto de un ticket»), y AS2, que deja lugar para
  fotos y documentos. El tipo es `WhatsAppAssistantInputType { Any, Text, Audio }` (`WhatsAppAssistantAgentModels.cs:4`) y el plan
  de documentos no lo nombra.
- **Qué falta:**
  1. Decidir si un caso puede elegir foto o documento, ya que el circuito de documentos los carga por los formularios que leen
     documentos.
  2. Si sí: el tipo, el validador, el editor y lo que lee el modelo.
- **Depende de:** nada.
- **Tamaño:** mediano.
- **Decide:** el usuario, con la sesión de documentos.
- **Dónde vive:** el plan de casos.

### W-16. La etapa «Agentes»

- **Estado:** propuesto sin decidir, sin tablero ni código.
- **Evidencia:** el plan de casos, «Tercera etapa: agentes» (`:89-105`), fuera de lo implementado (`:256`). El modelo la prevé: una
  fila principal con índice único filtrado por `IsMain`, y AS1-A («cada uno abre esta misma pantalla desde una lista»). El front
  no tiene rutas de agentes.
- **Qué falta:**
  1. Los tableros del menú «Agentes», «Probar el agente», el ruteo y los pases.
  2. Decidir el ruteo (por número o canal, por tipo de contacto o con un clasificador) y los pases entre agentes o a una persona.
  3. Un plan con TDD sobre el motor único (ADR 0020).
  4. La calificación propia del clasificador.
- **Depende de:** el cierre de los casos del asistente (su piloto y la tarea 11).
- **Tamaño:** grande.
- **Decide:** el usuario.
- **Dónde vive:** el plan de casos.
- **Lo nombran:** WhatsApp (A11), Front (A18) y Propuestas y memoria (A20).

### W-17. Entrar con WhatsApp sin código

- **Estado:** propuesto sin decidir, sin tablero ni documento.
- **Evidencia:** sólo la memoria de los chats: un botón «Entrar con WhatsApp» que abre `wa.me` al bot con «Hola», que quedó para
  después de dibujarlo. Contaba con el enlace de ingreso del bot, pero el enlace se retiró el 2026-11-16 (B4) y `LoginLinkIssuer`
  ya no existe.
- **Qué falta:**
  1. El tablero.
  2. Decidir cómo entra la persona sin el enlace retirado; si vuelve un enlace, revisar las razones del retiro de B4.
  3. La revisión adversarial completa, porque es ingreso.
  4. Un plan y el código.
- **Depende de:** el módulo prendido y un número con el bot.
- **Tamaño:** mediano.
- **Decide:** el usuario.
- **Dónde vive:** sólo en la memoria de los chats (estado del ingreso con WhatsApp).
- **Lo nombran:** Propuestas y memoria (A9).

### W-18. Mandar por WhatsApp el enlace de la ubicación del negocio

- **Estado:** propuesto sin decidir, sin tablero ni plan.
- **Evidencia:** front `docs/design/informacion-del-negocio-guiada.md`, «Estado y siguientes pasos». El enlace lo arma
  `AiBusinessDetails.cs:9-12` (IA) y ningún archivo de WhatsApp lo usa.
- **Qué falta:**
  1. El tablero y la decisión de cuándo se manda.
  2. El backend, sobre todo, y sus tests.
- **Depende de:** nada.
- **Tamaño:** mediano.
- **Decide:** el usuario.
- **Dónde vive:** el diseño del front.
- **Lo nombran:** Front (A17).

### W-19. Instalaciones independientes y proyectos derivados

- **Estado:** propuesto sin decidir.
- **Evidencia:** el plan de la bandeja, «Instalaciones independientes y proyectos derivados». La guía
  `docs/guides/crear-proyecto-desde-la-base.md` no existe.
- **Qué falta:**
  1. Decidir si sigue valiendo frente al multitenant.
  2. La guía.
  3. El traslado de mejoras.
  4. La prueba con dos instalaciones.
- **Depende de:** el rumbo del multitenant (los traspasos): puede quedar obsoleto.
- **Tamaño:** mediano (la guía) y grande (la prueba).
- **Decide:** el usuario.
- **Dónde vive:** el plan de la bandeja.

## IA

El asistente de Inicio y su panel, la IA que arma formularios y catálogos, la conexión con el proveedor y lo que dejaron los
29 arreglos de la revisión. No cuentan: declarar los perfiles de texto, audio y documentos en cada instalación (es requisito de
la prueba a mano) y los términos de Places (van con producción). El tablero de estados del asistente espera el lienzo (P-11).
I18, I19, I20 y el ensayo de carga van [aparte](#arquitectura-para-más-adelante).

### I-1. La IA propone «Renglones» (propose-form@3)

- **Estado:** diseñado sin programar.
- **Evidencia:** la decisión vive sólo en `docs/plans/2026-11-04-ia-arma-formularios-y-catalogos.md` («Resueltas por defecto»:
  cuando estén programados, lo que se repite se propone como Renglones). Los renglones están programados (traspaso 12), pero los
  esquemas de `propose-form@1` y `@2` (`FormAuthoringAssistantCodec.cs:22` y `FormAuthoringAssistantCodec.Document.cs:13`) sólo
  admiten de `shortText` a `document`, sin bloque ni preguntas `repeated` (comprobado). En `home-documents`, el oráculo admite
  que una planilla en papel salga con las columnas repetidas por renglón.
- **Qué falta:**
  1. Una versión nueva de la capacidad (`propose-form@3`) con el bloque y sus preguntas `repeated`, validada con las reglas de
     la autoría (RG1-A y RG6-A).
  2. Registrarla un despliegue antes de ofrecerla (regla I4) y dejar la 2 registrada.
  3. La tarjeta y «Ver cómo queda» en Inicio, el resumen de WhatsApp y «Crear borrador» con el bloque.
  4. Que los lectores que comparan la versión exacta (`FormDocumentAuthoringPolicy`, `Handles`) acepten la nueva.
  5. Los casos del arnés y la corrida real de `ia-authoring`, `home-documents` y `forms-documents`.
- **Depende de:** tu visto bueno para la corrida real. Conviene que viaje con I-3 e I-4 en una sola calificación.
- **Tamaño:** mediano.
- **Decide:** nadie; sólo el visto bueno de la corrida.
- **Dónde vive:** el plan de la IA que arma; al retirarlo, el índice de planes o `docs/plans/formularios/README.md`.

### I-2. Front: los tres datos del bootstrap del asistente, requeridos

- **Estado:** a medias.
- **Evidencia:** front `src/features/artificial-intelligence/api/assistant.ts:151-155` deja `attachments`, `documents` y
  `documentReading` en `.optional()` «hasta la exportación conjunta» (comprobado). La captura `ai-assistant-http/bootstrap.json`
  los trae desde `9aa157ae` (17/11). La Etapa 2 pedía volver requerido `attachments`.
- **Qué falta:**
  1. Quitar los tres `.optional()` y el comentario.
  2. Cambiar los tests que prueban su ausencia (`assistant.test.ts`, cerca de las líneas 110 a 125) y los dobles que arman un
     bootstrap sin ellos.
  3. Los tests de la feature y el de contrato.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie.
- **Dónde vive:** `docs/plans/2026-11-09-ia-arma-formularios-etapa-2.md`.
- **Lo nombran:** IA (A2) y Propuestas y memoria (A11).

### I-3. La próxima versión de instrucciones de Inicio (I15)

- **Estado:** diseñado sin programar: decidido, en espera de otra razón para calificar.
- **Evidencia:** el plan del panel, etapa 1: `Prompt.SourceTime` dice «zona del negocio» en Inicio aunque sea la del perfil, y
  faltan pedir fechas y números sólo con números y la frase del permiso. O14: `KnownInstructions` acepta «la 1 o la actual»
  (`AiAssistantChannelPolicy.cs:82`), una trampa para esta suba.
- **Qué falta:**
  1. `home-system@3`, y la de canales si cambia.
  2. Registrar la anterior y corregir `KnownInstructions` (O14).
  3. La corrida real de los sets de Inicio.
- **Depende de:** la próxima calificación por otro motivo, por ejemplo I-1.
- **Tamaño:** chico, más la corrida.
- **Decide:** nadie.
- **Dónde vive:** `docs/plans/2026-10-04-panel-conversaciones-inicio.md`.

### I-4. Lo que la IA no propone de los tipos nuevos del editor

- **Estado:** propuesto sin decidir: un hueco sin decisión escrita.
- **Evidencia:** el esquema de I-1 tampoco tiene la pregunta «Ubicación» (UB1-A), los títulos de sección, elegir primero por un
  campo ni los cálculos.
- **Qué falta:**
  1. Decidir cuáles debería proponer.
  2. Cada uno, una versión nueva con su calificación, como I-1 (pueden ir juntos).
- **Depende de:** nada.
- **Tamaño:** mediano cada uno, como I-1.
- **Decide:** el usuario.
- **Dónde vive:** el plan de la IA que arma; el informe propone llevarlo, con I-1, al índice o a `docs/plans/formularios/README.md`.

### I-5. Los valores del asistente: plazos de retención y cuotas

- **Estado:** propuesto sin decidir.
- **Evidencia:** pendientes 1 y 7 del plan del panel. `RetentionDays` es 30 y es el techo del código (`AiAssistantOptions.cs:34`).
  Hoy rigen 10 y 100 pedidos por día, 4 en curso, 4 pendientes y 30 días (`AiAssistantLimits.cs`). La tarea 18 (I3) resolvió la
  metadata de Inicio, no los plazos.
- **Qué falta:**
  1. Aprobar los plazos de contenido, metadata, comprobantes, deduplicación y backups.
  2. Aprobar las cuotas y la capacidad, con los datos del piloto y del ensayo de carga.
  3. Escribirlos en la feature, en `configurar-ia.md` y en la política pública, y sacar la marca de «propuesta».
  4. Si un plazo pasa de 30 días, cambiar el techo, con pruebas.
- **Depende de:** el piloto y el ensayo de carga.
- **Tamaño:** chico en código; la decisión es lo grande.
- **Decide:** el usuario.
- **Dónde vive:** el plan del panel.
- **Lo nombran:** IA (A3 y A7). Se cruza con N-5.

### I-6. La purga física de terminales y de la metadata de los canales

- **Estado:** a medias.
- **Evidencia:** pendiente 2 del plan del panel. Para Inicio lo resolvió I3 (`1fc155d9` y siguientes). Falta el registro mínimo
  para no repetir un UUID ni reprocesar webhooks y envíos de WhatsApp (referencias, estado de lectura e IDs de Meta).
- **Qué falta:**
  1. Definir el registro mínimo con WhatsApp.
  2. TDD y su migración.
  3. Validar el replay antes de borrar IDs de Meta.
  4. Un ADR, si cambia una regla de producto.
- **Depende de:** I-5 y la revisión de WhatsApp.
- **Tamaño:** mediano.
- **Decide:** el usuario (qué se conserva).
- **Dónde vive:** el plan del panel.

### I-7. La purga de WhatsApp por lotes

- **Estado:** diseñado sin programar.
- **Evidencia:** pendiente 4 del plan del panel. `WhatsAppMessageRetentionRepository.cs:32` y `:79` vacían el contenido con un
  `ExecuteUpdate` sin lote ni índice parcial.
- **Qué falta:**
  1. Lotes por fecha e Id, con un índice parcial del contenido presente.
  2. La salida durable sin `Pending` ni `Sending`.
  3. Su migración propia.
  4. TDD e integración.
- **Depende de:** nada.
- **Tamaño:** chico a mediano.
- **Decide:** nadie. Lo programa WhatsApp o IA.
- **Dónde vive:** el plan del panel.
- **Lo nombran:** IA (A5). La gran escala de WhatsApp también pide la retención por lotes.

### I-8. I6: el corte que falla rápido después de varios rechazos

- **Estado:** propuesto sin decidir («queda para más adelante»).
- **Evidencia:** la revisión de IA (I6) y el plan del panel, «Lo que quedó de los arreglos».
- **Qué falta:**
  1. Decidir si vale.
  2. Si sí: un corte por proveedor, con su estado y su texto, sin repetir pedidos facturables.
- **Depende de:** nada.
- **Tamaño:** chico a mediano.
- **Decide:** el usuario.
- **Dónde vive:** el plan del panel.

### I-9. I7: el aviso de clave ilegible para quien sólo tiene `ai.read`

- **Estado:** propuesto sin decidir (plan del panel, etapa 2: «quedó para después»).
- **Evidencia:** la revisión de IA (I7) y el plan del panel.
- **Qué falta:**
  1. Que la lectura de la conexión diga si la clave guardada se puede leer.
  2. Que la pantalla lo muestre sin «Reemplazar clave».
  3. El contrato aditivo con el front y sus tests.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** el usuario.
- **Dónde vive:** el plan del panel.

### I-10. Observaciones menores de la revisión de IA

- **Estado:** propuesto sin decidir, fuera del alcance de los arreglos. Comprobadas en el código el 18/11.
- **Evidencia, por quién decide:**
  - Sin decisión nueva: O1, `AddAiAssistantProvidersAndProtection` se llama dos veces
    (`ArtificialIntelligenceInfrastructureRegistration.cs:43,70`); O4, la prueba de conexión llama incierto a un corte antes de
    enviar (`LlmHttpTransport.cs:59-61`) y le falta la clasificación de I6; O8, el nombre del archivo quita los controles pero no
    los bidireccionales (`AiAssistantHomeAttachmentPolicy.cs:56`); O9, sólo la ruta de documentos prueba su límite de cuerpo.
    O14 va con I-3.
  - Del usuario: O11, el aviso de cierre es un solo puerto que WhatsApp reemplaza, y Formularios no recibe aviso; O12, no se
    fuerza la herramienta terminal (`tool_choice` en `auto`), y cambiarlo pide recalificar.
  - Del piloto: O6 (una sola ventana de envíos), y O2 y O13 (el catálogo y los tiempos con una cuenta real).
  - Sin dueño en el informe: O3 (una zona desconocida sin señalarla); O10 (la pantalla «Asistente» de WhatsApp lee los hechos
    sin `ai.business.read`, `WhatsAppAssistantAgentService.cs:21`, contra el comentario de `AiBusinessKnowledgeSource`); de la
    etapa 3, la prueba de la tanda con un solo carril, la clave de la copia independiente de la persona y la copia huérfana de
    dos subidas con distinto tipo; de la etapa 6, el veredicto del anillo en el bootstrap y en el chequeo previo a subir un
    documento.
- **Qué falta:** corregir las de la primera lista, cada una con su test; decidir las tuyas; mirar las del piloto con sus datos.
- **Depende de:** el piloto, para O2, O6 y O13.
- **Tamaño:** chico cada una.
- **Decide:** mixto, como dice la lista.
- **Dónde vive:** el plan del panel («Lo que quedó de los arreglos») y la revisión de IA.

## ARCA

Las seis etapas y las 34 tareas de los arreglos están programadas; H11 lo cubrió F2 de Formularios (`63edb23e`). En el código
sólo existen las familias `Connection`, `Registry` e `Invoicing`, y los tipos 1, 3, 6, 8, 11 y 13 (`ArcaVoucherTypes.cs:8-13`). Lo
que se prueba con ARCA real va [aparte](#verificaciones-con-servicios-reales); H33, H35, H36 y H37, en la
[gran escala](#arquitectura-para-más-adelante).

### A-1. CA1: «ARCA» y «Actualizado» en el listado de catálogos

- **Estado:** diseñado sin programar.
- **Evidencia:** el tablero CA1 dibujó en el listado las columnas «Origen» («ARCA») y «Actualizado» («hace 2 h»). Al programarlo
  (2026-11-11), el contrato de un catálogo `External` no traía el nombre de su origen ni, en el listado, la fecha, y formularios
  no nombra módulos (front `docs/design/arca-factura-c.md:70-79`). El listado dice «De otro sistema» y no tiene la columna (front
  `CatalogListTable.tsx:54`, comprobado). El editor sí muestra la fecha (`CatalogEditorPage.tsx:187`) y el detalle la trae
  (`FormCatalogStoredRow.FeedRefreshedAtUtc`).
- **Qué falta:**
  1. Que `IStructuredCatalogFeed` declare el nombre de su origen en los dos idiomas: es aditivo, y formularios sigue sin nombrar
     al módulo.
  2. Que el listado de catálogos devuelva ese nombre y la fecha de la última actualización.
  3. En el front, «Origen» con ese nombre y la columna «Actualizado», con sus tests y capturas.
- **Depende de:** nada.
- **Tamaño:** chico según el informe de ARCA; mediano según el del front.
- **Decide:** nadie, según el informe de ARCA, porque CA1 no tenía opciones. El del front pide tu visto bueno para el nombre
  neutral que manda el módulo.
- **Dónde vive:** front `docs/design/arca-factura-c.md` (CA1) y `docs/plans/2026-11-09-arca-etapa-3-factura-c.md`, hasta que se
  retire.
- **Lo nombran:** ARCA (A2) y Front (A13). Toca Formularios y el front.

### A-2. Las dos escrituras del robot usan el límite de lectura

- **Estado:** a medias: un desvío.
- **Evidencia:** la regla tiene límites de lectura, de escritura y `arca-remote` (`docs/features/arca.md`, «Límites por
  operador»). `manual-completion` y `platform-rearm` escriben y heredan el `ReadPolicy` de la clase (`ArcaRobotController.cs:22`;
  sin atributo en `:47` ni en `:57`, comprobado). La revisión lo anotó para `manual-completion` en O9, y `platform-rearm`, que llegó
  con H5, repite el patrón.
- **Qué falta:**
  1. Un `[EnableRateLimiting(WritePolicy)]` en cada una.
  2. Su test.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie.
- **Dónde vive:** la revisión de ARCA (O9) y la regla de ARCA.

### A-3. H22: medir Chromium y, si no entra, un lado por vez

- **Estado:** a medias.
- **Evidencia:** lo programado está en `e322d9b5`: el driver que se cae se recrea (`PlaywrightArcaDelegationRobot.cs:163-176`) y el
  chequeo de arranque abre y cierra Chromium (`ArcaRobotBrowserCheck.cs:35-36`). La guía deja la medición en blanco
  (`docs/guides/robot-arca.md:130`, «Memoria y CPU de una pasada»).
- **Qué falta:**
  1. En la corrida real, con la imagen publicada, medir con `docker stats` el pico de memoria y de CPU de una pasada de cada lado
     y de las dos juntas.
  2. Anotar los números en la guía del robot.
  3. Si las dos juntas no entran en los límites del despliegue, que cada host corra un lado por vez (un semáforo entre el worker
     del cliente y el de la plataforma), con su test.
- **Depende de:** la corrida real del robot y la imagen con Chromium publicada.
- **Tamaño:** chico la medición; mediano si hace falta el límite.
- **Decide:** nadie: H22 ya fijó «si no entra, un lado por vez».
- **Dónde vive:** la guía del robot y la hoja de ruta de ARCA (la nota de la etapa 7).

### A-4. H11 avisa sin el motivo

- **Estado:** propuesto sin decidir.
- **Evidencia:** H11 pedía avisar a quien cargó, con el motivo en su idioma, cuando la emisión falla. F2 manda un aviso general,
  una vez por acción frenada: «La regla de tu respuesta {0} de «{1}» quedó frenada…» (`FormActionStallService.cs:69-73`,
  `Responses.resx:35`). No dice el rechazo de ARCA, que sí se ve en la zona «Comprobante».
- **Qué falta:**
  1. Decidir si el aviso general alcanza.
  2. Si no: sumar al aviso el motivo que ya da el emisor (`FailureDetail`, por ejemplo «10015 · …») cuando la acción frenada es
     una emisión, con su texto es/en y un test.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** el usuario. El código es de Formularios.
- **Dónde vive:** la hoja de ruta de ARCA (H11).

### A-5. Observaciones menores de la revisión de ARCA

- **Estado:** propuesto sin decidir.
- **Evidencia:** `docs/reviews/2026-11-14-arquitectura-arca.md`: siguen O1 a O8, O11, O12 y lo que queda de O9. Entre ellas:
  - O4: la prueba de una renovación en rojo guarda los datos del padrón (`ArcaRegistryServiceDefinition.cs:32`,
    `ArcaConnectionTestService.cs:44`);
  - O5: la CUIT se valida distinto en el front;
  - O11: el consentimiento del robot queda sólo en la última corrida;
  - O12: lo confirmado en «¿La emito?» no se fija: con la fecha vacía, sale con la del día en que se toca «Emitir»;
  - de O9, los menores del robot que no entraron con O10 (A-2 es uno).
- **Qué falta:** decidir cuáles se hacen; cada uno, con su test.
- **Depende de:** nada.
- **Tamaño:** chico cada una.
- **Decide:** el usuario.
- **Dónde vive:** la revisión de ARCA.

### A-6. La purga de los comprobantes al vencer el plazo fiscal (H25)

- **Estado:** propuesto sin decidir.
- **Evidencia:** `docs/features/arca.md:305-306` (la purga al vencer queda para después) y `verificacion-y-produccion.md:210-211`.
  Mientras el módulo esté instalado, los PDF de `arca-vouchers/` no se borran nunca.
- **Qué falta:**
  1. Que el negocio fije el plazo con su contador.
  2. Un proceso que purgue el comprobante, su pedido y su PDF vencidos, con su prueba.
  3. La regla escrita.
- **Depende de:** el plazo.
- **Tamaño:** mediano.
- **Decide:** el usuario.
- **Dónde vive:** la regla de ARCA y `docs/plans/verificacion-y-produccion.md`.
- **Lo nombran:** ARCA (A7). Se cruza con N-5.

### A-7. Los comprobantes que quedaron fuera de las etapas 3 y 4

- **Estado:** propuesto sin decidir.
- **Evidencia:** «Fuera de esta vuelta» de la etapa 3 (`2026-11-09-arca-etapa-3-factura-c.md:38`) y «Fuera de la etapa» de la 4
  (`2026-11-09-arca-etapa-4-factura-a-y-b.md:280`). `ArcaVoucherKind` sólo tiene `Invoice` y `CreditNote`.
- **Qué falta, cada uno con su decisión:**
  1. La A con leyenda (tipos 51 a 54, RG 5762): si ARCA no autoriza al emisor a la A, rechaza con el 10000. Es el riesgo más
     concreto: se ve recién con la primera factura real de un responsable inscripto.
  2. La nota de crédito parcial y las notas de débito.
  3. Los tributos (`ImpTrib`: percepciones e impuestos internos) y «Otros impuestos» del recuadro de la RG 5614, hoy en 0.
  4. Otras monedas, la B al exterior, la B al no categorizado con la CUIT genérica (tributo 13) y la exportación.
  5. La factura de crédito MiPyME, el CAEA, facturar al cobrar y cargar por audio con la IA.
- **Depende de:** la homologación de la A y la B, que confirma lo que hoy sale del manual.
- **Tamaño:** de mediano (la A con leyenda) a grande (MiPyME y exportación).
- **Decide:** el usuario.
- **Dónde vive:** los planes de las etapas 3 y 4; al recortar la hoja de ruta, quedan en ella.

### A-8. Las otras familias de la hoja de ruta

- **Estado:** propuesto sin decidir, sin diseño, tableros ni plan.
- **Evidencia:** la fila «Después» y la tabla de servicios de `docs/plans/2026-10-31-arca-formularios-estandar.md:79`. En el código
  no hay ninguna (`ArcaServiceCode.cs:4` nombra `wscpe` sólo como ejemplo).
- **Qué falta, por familia:**
  1. Elegirla: Hacienda (WSLSP y el remito cárnico WSREMCARNE); Granos (WSCPE y WSLPG); Consultas (WSCDC, apócrifos y los padrones
     A4, A10, A13 y A100); Facturación (wsfexv1, wsmtxca, bienes de capital y turismo); otros sectores, si un cliente los pide. El
     DT-e es de SENASA, aparte.
  2. Su diseño, sus tableros, un ADR si hace falta y su plan.
  3. Programarla como carpeta propia (ADR 0025), con su `IArcaServiceDefinition`, su cliente SOAP, su estándar de la biblioteca y
     su guía para quitarla.
- **Depende de:** la homologación de la conexión. Nada del código.
- **Tamaño:** grande cada una.
- **Decide:** el usuario (cuál primero y si se hace).
- **Dónde vive:** la hoja de ruta de ARCA. Se cruza con D-7 (los comprobantes recibidos).

### A-9. Lo que quedó fuera de las etapas 5 y 6

- **Estado:** propuesto sin decidir.
- **Evidencia:** «Fuera» del plan de la etapa 5 y «Fuera de la etapa» del de la 6 (`2026-11-09-arca-etapa-6-robot.md:269`).
- **Qué falta, cada uno con su decisión:**
  1. Revocar la delegación desde la app.
  2. Crear con el robot el certificado de la empresa (modo 1).
  3. Pausar el robot para todas las instalaciones cuando se rompe; se evalúa en el multitenant.
- **Depende de:** nada.
- **Tamaño:** mediano cada uno.
- **Decide:** el usuario.
- **Dónde vive:** los planes de las etapas 5 y 6.

## Front (lo transversal)

Lo que cruza varias áreas en `ArquitecturaBaseFront`: los contratos, las escenas visuales, el fundamento visual y lo heredado.
Lo que es de una sola área quedó en su sección: D-1 y D-2 en Documentos, A-1 en ARCA, W-8, W-9 a W-11, W-13, W-14, W-16 y W-18 en
WhatsApp, y F-4 y F-7 en Formularios.

### P-1. Lectores de contrato de las cohortes del 17/11

- **Estado:** a medias: los cuerpos están exportados y sólo se verifican por nombre y hash.
- **Evidencia (comprobada):**
  - `src/features/business-forms/api/httpContract.test.ts:58`: «app-only, document-reading, folders, library and location-contact
    arrived with the 2026-11-17 export, still without a reader»;
  - `src/features/whatsapp/api/httpContract.test.ts:87`: lo mismo para la ubicación y el contacto compartidos (W3);
  - `src/test/httpCohort.ts:33-35`: `dashboard` y `documents`, «still without a reading consumer»;
  - las capturas de ubicación de `forms-entries-http` sólo figuran en su `provenance.json`;
  - no existen `*HttpContract.test.ts` para la bandeja, las propuestas de DOC6, el tablero ni la ubicación de la carga
    (`entriesLocationHttpContract.test.ts`, que nombran `locationQuestion.test.ts:12` y `FormEntryForm.location.test.tsx:11`), y
    los tests de `receivedDocuments`, `receivedDocumentRows`, `documentFormProposals` y `dashboard` siguen con cuerpos sintéticos.
- **Qué falta:**
  1. `forms-http`: un test por familia que lea cada cuerpo con su guard zod: `app-only` (6: borrador, detalle, listado, recibo
     `Published`, estado y sugerencias con `AppOnly`), `folders` (5: árbol, menú, carpetas de un elemento y conteo antes de
     borrar), `document-reading` (8), `library` (2) y `location-contact` (2).
  2. `receivedDocumentsHttpContract.test.ts`: los 26 cuerpos `document*` y `documents*` de `forms-documents-http` contra los guards
     reales (lista, conteos, grupos, detalle es/en, opciones, la extracción en sus cinco estados, la respuesta, el 409 de revisión,
     el remitente inactivo, las filas y las repetidas).
  3. `documentFormProposalsHttpContract.test.ts`: los ocho `form-proposal-*`.
  4. `dashboardHttpContract.test.ts`: los diez `dashboard-*`.
  5. `entriesLocationHttpContract.test.ts`: `entry-`, `sheet-`, `detail-` y `correction-location-es` de `forms-entries-http`.
  6. Sumar a los `*HttpContract` que ya existen (`audioHttpContract`, `formActionHttpContract` y `responseHttpContract`) los hilos
     de WhatsApp (`es/thread-location-*` y `es/thread-shared-*` de `whatsapp-forms-http`; `document-thread`, `document-why-thread`,
     `contact-thread` y `spreadsheet-loads-thread` de `whatsapp-audio-http`) y los detalles `detail-shared-whatsapp-es`,
     `detail-location-whatsapp-es` y `rows-*` de `forms-responses-http`.
  7. Quitar los comentarios «still without a reader» y «llega con la exportación conjunta» de los tests, de `dashboardFixtures.ts`,
     de las páginas y de `src/features/business-forms/api/AGENTS.md:100-102`.
  8. Correr los tests de las features.
- **Depende de:** nada: la exportación ya está.
- **Tamaño:** mediano (más de 80 capturas).
- **Decide:** nadie: es la regla de contratos de `AGENTS.md`.
- **Dónde vive:** front `src/test/AGENTS.md`, el plan de documentos («Lo que falta») y el de «sólo en la app».
- **Lo nombran:** Formularios (núcleo, A1), Documentos (A1), Front (A4) y Propuestas y memoria (que los daba por leídos).

### P-2. La escena de `test:visual` de «Por resolver» (F2)

- **Estado:** sin terminar.
- **Evidencia:** ninguna escena de `src/test/visual` busca «Por resolver» ni `unresolved` (comprobado). La pestaña está en
  `FormResponsesPage.tsx:107-110`.
- **Qué falta:**
  1. Sumar la escena a `responses.spec.ts`, o una propia, con la pestaña con y sin pendientes y la columna «Reglas».
  2. es/en a 1440, 390 y 320.
  3. Revisar las referencias y guardarlas.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie.
- **Dónde vive:** front `docs/design/visual-baseline.md` (mapa de tableros, F2).

### P-3. Las escenas de `test:visual` de la pregunta «Ubicación»

- **Estado:** sin terminar.
- **Evidencia:** en `src/test/visual` no hay escena de «Usar mi ubicación», del detalle con el mapa ni de la tarjeta del pedido en
  el chat (comprobado); sólo `business-guided` (el negocio) y `formLocationContact` (W3) tocan ubicaciones.
- **Qué falta:**
  1. Los estados de UB3-A en la carga: pedir permiso, esperando, lista, sin permiso y error.
  2. La planilla y el detalle de UB4-A.
  3. La tarjeta de W1-A en el chat.
  4. Las referencias, revisadas.
- **Depende de:** nada.
- **Tamaño:** mediano.
- **Decide:** nadie.
- **Dónde vive:** front `docs/design/ubicacion-en-formularios.md` y `visual-baseline.md`.

### P-4. El catálogo visual con «+18 %»

- **Estado:** a medias.
- **Evidencia (comprobada):** `src/test/visual/CatalogCharts.tsx:36` escribe a mano «+18 % que…» y «+21 % que…», con espacio,
  contra la regla del porcentaje pegado (`visual-baseline.md`, «Números con la región de cada idioma»). `formDashboard.spec.ts:56`
  acepta los dos (`\+18\s?%`). El componente real lo hace bien (`dashboard.ts:224-230`, con `style: "percent"`).
- **Qué falta:**
  1. Escribir «+18%» y «+21%», o armarlos con `formatPercent`.
  2. Cerrar la expresión del spec a `\+18%`.
  3. Rehacer las referencias con `--update-snapshots=all` y mirar los PNG; `charts.spec.ts` no tiene referencia versionada.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie sobre la regla, que ya fija el porcentaje pegado. La memoria pide que mires las referencias rehechas antes de
  guardarlas.
- **Dónde vive:** front `docs/design/visual-baseline.md`.
- **Lo nombran:** Front (A5 y el desvío 2) y Propuestas y memoria (A12).

### P-5. `cn` importado del paquete en tres componentes propios

- **Estado:** a medias: un desvío.
- **Evidencia (comprobada):** la regla dice que `cn` sale de `shared/lib/utils` (front `docs/architecture.md`, «Reglas»).
  `RuleMessageField.tsx`, `shared/ui/MultiSelect.tsx` y `shared/ui/WideTable.tsx` importan `from "cn"`. Los otros catorce que lo
  hacen son generados de shadcn o el propio `utils.ts`.
- **Qué falta:** cambiar los tres imports. No tiene efecto visible: es la misma función.
- **Depende de:** nada.
- **Tamaño:** trivial.
- **Decide:** nadie.
- **Dónde vive:** front `docs/architecture.md`.

### P-6. Mensajes: la recuperación incierta en los flujos de código y de redirects

- **Estado:** a medias.
- **Evidencia:** front `docs/plans/2026-09-30-unificacion-mensajes.md` (estado al 2026-11-18). El emisor y el límite de imports
  están hechos: sólo `shared/ui/notifications.ts` y `shared/ui/sonner.tsx` importan Sonner, y lo exige
  `designArchitecture.test.ts:19-21`.
- **Qué falta:**
  1. Revisar contra el contrato el ingreso y el registro con código, el regreso de Google y `VerifyDestinationDialog`.
  2. Cubrir con tests lo que falte: canal, cantidad, borrador, foco y los dos idiomas.
  3. El 429 centralizado y los otros consumidores que nombra `verificacion-y-produccion.md`.
  4. Marcar las casillas del plan con evidencia y retirarlo.
- **Depende de:** nada.
- **Tamaño:** mediano.
- **Decide:** nadie.
- **Dónde vive:** front `docs/plans/2026-09-30-unificacion-mensajes.md`.
- **Lo nombran:** Front (A6), Núcleo (A12) y `verificacion-y-produccion.md` («Adopción visual y mensajes»).

### P-7. La auditoría de accesibilidad móvil, los objetivos táctiles y los campos fuera de `FormField`

- **Estado:** sin terminar.
- **Evidencia:** front `visual-baseline.md`, «Responsive y accesibilidad mínimos» (los controles compactos y el volver de `Page` no
  crecen todos a 44 px en móvil) y «Pantallas por completar», puntos 1 y 4. Según `verificacion-y-produccion.md`, `Button` e
  `IconButton` compactos conservan 30 y 26 px, y `Page.backTo`, una caja de 24 px.
- **Qué falta:**
  1. Recorrer las pantallas a 320 y 390, con teclado y con lector.
  2. Llevar a 44 px los controles compactos y el volver de `Page` en móvil.
  3. Buscar campos dibujados fuera de `FormField` y terminar los avisos.
  4. Registrar el resultado.
- **Depende de:** nada.
- **Tamaño:** grande.
- **Decide:** nadie.
- **Dónde vive:** front `visual-baseline.md` y `docs/plans/verificacion-y-produccion.md` («Adopción visual y mensajes»).
- **Lo nombran:** Front (A7), Núcleo (A12) y Propuestas y memoria (A18).

### P-8. El panel de conversaciones móvil en el CSS compilado

- **Estado:** sin terminar, heredado por confirmar (2026-10-12).
- **Evidencia:** front `visual-baseline.md`, «Pendiente de compilación CSS»: Tailwind convierte `translate: 0 0` en
  `transform: translate(0)` y el panel móvil queda corrido en `dist`. No hay en `src` otra marca del arreglo.
- **Qué falta:**
  1. Mirar el panel en el build (`npm run preview`) a 390.
  2. Si sigue corrido, arreglarlo en la pieza común.
  3. Quitar la sección del fundamento.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie.
- **Dónde vive:** front `visual-baseline.md`.

### P-9. La lista heredada «Con la 2B» (ajustes de K6 y K7)

- **Estado:** sin terminar, heredado por confirmar.
- **Evidencia:** front `docs/design/formularios-whatsapp.md` la da por pendiente; el detalle está en
  `docs/reviews/2026-10-14-code-review-etapa-2-k6-k7.md:59-80`. Nadie la contrastó punto por punto.
- **Qué falta:**
  1. Contrastar cada punto con el código.
  2. Programar lo que siga abierto.
  3. Decidir el corte de la bandeja de WhatsApp, que pasó de `md` a `xl` sin tablero: volver a `md` o dibujarlo.
- **Depende de:** la revisión punto por punto.
- **Tamaño:** chico a mediano.
- **Decide:** nadie, salvo el corte de la bandeja, que es del usuario.
- **Dónde vive:** front `docs/design/formularios-whatsapp.md` y la revisión del 2026-10-14.

### P-10. Dos tests del front que fallan solos por la carga perezosa

- **Estado:** sin terminar, heredado por confirmar.
- **Evidencia:** el cierre del plan de arreglos de formularios dice que el primer test de `FormPendingApprovals.regressions` y el
  de `FormResponseFullPage` fallan por la carga perezosa si corren solos o con la máquina cargada, y que no se tocaron. Pero el
  front `1e877620` (17/11, antes de la corrida del cierre) ya hizo que el primer test de respuestas, pantalla completa y
  aprobaciones espere hasta 10 s.
- **Qué falta:**
  1. Correr los dos solos, con la máquina quieta y con carga.
  2. Si fallan, darles la misma espera que `FormDashboard`.
  3. Anotarlo en la guía de tests del front.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** nadie.
- **Dónde vive:** la guía de tests del front (el plan de arreglos se retiró el 2026-11-18).

### P-11. El lienzo «Sistema visual» es de otra cuenta

- **Estado:** sin terminar, bloqueado.
- **Evidencia:** el lienzo y la biblioteca «ArquitecturaBase UI» son de otra cuenta y no se abren desde estas sesiones. Front
  `visual-baseline.md` repite «falta pasar el tablero al lienzo del sistema visual» en RO1-A, PF1-A, IA1-A, UD, DL, OR1-A, F2, AC,
  SA y UB, y los planes de Formularios lo dicen para CW1-B a CW16, CP1-A a CP5-A, LD1-A a LD3-A y SA1-A a SA5. El tablero
  `Main.dc.html` todavía dibuja el menú anterior, a la biblioteca le faltan piezas de `shared/ui` (FilterBar, StatusDot, Avatar,
  FormError, Surface y Banner) y el tablero de estados del asistente (`docs/features/inteligencia-artificial.md`) espera lo mismo.
- **Qué falta:**
  1. Decidir: compartir el lienzo con la cuenta de Claude Code o entrar con la dueña; mudarlo a un lienzo nuevo; o abandonarlo y
     dar por buenos los lienzos aparte y las capturas de `docs/design/evidence`.
  2. Si se pasa: copiar los tableros de los lienzos aparte y alinear la biblioteca con `shared/ui`.
- **Depende de:** el usuario.
- **Tamaño:** mediano, si se pasa.
- **Decide:** el usuario.
- **Dónde vive:** front `visual-baseline.md` (mapa de tableros) y `docs/plans/verificacion-y-produccion.md` («Adopción visual y
  mensajes»).
- **Lo nombran:** Formularios (núcleo, A11), Front (A12), IA (fuera de su conteo) y Propuestas y memoria (A16).

### P-12. El responsive dibujado

- **Estado:** sin terminar: el diseño está a medias.
- **Evidencia:** front `formularios-whatsapp.md` (faltan 320 y 390, y el inglés del listado, del editor y del diálogo de envío);
  `ubicacion-en-formularios.md` (el chat a 320 no se dibujó); `visual-baseline.md`, «Pantallas por completar», punto 1 (tablas
  angostas y objetivos táctiles de todas las áreas).
- **Qué falta:**
  1. Dibujar esos anchos en el lienzo que se elija en P-11.
  2. Compararlos con las capturas de `test:visual`.
  3. Corregir lo distinto.
- **Depende de:** P-11.
- **Tamaño:** mediano.
- **Decide:** el usuario (dónde se dibuja).
- **Dónde vive:** front `visual-baseline.md` y `docs/plans/verificacion-y-produccion.md`.
- **Lo nombran:** Front (A11), Núcleo (A12) y Propuestas y memoria (A18).

### P-13. Selección y acciones masivas

- **Estado:** propuesto sin decidir.
- **Evidencia:** front `visual-baseline.md`, «Pantallas por completar», punto 2: espera un contrato del backend, y el tablero, hecho
  con `<span>`, es sólo referencia visual.
- **Qué falta:**
  1. Elegir dónde hace falta.
  2. El contrato del backend.
  3. El tablero y el código.
- **Depende de:** el contrato.
- **Tamaño:** sin estimar en los informes.
- **Decide:** el usuario.
- **Dónde vive:** front `visual-baseline.md`.
- **Lo nombran:** Front (A22) y Propuestas y memoria (A18).

### P-14. El Inicio autenticado completo, los estados 403 y 404 y la marca real

- **Estado:** propuesto sin decidir, heredado por confirmar.
- **Evidencia:** front `visual-baseline.md`, «Pantallas por completar», punto 3. Nadie lo contrastó con el Inicio de hoy, que ya
  tiene el asistente.
- **Qué falta:**
  1. Confirmar qué falta.
  2. Los tableros.
  3. El código.
- **Depende de:** nada.
- **Tamaño:** sin estimar en los informes.
- **Decide:** el usuario.
- **Dónde vive:** front `visual-baseline.md`.
- **Lo nombran:** Propuestas y memoria (A18).

### P-15. Decisiones chicas de diseño heredadas

- **Estado:** propuesto sin decidir, heredado por confirmar.
- **Evidencia:**
  - `verificacion-y-produccion.md` («Adopción visual y mensajes»): el rol `User` preseleccionado, la invitación marcada, la
    verificación de medios y el formato visible del número;
  - los avisos (AV1-A): la acción va a la derecha y rellena, y el código de seguimiento no va en monoespaciada, distinto del
    tablero;
  - Apariencia, IA y Negocio no tuvieron tablero propio de carga, y el tono del `Skeleton` no se tocó (en filas alternadas haría
    falta `loading-tint`);
  - decisiones chicas de septiembre (correo o número sin verificar, formato con guion, lupa del buscador), que los rediseños
    posteriores quizá ya resolvieron.
- **Qué falta:**
  1. Decidir si quedan como están o se alinean con un tablero.
  2. Registrar lo que quede como diferencia aceptada.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** el usuario.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md` y la memoria de los chats.
- **Lo nombran:** Núcleo (A12) y Propuestas y memoria (A17).

### P-16. Dos pendientes del caparazón: enlaces reales en `RowActions` y `main` con teclado

- **Estado:** propuesto sin decidir.
- **Evidencia:** `verificacion-y-produccion.md`, «Adopción visual y mensajes»: evaluar enlaces reales para Ver y Editar en
  `RowActions` (abrir en otra pestaña o copiar el destino), y revisar con teclado el desplazamiento de `main` al entrar desde el
  menú o con el foco en `body` (Re Pág, Av Pág, Espacio y flechas). Lo de Tab en la banda adherida ya está arreglado (front
  `Page.tsx:57`).
- **Qué falta:**
  1. Definir con el diseño la solución de cada uno.
  2. El código y sus tests.
- **Depende de:** nada.
- **Tamaño:** chico a mediano.
- **Decide:** el usuario, con el diseño.
- **Dónde vive:** `docs/plans/verificacion-y-produccion.md`, «Adopción visual y mensajes».
- **Lo nombran:** Núcleo (A12).

### P-17. Diálogos y menús sin animación

- **Estado:** propuesto sin decidir, heredado por confirmar.
- **Evidencia:** front `package.json` no tiene `tw-animate-css`, y `dialog.tsx`, `dropdown-menu.tsx` y `select.tsx` usan clases
  `animate-in` que nada define (`index.css` sólo define `@keyframes table-refresh`). No se miró en el navegador.
- **Qué falta:**
  1. Decidir si se quiere animación.
  2. Si sí, sumar el paquete y revisar las escenas visuales.
- **Depende de:** nada.
- **Tamaño:** chico.
- **Decide:** el usuario.
- **Dónde vive:** sólo en la memoria de los chats (biblioteca ArquitecturaBase UI).
- **Lo nombran:** Propuestas y memoria (A13).

### P-18. Rendimiento: lo que quedó de los veredictos 26 y 8

- **Estado:** a medias.
- **Evidencia:** la memoria de los 31 veredictos. La 26 (re-renders) se programó (front `77b48aaa` y `cc1df28d`), pero
  `useFormComposer.ts:84` corre un `setInterval` de 1 s siempre, aunque no haya una espera (comprobado). La 8 (permisos) se programó
  en parte: quedó afuera la consulta única de versiones de rol (heredado por confirmar).
- **Qué falta:**
  1. Elegir si se hacen: la memoria dice no programarlas hasta que el usuario elija.
  2. El reloj, sólo mientras hay una espera.
  3. Una consulta única de versiones de rol, en el backend.
- **Depende de:** nada.
- **Tamaño:** chico cada una.
- **Decide:** el usuario.
- **Dónde vive:** sólo en la memoria de los chats.
- **Lo nombran:** Propuestas y memoria (A14).

## Propuestas sin programar

Desarrollos nuevos, con plan o con charla, que no tienen código. Los grandes de un área quedaron en su sección: N-3, F-6, W-10 y
W-16. La base de demostración del modelo de formularios no es un pendiente: se retiró el 2026-11-18 (ver [Planes](#planes)).

### Pr-1. Tickets en Jira desde formularios

- **Estado:** diseñado sin programar.
- **Evidencia:** `docs/plans/integraciones/estudio-2026-10-22.md:1093-1252` (sección G, `afb0e4b2`, 2026-11-12) y el lienzo
  «Tickets en Jira» (tableros JR1 a JR9). No hay código: ni `Modules/Jira` ni «Se carga sola».
- **Lo decidido (JT1 a JT7):** cuatro disparadores (otro formulario lo sugiere, alguien lo completa a mano, «Reportar el problema»
  desde un error en pantalla y una falla sin nadie mirando, con origen «Sistema»); la ida y vuelta con Service Management y
  Software; la conexión en «Configuración → Jira», con «Probar conexión»; el formulario es el ticket; llamar a otro formulario
  extiende «Qué sugerir después» (F-9); la respuesta sugerida llega precargada, y una corrección va al ticket como comentario.
  Sólo Jira Cloud.
- **Lo que sigue sin decidir:**
  - el ADR de la etapa 1a: `Modules/Jira` (el recomendado) o el módulo `Integrations` del punto 2 del ADR 0023;
  - en el mismo ADR, el estándar con parámetros (proyecto y tipo) y «Actualizar desde Jira», que cambian la regla BI1-A;
  - la acción propia con un puerto neutral de tickets (la recomendada) o reutilizar `IssueDocument`;
  - «anular deja un comentario con el motivo y no cierra el ticket», que se mostró sin un sí registrado;
  - lo técnico de G.5, sin confirmar con Atlassian: los campos y sus valores, los adjuntos y el formato ADF, si la búsqueda por
    etiqueta ve enseguida un ticket nuevo, los límites de pedidos y cómo se reconoce «Responder al cliente».
- **Qué falta, por etapa:**
  1. Decidir lo de arriba.
  2. Etapa 1a: el tablero con estados (JR1, JR2a, JR2, JR3 y JR4) y su lista de chequeo; el diseño en el front; el ADR del módulo;
     un plan con TDD; un anillo propio de credenciales; el estándar con proyecto y tipo; la acción «Crear el ticket en Jira» con
     lease, fence y estado incierto; una etiqueta por run y la búsqueda antes de reintentar; el resultado `jira.ticket`; corregir y
     anular comentan; un Jira falso para la integración; las guías «configurar Jira» y «quitar Jira».
  3. Etapa 1b: es F-9, y puede ir antes o después de la 1a.
  4. Etapa 2, la ida y vuelta: el webhook con firma `X-Hub-Signature`, una bandeja durable, el descarte por
     `X-Atlassian-Webhook-Identifier`, la consulta periódica, el estado vivo aparte del resultado, los comentarios a quien reportó
     en la app y por WhatsApp, y el ADR de avisos entrantes.
  5. Etapa 3: «Reportar el problema» en errores y acciones fallidas, y «Configuración → Reportes de problemas».
  6. Etapa 4: un puerto de incidentes con huella estable, ocurrencias y topes, y su ADR.
  7. En cada etapa, la corrida completa al cerrar.
- **Depende de:** una cuenta de Jira Cloud de prueba; la etapa 2 por WhatsApp, de W-10; el traspaso al multitenant, al cerrar.
- **Tamaño:** grande; la 1a sola es mediana a grande.
- **Decide:** el usuario.
- **Dónde vive:** el estudio de integraciones, sección G.
- **Lo nombran:** Propuestas y memoria (A1), Formularios (núcleo, A10) y Front (A21).

### Pr-2. Recorridos de punta a punta con Playwright

- **Estado:** propuesto sin decidir.
- **Evidencia:** `docs/plans/2026-11-17-recorridos-de-punta-a-punta.md` (`9f800771`) y su fila del índice. En el front no hay `e2e/`
  ni `playwright.e2e.config.ts`. Los dobles que propone ya existen en `tests/ArquitecturaBase.Api.IntegrationTests`
  (`BusinessFormsManualAppPreviewTests.cs`, `CapturingEmailSender`, `FictionalLlmClient`, `FictionalPlacesClient`, `FakeArca`,
  `FakeArcaWeb` y `ExternalLoginTestController`).
- **Las ocho decisiones, recomendadas por el plan y sin confirmar:** un recorrido por caso de uso y no por botón; `e2e/` en el
  front, con `npm run test:e2e`; el host con el patrón de `BusinessFormsManualAppPreview` en `https://localhost:7445`, sin
  Aspire; los dobles de la integración más un controller de control sólo en `TestFeatures`; datos sin orden, con una base nueva
  por corrida; el ingreso por la pantalla; evidencia con video y traza, sin comparar píxeles; y correr una vez al cerrar, con uno o
  dos workers, sin reintentos y en menos de 45 minutos.
- **Qué falta:**
  1. Confirmar las ocho decisiones.
  2. Etapa 1 (un día): el host, el controller de control, la configuración de Playwright, los scripts, el `AGENTS.md` de `e2e/`, el
     mapa de instrucciones, un test que impida registrar el controller fuera del host, los pilotos R2, R13 y R16, y diez corridas
     seguidas sin fallas.
  3. Etapas 2 a 5 (medio día a un día cada una): identidad y administración (R1 y R3 a R12); formularios y catálogos (R14, R15 y
     R17 a R31); WhatsApp e IA (R32 a R40); ARCA (R41 a R44).
  4. Etapa 6: sumar `npm run test:e2e` a la corrida completa de `docs/guides/validar-un-cambio.md` y anotar en cada plan con «falta
     la prueba manual» qué recorrido la cubre.
- **Depende de:** terminar la prueba integral. No corre a la vez que `dotnet test`, porque el host compila dentro del proyecto de
  integración.
- **Tamaño:** grande (cinco a seis días).
- **Decide:** el usuario.
- **Dónde vive:** el plan de recorridos.
- **Lo nombran:** Propuestas y memoria (A2). Es infraestructura de pruebas: el informe del núcleo no lo cuenta como funcionalidad.

### Pr-3. Integraciones del estudio: webhooks, conector HTTP, Mercado Pago, API pública, MCP y Mercado Libre

- **Estado:** propuesto sin decidir.
- **Evidencia:** `docs/plans/integraciones/estudio-2026-10-22.md:930-1089` (E.1 a E.4) y la fila del índice («para después de la
  parte 1»). No hay código: ni `Modules/Integrations`, ni `MapMcp`, ni webhooks salientes firmados. Las decisiones 1 a 5 de E.4 (las
  de ARCA) se cerraron el 31/10, y para Jira la sección G reemplaza a E.1.
- **Lo que sigue sin decidir (E.4, puntos 6 a 13):**
  - 6, los webhooks: como acción de regla o también como suscripciones por la API, y con cuerpo fino o completo;
  - 7, los atributos de Mercado Libre: un formulario por categoría, o el asistente los propone y se validan;
  - 8, la API pública: claves por formulario u OAuth client credentials, `/api/public/v1` con `Asp.Versioning` (acota el ADR 0005) y
    topes por clave y día;
  - 9, las plantillas de Meta (W-10);
  - 10, si se expone MCP y cuándo, con las acciones de efecto legal sólo con «proponer» y confirmación humana (la recomendada);
  - 11, los eventos externos como disparadores de reglas («Pago aprobado», «Factura emitida»);
  - 12, la retención de cuerpos y respuestas (se cruza con N-5);
  - 13, los ADR: el módulo `Integrations` y su puerto, la API pública versionada, la excepción de `MapMcp` y las credenciales.
- **Qué falta, en el orden de E.3:**
  1. Decidir los puntos 6 a 13 y escribir los ADR.
  2. La base: el módulo `Integrations`, las conexiones por organización (cifradas con un anillo externo, con prueba, estados,
     revisión y 409) y la acción «Ejecutar en un sistema externo» sobre el motor de acciones, con reconciliación y revisión humana.
     Las acciones como descontar stock, que nombra el front, entran por acá.
  3. El webhook saliente firmado (Standard Webhooks), con registro de entregas y reenvío.
  4. El conector HTTP genérico con protección SSRF; después, importar OpenAPI.
  5. Mercado Pago: el link de pago y el webhook entrante («Pago aprobado»).
  6. La API pública, con plantillas de Meta para mandar fuera de la ventana.
  7. El servidor MCP sobre los mismos servicios y permisos.
  8. Mercado Libre.
- **Depende de:** W-10, del ADR que acote el 0005 y de un segundo proveedor que justifique `Integrations` (Jira podría serlo).
- **Tamaño:** grande; cada pieza es mediana o grande.
- **Decide:** el usuario.
- **Dónde vive:** el estudio de integraciones, E.1 a E.4.
- **Lo nombran:** Propuestas y memoria (A3) y Front (A21).

### Pr-4. CRM y módulos activables

- **Estado:** propuesto sin decidir, con una decisión tomada.
- **Evidencia:** sólo la memoria de los chats (una charla de diseño del 17/11). No hay documento: ni «MO1-B» ni «módulos activables»
  aparecen en `docs/` (comprobado).
- **Lo decidido:** MO1-B (17/11): el administrador del cliente prende y apaga cada módulo (WhatsApp, Formularios, IA, ARCA y CRM)
  desde la app, al instante, sin una capa de instalación que limite qué puede prender. Se descartaron A (por configuración, al
  reiniciar; era la recomendada) y C.
- **Lo que sigue sin decidir:** todo lo del CRM (qué es, qué guarda y cómo se relaciona con contactos y formularios) y el detalle de
  los módulos activables: los datos y los procesos en segundo plano de un módulo apagado, los permisos, el menú y las rutas.
- **Qué falta:**
  1. Escribir el documento en `docs/plans` con lo decidido.
  2. Dibujar los tableros.
  3. Decidir el resto.
  4. El ADR del control por pedido, con la forma del `[FeatureGate]` del multitenant. Cambia la premisa de hoy: un módulo se quita
     con código y una migración de baja.
  5. Programarlo después de la prueba integral.
- **Depende de:** la prueba integral y cómo quedan hoy los módulos opcionales.
- **Tamaño:** grande: el CRM es un módulo nuevo, y los módulos activables tocan los cuatro módulos y el núcleo.
- **Decide:** el usuario.
- **Dónde vive:** sólo en la memoria de los chats.
- **Lo nombran:** Propuestas y memoria (A5).

## Decisiones que te tocan

Ordenadas por importancia. Cuando el informe recomienda una opción, va primero.

### Antes de producción o con riesgo en los datos

1. **N-5.** Cómo se borra o anonimiza a una persona y cuánto se conserva en cada módulo, con asesoría legal. Ya está decidido que
   se decide antes de producción y que no hereda los 90 días de WhatsApp.
2. **Pr-4.** Escribir ya lo decidido del CRM (MO1-B) para que no se pierda; después, qué es el CRM y qué pasa con un módulo apagado.
3. **F-16.** Aceptar o corregir cada uno de los ocho menores. Primero los cuatro de migraciones y versiones (2b-2, 2b-1,
   `NotApplicable` y 2b-5). En 2b-5, la recomendada es aceptarlo por escrito.
4. **A-6.** El plazo fiscal de los comprobantes, con el contador; después, su purga.
5. **I-5.** Los plazos de retención y las cuotas del asistente, con los datos del piloto.
6. **I-6.** Qué se conserva de la metadata de los canales para no repetir un pedido ni reprocesar un webhook.
7. **A-7.** Los comprobantes que quedaron fuera de las etapas 3 y 4. Empezar por la A con leyenda: es el riesgo más concreto, y se
   ve recién con la primera factura real de un responsable inscripto.

### Diferencias con lo decidido y cierres de planes

8. **W-1.** Aceptar que un formulario vaya solo, o pedir la mezcla con otras acciones que se decidió el 21/10.
9. **P-11.** El lienzo «Sistema visual»: compartirlo o entrar con la cuenta dueña, mudarlo a un lienzo nuevo, o abandonarlo y dar
   por buenos los lienzos aparte y las capturas.
10. **F-10.** R3: confirmar si sigue haciendo falta.
11. **D-3.** Las billeteras: aceptar una fuente no oficial o esperar una oficial, y si se actualizan las instalaciones que ya tienen
    el catálogo.
12. **D-4.** Reabrir el monto en el aviso de DOC4, o dejar el texto con el nombre del formulario.
13. **A-4.** Si alcanza el aviso general de F2, o se suma el motivo de ARCA.
14. **W-8.** El estado del listado del chat: `usePagination` y `useFilters`, o una excepción escrita.
15. **W-4.** Qué contestar cuando cae el Flow que sigue a la ubicación: derivar con un código, como H5, o la frase del equipo.
16. **P-9.** El corte de la bandeja de WhatsApp: volver a `md` o dibujar `xl`.
17. **F-17.** Si se completa alguna de las seis partes que se dejaron (F1, F13, F17, B3, F8 y F24).
18. **F-18.** Si se amplía el escáner de PDF hostil (F6-4).
19. **A-5.** Cuáles de las observaciones de ARCA se hacen. O12 cambia la fecha con que sale un comprobante.
20. **I-10.** O11 (que Formularios reciba el aviso de cierre) y O12 (forzar la herramienta terminal, que pide recalificar).
21. **I-8.** Si vale el corte que falla rápido (I6).
22. **I-9.** Si quien sólo tiene `ai.read` ve el aviso de clave ilegible (I7).
23. **D-5.** Si el resumen por WhatsApp suma «Va a leer documentos como este.».
24. **W-12.** El tope diario de invitaciones por WhatsApp y su costo.

### Diseño y pantallas

25. **P-12.** Dónde se dibuja el responsive (depende de P-11).
26. **P-15.** Las decisiones chicas heredadas: dejarlas como están o alinearlas con un tablero.
27. **P-16.** Los enlaces reales en `RowActions` y `main` con teclado, con el diseño.
28. **W-13.** Las burbujas: Actuales o Redondeadas, o retirar la comparación.
29. **W-14.** «Sugerir con IA» a pedido: descartarlo, porque AS4 ya sugiere y su plan se retiró el 2026-11-18, o programar el botón.
30. **P-17.** Si diálogos y menús llevan animación.
31. **P-18.** El reloj de 1 s del compositor y la consulta única de versiones de rol.
32. **P-14.** Qué falta del Inicio completo, los 403 y 404 y la marca real.
33. **P-13.** Dónde hacen falta la selección y las acciones masivas, y con qué contrato.

### Desarrollos nuevos, después de la prueba integral

34. **Pr-2.** Confirmar las ocho decisiones de los recorridos de punta a punta (las recomienda el plan).
35. **W-10.** Las plantillas de Meta: cuándo y para qué (avisos fuera de las 24 horas, Jira y la API pública).
36. **N-3.** Las solicitudes de ingreso: el alcance y los tableros.
37. **N-2.** La historia administrativa en pantalla: el tablero y el permiso (`users.read` y `roles.read`, o uno nuevo).
38. **Pr-1.** Jira: el ADR de la 1a (`Modules/Jira`, el recomendado, o `Integrations`), la acción propia (la recomendada) o
    `IssueDocument`, si anular comenta, y el orden de la 1a y la 1b.
39. **F-9.** Llamar a otro formulario: cuándo, y si crear una respuesta en nombre de otra persona pide un ADR.
40. **F-1.** Copiar formularios: el tablero y qué se lleva.
41. **F-6.** Las bases externas: conector, VPN, recepción por API, o la recomendación del chat (no construir el conector todavía:
    el Excel con la IA y una API chica cuando un cliente la pida).
42. **N-7.** La privacidad en el ingreso y las invitaciones.
43. **N-4.** Dispositivos y sesiones: ver y cerrar las propias, o también las de otra cuenta.
44. **W-9 y W-11.** Los contactos y la conexión del canal en pantalla.
45. **W-17.** Entrar con WhatsApp sin código, ahora que no hay enlace de ingreso.
46. **W-15 y W-16.** Los casos por foto o documento, y la etapa «Agentes».
47. **D-6, D-7 y D-8.** La carga automática (con los datos del piloto), el orden de los estándares siguientes y varias fotos en una
    carga.
48. **I-4.** Qué tipos nuevos del editor propone la IA (pueden viajar con I-1).
49. **F-2, F-3, F-4, F-5, F-7 y F-8.** Importar respuestas desde un Excel, las fotos juntas, CW7 por WhatsApp (con `update_data` o
    con el intercambio), el intercambio cifrado, K5 cuando aparezca el caso y completar con un dato de la opción.
50. **A-8 y A-9.** Qué familia de ARCA sigue, y lo que quedó fuera de las etapas 5 y 6.
51. **Pr-3.** Las decisiones 6 a 13 de las integraciones.
52. **D-9.** Los tableros con datos externos, después de F-6.
53. **W-18 y W-19.** El enlace de la ubicación del negocio, y si siguen valiendo las instalaciones independientes.
54. **N-10.** Mover el JSON del cliente de Google fuera del repo.

## Se programan sin decisión nueva

De chico a grande. Lo acordado el 16/11 los deja para después de la prueba integral.

1. **P-5** (trivial). `cn` desde `shared/lib/utils` en los tres componentes.
2. **W-7** (trivial). Los comentarios del bot retirado y de «Colas en memoria».
3. **A-2** (chico). El límite de escritura en las dos acciones del robot.
4. **I-2** (chico). Los tres datos del bootstrap del asistente, requeridos.
5. **P-4** (chico). «+18%» en el catálogo visual; mirás las referencias rehechas.
6. **F-13** (chico). La zona general antes de UTC en el Excel.
7. **N-8** (chico). Las observaciones del núcleo, al tocar esos archivos.
8. **F-11** (chico). La prueba de integración del retiro de Flows.
9. **N-1** (chico). El lector de permisos no concede nada a una cuenta borrada, con su test y la revisión adversarial.
10. **N-6** (chico). El health check del almacén.
11. **F-15** (chico). El toque viejo (B1).
12. **W-6** (chico). El motivo neutral del puerto de autoría (F22).
13. **I-10** (chico, una parte). O1, O4, O8 y O9 de la revisión de IA.
14. **D-2** (chico). Anotar la diferencia de DOC3, o sumarlo.
15. **D-1** (chico). El porqué de un documento en el chat (DOC2).
16. **P-2** (chico). La escena de «Por resolver».
17. **P-8** (chico). Mirar el panel móvil en el build.
18. **P-10** (chico). Confirmar los dos tests lentos.
19. **W-5** (chico). Confirmar con un test la retención de un día.
20. **A-1** (chico o mediano). CA1 en el listado de catálogos; el informe del front pide tu visto bueno para el nombre del origen.
21. **P-9** (chico a mediano, una parte). Contrastar la lista «Con la 2B», salvo el corte de la bandeja.
22. **N-9** (chico). Borrar lo que dejó el enlace de ingreso, cuando la retención vacíe la tabla (desde mediados de diciembre).
23. **F-14** (chico a mediano). La cadena de correcciones en la aprobación (F4), con la revisión adversarial.
24. **W-3** (chico a mediano). La traza en la captura de Flows y en la cola de acceso (H19).
25. **I-7** (chico a mediano). La purga de WhatsApp por lotes.
26. **I-3** (chico, más la corrida). `home-system@3` y `KnownInstructions`, con la próxima calificación.
27. **A-3** (chico a mediano). La medición de Chromium en la corrida real y, si no entra, un lado por vez.
28. **P-1** (mediano). Los lectores de las cohortes del 17/11.
29. **P-3** (mediano). Las escenas de la pregunta «Ubicación».
30. **F-12** (mediano). Repetir en una copia quitar WhatsApp y la parte del front de quitar Formularios.
31. **W-2** (mediano). Lo que todavía no espera a otra versión (H25).
32. **P-6** (mediano). La recuperación incierta de los mensajes.
33. **I-1** (mediano). `propose-form@3` con Renglones; falta tu visto bueno para la corrida real.
34. **P-7** (grande). La auditoría de accesibilidad móvil y los objetivos táctiles.

## Desvíos del código contra una regla

1. **W-6.** WhatsApp compara códigos de Formularios como texto (`WhatsAppAssistantAuthoringPolicy.cs:49-50`), contra la regla de
   que los módulos sólo se cruzan por puertos neutrales.
2. **A-2.** Las dos escrituras del robot usan el límite de lectura (`ArcaRobotController.cs:22`, `:47` y `:57`), contra «Límites
   por operador» de `docs/features/arca.md`.
3. **F-13.** El Excel cae a UTC (`FormExportResponsePolicy.cs:22-27`), contra XL2. La regla de `formularios.md` no dice el respaldo.
4. **W-8.** El estado del listado del chat va en `useState` (front `WhatsAppChatPage.tsx:46-48`), contra las premisas de pantallas
   del front, que piden filtros y búsqueda en la URL.
5. **P-5.** `cn` sale del paquete en `RuleMessageField.tsx`, `MultiSelect.tsx` y `WideTable.tsx`, contra el `docs/architecture.md`
   del front.
6. **P-4.** «+18 %» con espacio (front `CatalogCharts.tsx:36`), contra el porcentaje pegado; `formDashboard.spec.ts:56` lo tolera.
7. **I-2.** Tres `.optional()` en un contrato ya publicado (front `assistant.ts:151-155`): una capa de compatibilidad que
   `AGENTS.md` no admite y que el plan mandaba quitar.
8. **P-1.** Contratos exportados sin lector. No rompe una regla escrita, pero el contrato con el front son los DTO, el OpenAPI y
   los ejemplos exportados, y esos ejemplos no los controla nadie.
9. **F-11.** Una prueba que el plan de «sólo en la app» daba por agregada y no está.
10. **Comentarios que contradicen la regla escrita (N-8 y W-7):**
    - `LoginCodeService.cs:39-41` dice que el correo sale igual si el commit falla; con la cola transaccional (ADR 0015), un
      rollback deshace el encolado;
    - `AccountCreationPolicy.cs:11-12` llama «por defecto» a `InviteOnly`, y la plantilla configura `Open`;
    - `EmailOptions.cs:27-30` y `WhatsAppOptions.cs:72` citan «Colas en memoria», que ya no existe en `backend.md`;
    - `IUnitOfWork.cs:23-25` promete traducir la clave única, y un diferido que choca al confirmar da 500 (O7);
    - el comentario de `AiBusinessKnowledgeSource` promete un control que queda en manos del llamador (O10 de IA).
11. **Riesgo latente contra la regla I4 de versiones:** `KnownInstructions` (`AiAssistantChannelPolicy.cs:82`, I-3) y los lectores
    que comparan la versión exacta de una capacidad (I-1). Fallan recién con la próxima suba.
12. **Observaciones sin regla escrita:** `BusinessFormsApiRegistration.cs:20` asigna el `GlobalLimiter`, que es uno solo para toda
    la app (si otro módulo asigna otro, gana el último; conviene que lo mire la revisión de arquitectura); y los 429 de negocio
    llevan `retryAfter` en el cuerpo, sin el encabezado `Retry-After` (O1 del núcleo).

## Aparte

### Pruebas a mano sin criterio escrito

- Corregir y anular, y los títulos de sección: el plan de la carga dice «la prueba manual», sin pasos.
- La cámara de un celular real: remite a la guía, pero el túnel sólo publica la Api y no dice cómo llega el celular a la app.
- F2: la pestaña «Por resolver» y la columna «Reglas».
- F7: revisar un tema de «Formularios que faltan».
- F13: confirmar un documento repetido.
- F3: descartar una publicación. Sólo lo describe el recorrido R15 del plan de recorridos (Pr-2), sin programar.
- Los 29 arreglos de IA: el plan dice «queda la prueba manual», sin pasos. El informe de IA propone un criterio: borrar una
  conversación con foto y audio (I2), la hora local de renovación con la cuota agotada (decisión 8) y una subida que no espera
  (I14), la clave ilegible con «Reemplazar clave» resaltado (I7), una fila ilegible que no tira el panel (I8) y apagar la Api con
  una consulta en curso (I12).
- Las etapas 1 y 3 de la IA que arma: los planes no tienen pasos; la prueba integral los toma del diseño del front
  (`ia-arma-formularios.md`), con los tres perfiles declarados.
- Las pruebas reales de la revisión de WhatsApp (Meta lento o caído, varias copias, plantillas en inglés aprobadas): no tienen
  criterio y tampoco figuran en `verificacion-y-produccion.md`.

Desde el barrido ya tienen criterio escrito: carpetas y «sólo en la app» (en sus planes) y lo de la app en documentos (en «Lo que
falta» de su plan).

### Verificaciones con servicios reales

Con Meta:

- la vista oficial del Flow r14 (las marcas, la ayuda, los errores dinámicos y la gemela de la pregunta del documento), con su
  plan B;
- el reenvío de W3 con el mismo `flow_token`, y el pie de una carga por fila;
- el pedido de ubicación (W1-A) y la ubicación que vuelve, por el operador y por el asistente;
- r15 (la coma) y los límites completos del piloto de formularios;
- un Flow con títulos de sección (CW15-B), y elegir primero por un campo en el celular (LD2-A, r11);
- la Etapa 2 de la IA: un `.xlsx` y un `.csv` reales desde el celular, comparando `sha256` y `mime_type` con el webhook;
- el piloto con comprobantes reales y la calibración de los cupos de lectura (10 por persona y 50 por instalación) y del tope de 50
  filas;
- el piloto de los casos del asistente (los cuatro criterios de su plan) y el acceso y la atención con un teléfono real;
- el piloto único de catálogos E1 y E2 (`verificacion-y-produccion.md`);
- el Hito 4 del ingreso con WhatsApp (espera la plantilla `invitacion_acceso`), la tanda E del caso de prueba y el «Registro de
  lluvia» por WhatsApp;
- las pruebas reales de la revisión de WhatsApp, sin criterio (arriba).

Con ARCA real:

- la homologación de la conexión con un certificado de WSASS (los CN de los emisores y la condición frente al IVA con CUIT
  reales);
- la homologación de la factura C con su nota de crédito, y la de la A y la B (los nueve pasos de la etapa 4 y la tarea 8);
- la delegación de verdad, en homologación y en producción (el formato del token y el ticket nuevo después de aceptar);
- la corrida real del robot con las cuentas del usuario, que trae la medición de A-3;
- la receta de la imagen con Chromium contra Azure, la primera vez que una instalación prenda el robot;
- repetir lo del ADR 0031 con ARCA real: el 10016 por fecha, los errores de la constancia y la ventana del WSAA.

Con otros proveedores: Google con el secreto real (tarea 25 de la Fase 2), la cámara de un celular real, Anthropic con credenciales
propias, y la 9 y la 27 de rendimiento en Azure dev.

### Arquitectura para más adelante

No son funcionalidades: se programan con un plan propio cuando se decida escalar. El detalle está en las cinco revisiones de
arquitectura (12 y 14/11) y en `verificacion-y-produccion.md`.

- **Varias copias** (antes de pasar de `--max-replicas 1`): la lease de la limpieza de archivos (N28) y el barrido sólo bajo
  prefijos declarados (I18), en el núcleo; H23 (la prueba con dos hosts), H24 (cada respuesta de Flow con `SKIP LOCKED`) y H26
  (límites y salud por copia), en WhatsApp; F32 (la reserva de una acción que no se renueva), en Formularios.
- **Gran escala** (con volumen real que medir): N29, N30, N32 y N34 del núcleo; H27 a H30, H32, H34 y lo que queda de H31 de
  WhatsApp; lo que queda de F33, F34 a F36, F39, F41 a F46, el aviso de «hay más» de F48 y el XLSX por partes (F11) de Formularios;
  I19, I20 y el ensayo de carga con la operación de la base (pendientes 3, 5 y 6 del plan del panel) de IA; H33 (más puntos de
  venta o lotes, que reabre el ADR 0028), H35, H36 y H37 de ARCA.

## Planes

Según las secciones C de los informes. Al retirar cualquiera: anotar en `docs/history/README.md` el commit anterior y dónde
quedó cada cosa, y corregir sus enlaces.

### Retirados el 2026-11-18

Se retiraron los nueve. A dónde fueron las decisiones y los pendientes de cada uno está en la [procedencia](../history/README.md#barrido-documental-del-2026-11-18).

### Se retiran después de la prueba integral

- `2026-10-29-cargar-formularios-desde-la-app.md`: guarda las pruebas a mano de la cámara, corregir y anular, y los títulos con
  Meta. Sus criterios pasan a `verificacion-y-produccion.md`.
- `2026-11-03-carpetas-de-formularios-y-catalogos.md`: también espera P-1, su lector.
- `2026-11-07-elegir-primero-por-un-campo.md`: espera el celular. LD1-B, LD2-B y LD2-C, descartadas, quedan en una línea de
  `formularios.md`.
- `2026-11-09-excel-de-respuestas-en-una-hoja.md`: después de su prueba a mano y de F-13.
- `2026-11-09-formularios-solo-en-la-app.md`: después de P-1, F-11, F-12 y su prueba a mano.
- `2026-10-22-documentos-por-whatsapp.md`: antes, la sección de la bandeja en `formularios.md` (hoy el plan es su única fuente
  escrita), P-1 y la prueba con Meta. Sus propuestas (D-3 a D-8) pasan al índice o a un plan chico «Documentos: lo que sigue».
- `2026-10-21-casos-del-asistente.md`: guarda los pasos del piloto con Meta. W-1 se cierra con tu respuesta; W-15 y W-16 pasan a un
  plan propio o quedan recortados ahí.
- `2026-11-04-ia-arma-formularios-y-catalogos.md`: sólo si antes I-1 e I-4 pasan al índice o a `docs/plans/formularios/README.md`.
- `2026-11-09-ia-arma-formularios-etapa-2.md`: también después de I-2. Sus pasos con Meta pasan a la lista de la prueba integral.
- `2026-11-09-arca-etapa-4-factura-a-y-b.md`: también después de la homologación. Las «Normas confirmadas», con sus fuentes, van a
  la regla de ARCA.
- En el front, `2026-11-09-acciones-del-encabezado.md` y `2026-11-09-dialogos-con-la-lista-de-chequeo.md`, después de su prueba
  manual. Los otros dos planes del front (motivo de lo apagado y un dato por columna) no tienen fecha de retiro en los informes.

### Se retiran después de otra verificación

- `2026-11-09-arca-etapa-3-factura-c.md`: después de homologar la C y su nota de crédito. El riesgo de que homologación no devuelva
  puntos de venta pasa a `configurar-arca.md`.
- `2026-11-09-arca-etapa-5-delegacion-guiada.md`: después de la delegación de verdad. Los puntos 4, 6, 8 y 9 de «Cómo funciona la
  delegación en ARCA» van a `configurar-arca.md` o al ADR 0027.
- `2026-11-09-arca-etapa-6-robot.md`: después de la corrida real, con A-3.
- `2026-10-02-almacenamiento-de-archivos.md`: no depende de la prueba integral. Se retira cuando sus decisiones vigentes pasen a
  `backend.md` («Archivos privados») y su tabla de pendientes a `verificacion-y-produccion.md`.

### Se recortan, no se retiran

- `2026-10-31-arca-formularios-estandar.md`, después de la prueba integral. Quedan el mapa de servicios, la fila «Después» (A-8) y
  lo que quedó fuera de las etapas 3 y 4 (A-7). Al recortar los arreglos, corregir su ancla en el ADR 0031, la revisión y el
  índice.
- `2026-10-01-bandeja-whatsapp.md`. El informe de WhatsApp propone recortar ya la sección de los arreglos; el plan sigue por W-2,
  W-3, W-5, W-8 a W-11 y W-19. Al quitar las etapas, los dos enlaces del traspaso del 16/11 pasan a la revisión.
- `2026-10-04-panel-conversaciones-inicio.md`. Sigue abierto por I-3 e I-5 a I-10. Después de la prueba de los arreglos, las siete
  etapas quedan en una línea cada una.

### No se retiran

`2026-09-30-solicitudes-de-ingreso.md` (N-3), `verificacion-y-produccion.md`, `formularios/README.md`,
`2026-10-21-catalogos-con-origenes-externos.md` (F-5, F-6 y F-8), `2026-11-17-recorridos-de-punta-a-punta.md` (Pr-2) e
`integraciones/estudio-2026-10-22.md` (F-9, Pr-1 y Pr-3). En el front, `2026-09-30-unificacion-mensajes.md` sigue hasta cerrar
P-6. Los traspasos al multitenant quedan fuera de este análisis: se retiran cuando el multitenant los incorpore.
