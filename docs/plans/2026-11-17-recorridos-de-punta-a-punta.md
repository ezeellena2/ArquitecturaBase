# Recorridos de punta a punta con Playwright

Propuesta del 2026-11-17, a pedido del usuario. Cada caso de uso tendría un recorrido en un navegador real contra la API, la base
y el ingreso reales, y los proveedores externos serían dobles. La corrida guarda video y traza de cada recorrido, para ver
pantalla por pantalla qué hizo. **Sin programar.** Las decisiones de abajo son recomendaciones que el usuario tiene que confirmar.

## Por qué

Hoy cada capa se prueba por su lado:

- **Backend:** unas 11.600 pruebas contra PostgreSQL y Redis reales, sin navegador.
- **Front:** unas 6.000 pruebas de pantallas en un navegador simulado (jsdom), con un servidor falso, más las pruebas visuales
  de Playwright (`npm run test:visual`), que comparan capturas con datos armados.
- **El puente** son las cohortes HTTP: el front lee las respuestas reales que guardó la corrida del backend.

Nadie prueba todo junto: un navegador real con el `dist` de producción, el ingreso por OIDC con su código, la API, la base, los
archivos y los trabajos en segundo plano. Eso lo cubren hoy las pruebas manuales, y por eso casi todos los planes terminan con
«falta la prueba manual», como la de punta a punta del 2026-11-09, que se hizo clic por clic con el usuario.

**Lo que no reemplaza.** Las pruebas con datos simulados no cierran los recorridos con proveedores reales
([verificación y producción](verificacion-y-produccion.md)). Siguen siendo manuales:

- que Meta entregue el mensaje en un teléfono;
- la pantalla de Google y el SMTP real;
- la calidad de las respuestas de la IA, que tiene su propia [calificación](../guides/configurar-ia.md);
- la homologación de ARCA y el robot contra el portal real;
- la cámara y el toque en el celular, con [probar en el celular](../guides/probar-en-el-celular.md);
- el juicio visual.

La prueba manual que queda se achica a eso.

## Decisiones recomendadas (a confirmar)

1. **Un recorrido por caso de uso, no uno por botón.** Cada recorrido cubre el camino feliz y uno o dos bordes que el usuario
   vería (sin permiso, 409, cupo, archivo inválido). Los casos borde restantes siguen en el backend y en las pruebas de pantallas,
   donde son rápidos y estables. Llevarlos todos al navegador real llevaría la corrida a varias horas y multiplicaría las fallas
   por tiempo, como las que se vieron en la corrida completa del 2026-11-17.
2. **Dónde viven:** en el front, en `e2e/`, con su propio `playwright.e2e.config.ts` y `npm run test:e2e`, separados de
   `test:visual`. El front ya usa Playwright, y los selectores por rol y nombre son los mismos de Testing Library.
3. **El host de la API:** se reusa el patrón del arnés de vista previa (`BusinessFormsManualAppPreview`).
   - Es un test de integración que sólo arranca con sus variables de entorno.
   - Levanta la Api con Kestrel en `https://localhost:7445`, sirve el `dist` del front y usa el PostgreSQL y el Redis de
     Testcontainers y los dobles de `ApiFactory`.
   - Publica un archivo de estado y espera un `.done`. Playwright lo lanza en `globalSetup` y lo cierra en `globalTeardown`.
   - No usa Aspire: Aspire habla con los proveedores reales y manda los códigos de ingreso a un buzón real.
4. **Los dobles son los de la integración:**
   - correo: `CapturingEmailSender`, de donde salen los códigos;
   - IA: `FictionalLlmClient` y `FictionalPlacesClient`;
   - Meta: la cola de envío en memoria, los clientes HTTP sin red y los webhooks firmados de los tests de WhatsApp;
   - ARCA: `FakeArca` y `FakeArcaWeb`;
   - Google: `ExternalLoginTestController`, que deja la misma cookie que la vuelta de Google.

   El host suma un controller de control, sólo en `TestFeatures` y registrado sólo en ese host, para:
   - leer el último código de un correo;
   - hacer entrar un mensaje de WhatsApp o la respuesta de un Flow;
   - ver lo que se mandó a Meta;
   - adelantar el reloj, por ejemplo para cerrar la ventana de 24 h.

   La Api no lo conoce, y `ExplicitRouteInventoryTests` lo deja afuera.
