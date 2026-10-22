# Verificaciones y preparación de producción pendientes

Estado: **abierto**. Reconciliado el 2026-10-02 con el código, la documentación y las revisiones locales.
Conserva información pendiente de los planes retirados; no autoriza un despliegue ni nuevas
funciones. Las pruebas automáticas y capturas con datos simulados no cierran los recorridos
manuales con proveedores reales. La [procedencia](../history/README.md) permite consultar los originales.

## Verificación manual con la aplicación real

La [revisión funcional del 2026-10-01](../reviews/2026-10-01-revision-funcional.md) y su
[consolidado](../reviews/2026-10-01/resultados.json) acreditan recorridos con SPA/API reales
en HTTPS y PostgreSQL/Redis aislados: correo local, OIDC, perfil, usuarios, roles, permisos,
configuración y recuperación HTTP. P2/P3 se corrigieron con regresiones posteriores.
No fue una prueba de Google/SMTP/Meta externos ni del refactor de WhatsApp del día siguiente.
La [validación del 2026-10-02](../reviews/2026-10-02-separacion-acceso-whatsapp-validacion.md)
acredita el refactor con Meta simulado; no reemplaza la prueba con teléfono.

- [ ] Refactor acceso/atención del 2026-10-02: recepción con/sin cuenta sin efectos de identidad,
  respuesta manual, código en Manual y nueva invitación + WANT_TO_ENTER histórico → `/login` →
  código. Comprobar payload real de la plantilla y receipts con el titular del teléfono.
  Usar [la guía local](../guides/whatsapp-en-local.md#probar-acceso-y-atención-separados--2026-10-02).
  Los tests automáticos con Meta simulado no cierran esta prueba.

- [ ] Completar los recorridos con proveedores externos: Google real, SMTP real e invitaciones
  y verificar/vincular/desvincular teléfono con Meta, además de los casos del refactor anterior.
  No volver a declarar ausentes los recorridos locales acreditados el 2026-10-01. Registrar
  entorno, versión, resultados y límites; una suite automática no prueba al proveedor real.
- [ ] Comprobar el arranque real con una base nueva aislada, migraciones, seed e ingreso del
  administrador inicial. Las pruebas automáticas de bases vacías y el bundle ya pasaron;
  queda la experiencia de la aplicación levantada, sin borrar la base habitual.
- [ ] Completar en roles los caminos de salida con cambios todavía no individualizados en la
  evidencia (migas, menú, Atrás y recarga) y los escenarios responsive/teclado pendientes.
  Alta/edición, Admin protegido, User inmutable, rol inexistente, rechazos de permisos y
  Cancelar/volver tienen evidencia con API real en la revisión del 2026-10-01.

Si se inicia Aspire, terminar con `aspire stop`. No enviar mensajes de prueba a terceros sin
la autorización correspondiente al recorrido.

## Formularios: piloto de aprobación y PDF

- [x] Repetir con Meta la visita, aprobación por otra cuenta y PDF con foto: el 2026-10-18 se abrieron en el celular el PDF inglés con traducción completa y el PDF español al faltar una traducción usada; los avisos respetaron el mismo idioma y las acciones quedaron en Hecho.

El [recorrido real en español con foto](../features/formularios.md#piloto-de-aprobación-y-pdf--2026-10-17) quedó verificado el 2026-10-17 con Meta y Azure privado reales. El [piloto del 2026-10-18](../features/formularios.md#piloto-del-asistente-y-audios--2026-10-18) agrega la evidencia física en inglés y del respaldo al español del PDF y los avisos. Chrome conserva el alcance local de visor y descarga del [cierre](../features/formularios.md#cierre-local-de-la-etapa-5-y-del-review-de-la-etapa-4); Edge no se recorrió.

## Formularios: piloto del asistente y audios

El 2026-10-18 se republicó **Registrar visita**, versión 2, con `flow-static-7.3-r8`, «Cuándo lo ofrece el asistente» y foto obligatoria. Se usaron Aspire con túnel, Meta, OpenAI y Azure privado reales; el [resultado](../features/formularios.md#piloto-del-asistente-y-audios--2026-10-18) conserva versiones y límites.

- [x] Texto y audio: formulario prellenado confirmado por la persona, foto guardada, motivo visible y transcripción completa en español en el chat (`VISITA-000002/000003`).
- [x] Pedido sin formulario: aviso recibido y tema agrupado en Formularios que faltan, sin datos personales; sugerencia manual revisada y enviada por la persona (`VISITA-000004`).
- [x] Sugerencia tras confirmar una respuesta: el 2026-10-20 se confirmó `VISITA-000012` → `PRB1010-000002`, con «Preguntar siempre», revisión Manual y ambos datos mapeados conservados. Ver el [piloto total](#formularios-piloto-total-con-proveedores-reales--2026-10-20).
- [x] Cuenta en inglés y traducción completa: formulario, avisos y PDF con foto en inglés (`VISITA-000005`).
- [x] Respaldo de todo el recorrido al idioma original: la repetición física del 2026-10-20 cerró P24 con faltantes separados de aviso y PDF (`VISITA-000011/000012`), incluida la apertura y el formulario. Conserva como antecedente el fallo del 2026-10-18: con sólo el nombre inglés del PDF vacío, avisos y PDF salían en español, pero formulario y apertura seguían en inglés (`VISITA-000006`). Ver el [piloto de idioma original y traducción opcional](#formularios-idioma-original-y-traducción-opcional).

La [calificación OpenAI y su reevaluación de canales](../guides/configurar-ia.md#evidencia-para-habilitar-perfiles) son evidencia separada. No se corrigió código durante el piloto. Se restauraron el idioma español de la cuenta, el título inglés del PDF y el modo original Manual; Aspire y el túnel se apagaron con `aspire stop`.

## Formularios: idioma original y traducción opcional

La [revisión local del cierre](../reviews/2026-10-19-code-review-cierre-formularios.md) incorpora regresiones y un recorrido de navegador con proveedores controlados.
La verificación completa y los contratos HTTP exportados quedan registrados allí; este trabajo no acredita un piloto físico con Meta ni una nueva calificación LLM.

Piloto físico del 2026-10-20 con Meta y la misma cuenta configurada en inglés:

- [x] Original español solo, versión 3: apertura, formulario, avisos y PDF con foto en español (`VISITA-000007`).
- [x] Inglés completo, versión 4: apertura, formulario, avisos y PDF con foto en inglés (`VISITA-000010`).
- [x] P24 con versión 4 completa: faltó sólo el inglés del aviso de recepción, reglas r3 (`VISITA-000011`); después se restauraron los avisos, reglas r4, y faltó sólo el título inglés del PDF, diseño r5 (`VISITA-000012`). En ambos casos apertura, formulario, avisos y PDF con foto salieron enteros en español y se abrieron en el celular.
- [x] Sin inglés en texto/detalle del ítem «Seguimiento», catálogo r2: conservó «Seguimiento» / «Seguimiento de una visita»; formulario, avisos y PDF con foto mantuvieron el inglés (`VISITA-000013`, diseño PDF completo r6).

Registrar las versiones y resultados reales. Conservar las sesiones, bindings, perfiles, presentaciones y copias fijas anteriores; restaurar los idiomas y el modo de conversación utilizados para la prueba y terminar con `aspire stop`. No requiere repetir la calificación OpenAI.

## Formularios: piloto total con proveedores reales — 2026-10-20

Se usaron Aspire con túnel, Meta, OpenAI y Azure Blob privado reales, sobre backend `a7cea3f2` y front `44e464b`. El titular recorrió Chrome y el celular; la preparación y el contraste de estados se hicieron por la API autenticada porque falló el control del navegador. No se cambió código de producto ni se repitió la calificación de IA.

| Recorrido | Resultado comprobado |
| --- | --- |
| Configuración y publicación | Registrar visita v3, sólo español, con catálogo Manual, foto y documento PDF obligatorios; recepción con aviso y aprobación por rol, aprobación con PDF que incluye fotos y envío al autor. V4 agregó el inglés completo. |
| Texto en Asistente | `VISITA-000007`: lugar y tipo prellenados, foto y documento enviados, aviso «Recibimos…» recibido y motivo del envío visible. |
| Aprobación y PDF | Otra cuenta con permiso de decidir aprobó la visita; el titular recibió y abrió el PDF con la foto. `VISITA-000007` a `000013` quedaron aprobadas con las cuatro acciones en Hecho. |
| Planilla y D2 | Confirmados panel lateral, catálogo/documento, Fotos, PDF, motivo, momentos y actividad; ↑/↓, filtro Aprobada y Excel abierto con sólo las aprobadas. |
| Audio | `VISITA-000008`: transcripción visible en el chat, formulario prellenado confirmado con foto y documento; aprobación y PDF generados. |
| Formulario inexistente | Aviso recibido y pedido visible en Formularios que faltan; «Préstamo de herramientas» agrupó el nuevo pedido y pasó de 1 a 2. |
| Qué sugerir después | `VISITA-000012` → `PRB1010-000002`: revisión y envío de Bilingual form test v3, apertura y confirmación en el celular; «Central workshop» y «VISITA-000012» quedaron mapeados sin cambios y la cadena conserva ambas respuestas. Se probó «Preguntar siempre» en Manual. |
| Revisión Manual | `VISITA-000009`: sugerencia del asistente revisada por el operador con «Revisar y enviar»; abrió prellenada y se confirmó con foto y documento. |
| Idioma | Original solo, traducción completa, los dos faltantes P24 por separado y excepción individual del catálogo comprobados en el celular, como se detalla arriba. |

- [x] Error de apertura Manual sin prellenado corregido después del piloto, el 2026-10-20. Los dos envíos fallidos v3/es se conservaron como antecedente; la reproducción y el arreglo sobre la misma v5 están registrados abajo.

El primer intento de continuación apuntó al destino v2 anterior y fue rechazado como NotRepresentable antes de crear una salida. Se republicó el mismo contenido de Bilingual form test como v3 con `flow-static-7.3-r8`; el recorrido nuevo sí terminó en `PRB1010-000002`, sin modificar propuestas anteriores.

Cierre verificado: el contenido original de Registrar visita se restauró y publicó como v5 es/en; reglas originales en r4, diseño PDF original de dos preguntas en r7 y sugerencias vacías en r2. Bilingual form test conserva su contenido original, publicado como v3. Se restauraron las traducciones del catálogo nuevo y se lo desactivó (r3), conservando respuestas, archivos, versiones y copias PDF históricas. Cuenta de nuevo en español y chat en su modo original Manual. Se eliminó la sesión temporal de preparación y se ejecutó `aspire stop`: Api y front dejaron de escuchar; PostgreSQL persistente se conservó. Sin push.

### Corrección de apertura Manual — 2026-10-20

Se reprodujo en el Android del titular con Registrar visita v5/es, sólo lugar y foto: el envío Manual vacío fue aceptado por Meta pero falló al abrir; otro envío de esa misma publicación con el lugar prellenado abrió. El transporte mandaba el objeto vacío `prefill_FORM:{}`. Se agregó primero la regresión del despacho Manual y de una propuesta sin campos incluidos; después se omitieron los mapas sin valores, conservando los mapas parciales y poblados. El envío Manual nuevo de la misma v5 abrió vacío en Android, sin republicar ni cambiar JSON, hash, binding o codec. No se completaron ni enviaron respuestas durante esta comparación.

Se conservaron definición v5, cuenta en español y chat en Manual. La sesión temporal de preparación se eliminó y se terminó con `aspire stop`. Las capacidades, esquemas e instrucciones de IA no cambiaron; no se repitió su calificación.

Verificación final: regresiones del despacho en rojo antes del arreglo; 649 tests rápidos de WhatsApp y 6 del transporte en verde. `dotnet test` completo con PostgreSQL y Redis en Docker: 4.911 aprobados, sin omitidos. `dotnet build ArquitecturaBase.slnx`: cero advertencias y errores. Corrección en `4dc36cba`, en main y sin push.

## Formularios: menú y detalle completo

El [review 1–10](../reviews/2026-10-21-code-review-menu-y-detalle.md#cierre-local) quedó corregido y verificado localmente: Docker **5.389/5.389**, front **2.689/2.689**, visual **623/623**, builds limpios y recorrido es/en a 390/320. El piloto propio sigue pendiente; no se usó Meta durante el desarrollo. Las reglas y el alcance vigente se consolidan en [formularios](../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21).

- [ ] Después del review, confirmar con una visita real el recorrido desde Formularios y su bloque Por aprobar, D2 y pantalla completa, retorno a la fila/filtros, flechas según el origen y PDF dentro de la acción que lo generó. Comprobar también la cuenta que sólo decide, sin respuestas ni pantalla completa.
- [ ] Con dos o tres contactos simultáneos, comprobar en Asistente y Atención manual que cada número recibe únicamente su propuesta y contexto, y que un turno lento no detiene a los otros. La integración con proveedores controlados no acredita esta comprobación física.

## Propuesta heredada por confirmar

La administración de dispositivos y sesiones (listar sesiones activas y cerrarlas a distancia)
es una propuesta heredada del diseño inicial y del diseño de Administración retirado.
No está implementada ni tiene una entrega actual acordada: antes de retomarla, confirmar
su alcance y diseñar el manejo de autorizaciones/tokens de OpenIddict. Esto es distinto de
la revocación de todas las sesiones al desactivar o borrar una cuenta, que ya existe.

## Adopción visual y mensajes

- [ ] Completar la [unificación transversal de mensajes](../../../ArquitecturaBaseFront/docs/plans/2026-09-30-unificacion-mensajes.md).
  Configuración y el mecanismo común ya existen; quedan otros consumidores, 429 centralizado,
  recuperación y límites de imports. El adaptador sin props de apariencia y su montaje único
  ya están resueltos; no se vuelven a abrir como pendientes.
- [ ] Sincronizar biblioteca y lienzo externos según el [fundamento visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), incluidos los tableros de Inicio (2026-10-04) y del panel (2026-10-05).
  Incluye las piezas de WhatsApp que quedaron atrás, los tokens Campo y roles de Apariencia, filtros, layout y editor
  de roles; corregir el texto de invitación y notas antiguas del lienzo. Esta auditoría no accedió a esos artefactos.
- [ ] Completar los tableros responsive y estados que todavía enumera el fundamento. Roles,
  Configuración y acceso público ya tienen evidencia local a 320 px; no declarar todos los
  tableros móviles ausentes ni todos los recorridos verificados.
- [ ] Revisar con teclado el desplazamiento de `main` al entrar desde el menú o con foco en
  `body` (Re Pág, Av Pág, Espacio y flechas), pendiente heredado del editor de roles.
  Definir una solución con el diseño antes de cambiar foco o caparazón.
- [ ] Completar la auditoría de accesibilidad móvil y objetivos táctiles del fundamento visual.
  `Button`/`IconButton` compactos conservan 30/26 px y `Page.backTo` una caja de 24 px;
  las capturas desde 320 px no demuestran blancos de 44 × 44 px en todas las acciones.
  Acceso público y los checks móviles del selector de permisos sí tienen tamaños ampliados.
- [ ] Evaluar enlaces reales para Ver/Editar en `RowActions`, para abrir en otra pestaña o copiar
  destino. El separador de acciones y el tema claro ya se corrigieron; no son trabajo pendiente.
- [ ] Confirmar con el diseño las decisiones heredadas por omisión: rol User preseleccionado,
  invitación inicialmente marcada, verificación de medios y formato visible del número.
  Selección masiva, marca y pantallas nuevas requieren su propio contrato y elección.

## Despliegue y operación

- [ ] Ejecutar los dos disparadores y validar el primer despliegue real con la [guía Azure dev](../deploy/azure-dev.md).
  La preparación local de los workflows no acredita Actions, login ni arranque en Azure.
- [ ] Completar la [lista antes del primer despliegue](../guides/despliegue.md#lista-antes-del-primer-despliegue),
  con Redis privado/TLS, prefijos por instalación, secretos, issuer, redirects y certificados.
- [ ] Planificar WhatsApp en producción: el piloto de formularios del 2026-10-11 ya usó una cuenta
  configurada, con pago y número reales. Falta el número de producción y su PIN, las plantillas en su
  cuenta, HTTPS/webhook propio, secreto de la app requerido, monitoreo del token y del costo, y el
  control de cambio de identidad (un número reciclado llega con otro BSUID y no traslada la cuenta).
## Inteligencia artificial

La [calificación OpenAI](../guides/configurar-ia.md#evidencia-para-habilitar-perfiles) registra el modelo de texto y STT exactos, sus perfiles/instrucciones y los 48 casos ficticios; no acredita otra cuenta, proveedor ni datos reales.

- [ ] Verificar Anthropic con credenciales propias y calificar su modelo exacto antes de declararlo. Registrar proveedor, modelo, resultado, uso y límites sin secretos; las pruebas de OpenAI no lo sustituyen.
- [ ] Revisar egress y retención de las cuentas OpenAI y Anthropic antes de usar datos reales
  (`store:false` no es retención cero). Hasta entonces, el piloto usa datos ficticios.
- [ ] Restaurar en un ambiente aislado la base, el anillo de claves y el `InstallationId`, y comprobar que se
  descifran la credencial, los comprobantes y el contenido del asistente antes de habilitar tráfico.
- [ ] Probar una segunda instalación independiente (base, anillo e ID, UserSecretsId, prefijo de Redis,
  origen y credenciales propios) y comprobar que no comparte nada con la primera.
- [ ] En el despliegue: anillo de IA en secrets o Key Vault, alertas y exportación OpenTelemetry, dos
  réplicas reales y apagado con turnos en curso (deben quedar Uncertain o Expired, sin reenviarse).

## Privacidad de la instalación de prueba

El plan de WhatsApp registró una política publicada en el repo `ezeellena2/privacidad`, para
«Servicios Ya», con Ezequiel Ellena (Rosario, Santa Fe) como responsable y edad mínima de 18 años.
Es información de aquella instalación, no una premisa de cada proyecto derivado. El contenido
externo y la vigencia jurídica no se verificaron en esta auditoría.

Se conservan estos pendientes del registro original para revisar antes de incorporar personas reales:

- [ ] Procedimiento probado de borrado/anonimización: cuenta, roles/logins, códigos/enlaces/auditorías,
  invitaciones, contactos, mensajes, lecturas por operador, operaciones salientes y receipts
  (mensajes, lecturas y operaciones antes de contactos por sus FK Restrict), autorizaciones
  y tokens OpenIddict, y copias de correos en Enviados de Gmail.
- [ ] Plazos y retención de `LoginCodes`, `LoginLinks`, `LoginAudits` (IP y user agent), tokens,
  contactos, cuentas borradas y `UserInvitations`; reflejarlos en la política y el procedimiento.
  El cuerpo de mensajes ya se limpia a los 90 días mientras la Api corre; no implica limpieza al estar apagada.
- [ ] Revisión profesional de transferencias internacionales, registro de la base, domicilio del
  responsable y plazos prometidos. Es una consulta pendiente heredada, no asesoramiento jurídico.
- [ ] Alinear nombre del negocio en web/correos/orientación de acceso y política; enlazarla desde
  login, registro, canje histórico, pie y comunicaciones de acceso antes de crear cuenta.
  El bot anterior está retirado; las superficies nuevas se diseñan primero.
- [ ] Canal humano de contacto en los recursos de acceso de ambos idiomas y un remitente propio para correo;
  revisar el tratamiento de los datos por los proveedores.
- [ ] Revisar que las suscripciones del webhook incluyan solo los campos usados (`messages`).
- [ ] Revisar opt-in y propósito del código de vinculación, que usa la misma plantilla del ingreso;
  conservar en la retención qué cuenta pidió un código para el contacto de otra persona.
- [ ] Evaluar exclusión de invitaciones («no me invites») y la evidencia de consentimiento:
  actualmente queda quién y cuándo lo confirmó como administrador, no una aceptación del destinatario.
- [ ] Revisar la confirmación del contacto al invitar, para mitigar números/correos mal cargados.
- [ ] Evaluar un límite diario de invitaciones además de la espera por cuenta y su costo.
- [ ] Evaluar si `users.read` debe ver «leída» o solamente «entregada» para la última invitación.

El plan también registraba dos actualizaciones ya hechas: retención del cuerpo a 90 días y
adaptación de la política a perfil/invitaciones. No se vuelven a abrir como tareas de implementación;
la coherencia con un producto derivado sí se revisa al preparar su producción.

## Notas heredadas por confirmar

- La carrera de arranque en tests de configuración WhatsApp/Google requiere revisar el test
  actual antes de actuar: `WhatsAppRegistrationTests` ya invoca `IStartupValidator`; no afirmar
  que conserva el defecto del plan original.
- Un índice para el tope diario de códigos es una mejora condicionada al volumen. El canal actual
  almacenado es `Phone`, no `WhatsApp`; validar consulta y plan de ejecución antes de proponerlo.

Cada casilla se cierra con evidencia del trabajo concreto y fecha/commit. Una modificación de
documentación no acredita ejecución, aprobación externa ni despliegue.

## Formularios: baja, reactivación y eliminación

Pendiente por pedido del usuario: primero code review, después piloto real con Meta. Las [reglas implementadas](../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21) y la integración local no acreditan ese piloto.

- [ ] Con un envío abierto, dar de baja: deja de ofrecerse en selector, asistente y sugerencias. Responder ese envío antes de vencer y comprobar respuesta, aprobación, avisos y PDF conservados.
- [ ] Al vencer el último envío abierto, comprobar que Meta depreca los Flows de todas las versiones; listado y editor deben mostrar su confirmación. Si un resultado queda Sin confirmar, observar la consulta previa a cualquier repetición.
- [ ] Reactivar: durante la publicación sigue en Dados de baja; después de aprobar Meta la versión nueva, vuelve a Activos, se manda con Flows nuevos y continúa la numeración.
- [ ] Eliminar un formulario sin respuestas ni envíos abiertos: desaparece, el prefijo queda reservado y sus Flows publicados se deprecan. Comprobar que un formulario con respuestas no ofrece Eliminar.

Para el piloto se levanta Aspire con el túnel, se conservan los datos reales y al terminar se restaura la configuración de prueba y se ejecuta `aspire stop`. No se inició en este desarrollo.

## Formularios: catálogos E1

La carga de una vez, el valor tipado y elegir varias están implementados según las [reglas de E1](../features/formularios.md#catálogos-base-de-la-etapa-e1--2026-10-21). El cierre local conjunto está en curso. Primero va el code review; no se inició el piloto real con Meta.

- [ ] Pegar un catálogo de proveedores de OCHA y después importar su XLSX: revisar altas, cambios, bajas y errores antes de aplicar; comprobar que conservaron las identidades por código.
- [ ] Usar un catálogo decimal y comprobar texto y valor histórico en la respuesta y el PDF. La columna numérica de una elección simple en Excel conserva su límite de 15 cifras significativas; exportar el catálogo o los valores de varias conserva texto canónico exacto.
- [ ] Publicar con r9 un formulario con casillas y mandar al celular: confirmar el formato real del `nfm_reply`, el orden que entrega Meta y la diferencia entre `[]` y clave ausente. La implementación conserva el orden recibido; el orden físico de selección sigue por comprobar.
- [ ] Confirmar una respuesta con varias opciones y archivos; revisarla en la planilla, el detalle y el chat, filtrar «alguna de» y abrir el Excel y el PDF. Comprobar las etiquetas históricas después de editar el catálogo.

La prueba se hace con Aspire y el túnel, conserva los datos reales y termina restaurando la configuración de prueba y ejecutando `aspire stop`. Los escenarios locales con Meta controlado y las pantallas de datos sintéticos no acreditan este piloto.