5. **Datos sin orden:** la base es nueva en cada corrida. Cada recorrido crea lo suyo con nombres únicos y no depende de otro
   recorrido ni del orden. El 2026-11-17, dos siembras compartidas rompieron capturas según el orden de las clases. Lo que el
   recorrido prueba se hace por la pantalla; lo previo (cuentas, roles, un formulario publicado) va por la API o el controller de
   control.
6. **Ingreso por la pantalla:** cada recorrido entra con el código capturado y se mueve con los enlaces de la app. En el arnés,
   los tokens viven en memoria y la renovación silenciosa no vuelve, así que no se recarga la página entera.
7. **Evidencia:** video, traza y una captura por paso de cada recorrido, más el reporte HTML de Playwright. No compara píxeles:
   eso lo hace `test:visual`. En la corrida no hay ningún secreto ni clave real.
8. **Cuándo corre:** una sola vez al cerrar un desarrollo, después de la corrida completa y con la PC quieta.
   - Usa uno o dos workers y no tiene reintentos automáticos: un recorrido que falla se investiga, como pide
     [validar un cambio](../guides/validar-un-cambio.md#verificación-y-cierre).
   - El objetivo es que la corrida entera tarde menos de 45 minutos.
   - En un corte se corre sólo el recorrido que toca lo cambiado.

## Los recorridos

Salen de las rutas del front (`src/app/routes.tsx`) y de las reglas de `docs/features`. Entre corchetes, el doble que usa.

**Identidad**

- R1. Crear una cuenta con el código por correo y llegar a Inicio. [correo]
- R2. Entrar con un código equivocado (ver los intentos restantes) y después con el correcto. [correo]
- R3. Entrar con Google en una cuenta con el correo confirmado. [vuelta de Google simulada]
- R4. Con el registro cerrado, una cuenta no invitada recibe «no invitado»; una desactivada, «desactivada». [correo]
- R5. Cerrar la sesión en una pestaña también la cierra en la otra.
- R6. Desde el perfil, vincular el número de WhatsApp con su código y la confirmación al correo. [Meta, correo]

**Administración**

- R7. Dar de alta a un usuario con invitación; la persona entra con su código y queda verificada. [correo]
- R8. Reenviar la invitación antes del minuto avisa cuánto falta. [correo]
- R9. Crear un rol y asignarlo: con el permiso aparece el menú; sin él, la pantalla dice «No tenés permiso» en su dirección.
- R10. Quien administra usuarios no puede dar un rol que no tiene ni desactivar al último administrador.
- R11. Editar el mismo rol en dos pestañas: la segunda recibe el conflicto, relee y conserva su borrador.
- R12. Cambiar el modo de registro y la apariencia, y ver el efecto en `/registro`.

**Formularios**

- R13. Crear un formulario sólo para la app, chequearlo y publicarlo hasta la versión 1 activa.
- R14. Publicarlo en WhatsApp hasta «Publicado» y abrir la vista previa. [Meta]
- R15. Una publicación que queda sin confirmar se descarta con «Descartar esta publicación». [Meta que no responde]
- R16. Cargarlo en la web con una foto; la respuesta aparece en la planilla con su número y el canal App.
- R17. Mientras se carga se publica una versión nueva: se pasa a la nueva sin perder lo cargado; un archivo inválido se rechaza.
- R18. Una regla pide aprobación a un rol y manda un aviso; otra cuenta aprueba desde «Por aprobar». [Meta]
- R19. Rechazar con motivo; el aviso a quien la cargó queda «No aplica» si no tiene WhatsApp.
- R20. Diseñar el PDF, verlo con datos de ejemplo, aprobar y descargar la copia fija.
- R21. Quien la cargó la corrige antes de la decisión; quien puede enmendar la anula con motivo.
- R22. Planilla con filtros y conteos, detalle completo, Excel y tablero por semana.
- R23. Crear una carpeta y una subcarpeta, mover un formulario y un catálogo, y navegar desde el menú.
- R24. Dar de baja, reactivar y eliminar un formulario sin respuestas; con respuestas no se puede eliminar.
- R25. Agregar «Factura» desde la biblioteca, con y sin permiso de emitir; revisar «Formularios que faltan».
- R26. Un documento recibido se convierte en formulario con la IA hasta aceptar el borrador. [IA]
- R27. Quien sólo completa ve sólo lo suyo en `/formularios/respuestas` y no entra al editor.

**Catálogos**

- R28. Crear un catálogo manual con campos extra, usarlo en una pregunta y cargar eligiendo una opción.
- R29. Importar un XLSX con vista previa y un valor ambiguo; después exportarlo a Excel.
- R30. Eliminar una opción usada se rechaza y se da de baja; una carga en curso que la tenía avisa.
- R31. Datos de las personas con un filtro por persona; a quien le falta el dato se le avisa.

**WhatsApp**

- R32. Entra un mensaje, aparece en la bandeja y el operador responde; pasadas 24 h, la ventana está cerrada. [Meta, reloj]
- R33. Mandar un formulario a un contacto vinculado; su respuesta cae en la planilla con el canal WhatsApp. [Meta]
- R34. Configurar el asistente y pasar el chat a Asistente; un mensaje entrante recibe la respuesta redactada. [Meta, IA]
- R35. En Atención manual, revisar una sugerencia, quitar un dato y enviarla. [Meta, IA]
- R36. Llega un audio: escucharlo y transcribirlo. [Meta, IA]

**IA**

- R37. Cargar la conexión, listar los modelos, probarla y guardarla hasta «Disponible»; una clave rechazada se avisa. [IA]
- R38. Información del negocio: buscar el lugar y guardar los datos confirmados. [lugares]
- R39. Inicio: una consulta, el historial, pedir un formulario y aceptar la propuesta. [IA]
- R40. Inicio con una planilla (propone un catálogo), un PDF y un audio; un archivo de más de 10 MiB se rechaza. [IA]

**ARCA**

- R41. Conectar con certificado (pedido, certificado, prueba, padrón y punto de venta) hasta «activa». [ARCA]
- R42. Un certificado de otra CUIT o vencido se rechaza; dos pestañas dan conflicto; un punto de venta no disponible se avisa.
  [ARCA]
- R43. Cargar una «Factura», confirmar «¿La emito?», ver el CAE y el PDF fiscal y anularla con nota de crédito. [ARCA]
- R44. Delegación guiada: la verificación primero avisa que falta delegar y después pasa. [ARCA]

## Etapas

1. **El arnés y tres pilotos (R2, R13 y R16).**
   - Lo que se arma: el host, el controller de control, la configuración de Playwright y los scripts.
   - Lo que se mide: la duración de cada recorrido y diez corridas seguidas sin fallas antes de seguir.
   - Lo que se escribe en el mismo desarrollo: el `AGENTS.md` de `e2e/`, el
     [mapa de instrucciones](../architecture/mapa-de-instrucciones.md) y un test que impide registrar el controller de control
     fuera del host.
   - Es la etapa más grande: más o menos un día.
2. **Identidad y administración:** R1, R3 a R12.
3. **Formularios y catálogos:** R14, R15, R17 a R31.
4. **WhatsApp e IA:** R32 a R40.
5. **ARCA:** R41 a R44.
6. **Cierre:**
   - la guía [validar un cambio](../guides/validar-un-cambio.md) suma `npm run test:e2e` a la corrida completa;
   - en cada plan con «falta la prueba manual», se anota qué recorrido la cubre y qué queda manual.

Las etapas 2 a 5 llevan entre medio día y un día cada una.

## Riesgos

- **Fallas por tiempo:** son las mismas que en la corrida completa del front. Se manejan con pocos workers, plazos amplios en la
  primera carga de cada pantalla, la PC quieta y nada de reintentos que las tapen.
- **Mantenimiento:** un texto o un nombre accesible que cambia rompe sus recorridos, que se arreglan en el mismo cambio, como
  hoy las pruebas de pantallas.
- **La duración crece con cada desarrollo:** el tope es 45 minutos; si se pasa, se parte en grupos por módulo.
- **El host compila dentro del proyecto de integración:** no se corre a la vez que `dotnet test`, porque pisaría sus DLL.

## Cuándo

Después de la prueba integral de esta tanda, sin trabarla. Cuando el usuario confirme las decisiones, se programa por etapas.
