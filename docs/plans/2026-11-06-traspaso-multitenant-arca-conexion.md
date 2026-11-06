# La conexión con ARCA: traspaso 6 al multitenant (2026-11-06)

Autor: el chat guía de ArquitecturaBase. Destino: ArquitecturaBaseMutitenant, para lo que su hoja de ruta de formularios
pone **después del bloque**: la decisión 76 («lo primero son las integraciones: ARCA, Mercado Libre, Mercado Pago y
webhooks») y el módulo `Integrations` de su §3, con conexiones y credenciales por organización.

El [traspaso 5](2026-11-03-traspaso-multitenant-lecturas-y-filtros.md) nombró la conexión con ARCA como diseñada y sin
programar. Del 2026-11-03 al 2026-11-05 se programó entera acá: es la etapa 1 de la
[hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md). Una instalación se conecta con ARCA con el certificado de
la empresa, prueba la conexión, trae del padrón los datos de la empresa y elige el punto de venta, sin emitir comprobantes.
Este traspaso cuenta:
- qué decidió el usuario;
- cómo quedó;
- qué se encontró al programarla;
- qué tiene que decidir el multitenant al adaptarla.

**Como los anteriores, no es código para copiar** (decisión 18 de su hoja de ruta): se toman las pantallas y las reglas de
producto, y el código sale del multitenant, con sus organizaciones, su RLS, `[FeatureGate]` y sus fichas. Tampoco hace
falta para dibujar (decisión 108): los tableros de acá son un ejemplo para adaptar con sus piezas y su decisión 43.

**No depende de los traspasos 4 y 5,** que tratan de formularios. Se puede incorporar aparte, cuando el multitenant sume
ARCA a su hoja de ruta.

## 1. En pocas palabras

- **Decidido y programado:** conectar una organización con ARCA con el certificado de la empresa, probar la conexión, traer
  la razón social, la condición frente al IVA y el domicilio fiscal del padrón, y elegir el punto de venta. Sin efecto
  legal (§2 y §3).
- **Un módulo opcional con una carpeta por familia de servicios:** la conexión, el padrón y la facturación. Cada familia se
  quita sola y cada empresa elige qué servicios usa (§3).
- **Lo delicado es la credencial:** la clave privada se genera en el servidor y nunca sale, va cifrada, y el ticket de
  acceso de ARCA se comparte entre hosts con una reserva, sin HTTP dentro de una transacción (§3).
- **La retirada está probada:** dos agentes siguieron la guía en copias, para el módulo entero y para una familia (§4).
- **Lo que tiene que decidir el multitenant** (§5): de quién es la conexión (de la organización o de cada empresa), si ARCA
  es su propio módulo o va dentro de `Integrations`, y dónde vive el certificado de la plataforma para la delegación.
- **Falta la homologación real** con una CUIT del usuario, acá también (§6).
- **El código todavía no está en GitHub:** los commits son locales. Desde esta PC se leen en
  `C:\Users\ezequ\source\repos\ArquitecturaBase` y `C:\Users\ezequ\source\repos\ArquitecturaBaseFront` (§7).

## 2. Lo que decidió el usuario

Del 2026-10-31 al 2026-11-05, viendo los tableros y las propuestas:

1. **La factura primero** (la C, después A y B, con nota de crédito). Esta etapa sólo conecta; emitir llega en la etapa 3
   de la hoja de ruta de ARCA.
2. **Tres modos de acceso, programando sólo el primero:** el certificado de la empresa, la delegación a la CUIT de la
   plataforma guiada y la delegación automatizada con un robot propio (Playwright), donde la clave fiscal del cliente queda
   sólo en memoria. El modelo de datos ya admite los tres; la pantalla muestra sólo el primero.
3. **Un ambiente por vez:** la conexión es de homologación o de producción. Pasar a producción es configurarla de nuevo.
4. **Sólo certificados nuevos, generados en la app.** No se importa uno existente: la clave privada no viaja, y compartir el
   certificado con otro sistema traba los tickets de acceso.
5. **El alta en la misma página, por zonas que se habilitan de a una (AC1-A),** no un asistente en un diálogo (AC1-B).
6. **La conexión activa, por zonas con su acción en la banda (AC2-A),** no una ficha con historial (AC2-B).
7. **Una carpeta por familia de servicios** ([ADR 0025](../decisions/0025-familias-de-servicios-arca.md)): ninguna usa a
   otra y cada una se quita sola.
8. **Cada empresa elige qué servicios de ARCA usa.** Sólo esos se autorizan, se prueban y muestran su zona; la constancia
   de inscripción es obligatoria, porque trae los datos de la empresa.
9. **ARCA se puede quitar entero,** como WhatsApp, la IA o los formularios.
10. **Las tablas de ARCA como catálogos y el autocompletar de la CUIT del cliente** pasan a la etapa 3, ya puestos en una
    carpeta «ARCA».

## 3. Cómo quedó

La regla vigente completa está en [ARCA](../features/arca.md); acá va lo que importa para adaptarla.

### Backend

- **Módulo y familias:** `Modules/Arca` en las cuatro capas, con una carpeta por familia debajo de cada carpeta de capa
  (`Connection`, `Registry`, `Invoicing`). Una familia usa sólo `Connection`; `Connection` conoce a las demás sólo por un
  puerto, `IArcaServiceDefinition` (sus servicios, su prueba y si su configuración está completa), registrado una vez por
  familia. Un test de arquitectura lo controla con un caso que falla a propósito.
- **Permisos:** `arca.read` para leer y `arca.manage` para cambiar algo o llamar a ARCA.
- **Datos:** cinco tablas. `ArcaConnections` (una por instalación, con un índice único), `ArcaCertificates` (a lo sumo uno
  activo y uno pendiente), `ArcaAccessTickets` (uno por certificado y servicio), `ArcaRegistryCompanies` y
  `ArcaInvoicingSettings`. Las de cada familia cuelgan de la conexión con borrado en cascada. Desconectar borra todo de
  verdad: hay secretos.
- **Estados:** sin conexión → esperando el certificado → sin verificar → activa ⇄ requiere atención. El servidor guarda el
  resultado general de la última prueba y su hora, no el de cada servicio.
- **El certificado:** clave RSA de 2048 bits generada en el servidor y pedido PKCS#10 con SHA-256 (`C=AR`, `O` y `CN` con el
  alias, `serialNumber` «CUIT …»). Al subir el `.crt` se comprueban la clave, la CUIT, el emisor del ambiente y el
  vencimiento, cada uno con su error. Renovar genera otro pedido mientras el actual sigue andando; el nuevo reemplaza al
  anterior sólo con la prueba en verde.
- **Cifrado:** la clave privada y el ticket van cifrados con un anillo propio del módulo (AES-GCM, como el de la IA), con un
  AAD atado a la instalación, la clave del anillo, el propósito, la conexión y el certificado. La Api no arranca sin el
  anillo ([ADR 0023](../decisions/0023-arca-como-modulo-opcional.md)).
- **El ticket de acceso (WSAA):** se reutiliza mientras le quede margen. Renovarlo lo hace un host por vez: una transacción
  corta toma una reserva con dueño y vencimiento, el pedido al WSAA va fuera de la transacción, y otra transacción guarda el
  ticket sólo si la reserva sigue siendo propia. Si ARCA contesta «ya posee un TA válido», la fila guarda hasta cuándo no se
  puede pedir otro (2 minutos en producción, 10 en homologación) y la prueba lo informa; ningún pedido espera minutos.
- **La prueba:** pide un ticket por servicio elegido y corre la prueba de su familia, fuera de la transacción. Lo que
  contesta ARCA no es un error del pedido: es el resultado de cada servicio con un motivo estable (sin autorizar, reloj
  desfasado, bloqueado, sin respuesta). Se guarda sólo si el certificado probado sigue siendo el activo.
- **Familias:** el padrón (`ws_sr_constancia_inscripcion`, `getPersona_v2`) guarda los datos de la empresa con cada prueba;
  la facturación (`wsfe`) responde a `FEDummy` y lista los puntos de venta de `FEParamGetPtosVenta`, sin los dados de baja y
  con los bloqueados apagados.
- **HTTP:** doce rutas MVC bajo `api/arca/{connection,registry,invoicing}`; las escrituras llevan la revisión esperada y
  responden 409. Tres límites por operador: lectura, escritura y lo que llama a ARCA.
- **Clientes SOAP propios** (`HttpClient` y `System.Xml.Linq`): sólo HTTPS, sin seguir redirecciones, con tope de tamaño,
  sin reintentos automáticos y con los loggers del `HttpClient` apagados. Los logs no llevan token, firma, clave ni
  certificado, y la CUIT va enmascarada.
- **Robustez proporcional:** la prueba sólo lee, así que no usa un motor durable; alcanzan la revisión, el 409 y la reserva
  del ticket.

### Front

- `/configuracion/arca`, con `arca.read`, en una sola pantalla sin estado en la URL.
- **El alta (AC1-A):** cinco zonas numeradas (Datos, Pedido, Certificado, Prueba y una por familia que lo necesite, hoy el
  punto de venta). La zona en curso está abierta, las hechas plegadas con su resumen en la banda y las siguientes apagadas;
  la que se abre recibe el foco. Se puede salir y volver.
- **La conexión activa (AC2-A):** Empresa, Servicios (con «Lo usa esta empresa» por servicio y «Probar»), Certificado (con la
  renovación adentro) y la zona de cada familia. Aviso amarillo treinta días antes del vencimiento y rojo vencido.
- **Los estados (AC3):** sin conexión, sólo lectura, cargando, no se pudo leer, probando, un servicio sin autorizar, un
  certificado que no corresponde, sin puntos de venta, un 409 con «Revisar» y desconectar con confirmación.
- **Una carpeta por familia** también en el front, y la página conoce las zonas de las familias por un registro chico:
  quitar una familia es borrar su carpeta y su línea.

El diseño elegido, con los textos, lo que se fijó al programar y las piezas, está en
`ArquitecturaBaseFront/docs/design/arca-conexion.md`.

## 4. Lo que se encontró al programarla (no repetir)

1. **Ningún pedido a ARCA dentro de una transacción.** El ticket necesita dos pasos guardados (reservar y guardar) con el
   WSAA en el medio: cada paso corre en su propio scope y su propia transacción.
2. **Sin reintentos automáticos.** La resiliencia común de los `HttpClient` se saca en los de ARCA: un pedido repetido a
   ciegas puede emitir dos veces o pedir un segundo ticket que ARCA rechaza y bloquea por minutos.
3. **Un puerto con una implementación por familia** se registra como «todos avisados» (uno por familia), no como un servicio
   único.
4. **El resultado de una prueba lenta no pisa una renovación:** se guarda sólo si el certificado probado sigue siendo el
   activo. La prueba de una renovación en rojo no guarda nada.
5. **Detalles de ARCA confirmados con los WSDL de homologación:**
   - WSFEv1 va en `http://ar.gov.afip.dif.FEV1/`, con los errores en `Errors/Err`; el error 602 es «sin resultados»: una
     lista vacía, no una falla;
   - el padrón es `getPersona_v2` en `http://a5.soap.ws.server.puc.sr/`, con los elementos sin calificar y el `SOAPAction`
     vacío;
   - el pedido de ticket lleva las horas con el huso explícito y un `uniqueId` al azar: con la hora, dos pedidos en el mismo
     milisegundo chocaban;
   - `cms.bad` no es un reloj desfasado: el reloj se reconoce por `generationTime` o `expirationTime`.
6. **El estado de cada servicio vive en la visita.** El servidor guarda el resultado general y su hora; al volver, los
   renglones quedan sin estado, y «Sin probar» aparece sólo si nunca hubo una prueba.
7. **«Activar la conexión» aparece desde «Sin verificar»,** no desde que se pide el certificado.
8. **Para poder quitar una familia sola,** los tests de la conexión usan una definición de familia opcional genérica, sin
   código de la familia. Si usan la facturación de ejemplo, quitarla rompe decenas de tests ajenos.

## 5. Adaptación al multitenant

Leído en el multitenant en `c339b58d` (front `c5172e65`), con la hoja de ruta de formularios hasta la decisión 108. ARCA
aparece sólo en la decisión 76 y en «Después del bloque» («un módulo `Integrations`, con conexiones y credenciales por
organización»; quedan abiertas las 13 decisiones de su estudio).

| Tema | En ArquitecturaBase | Qué evaluar en el multitenant |
| --- | --- | --- |
| **De quién es la conexión** | Una por instalación, con la CUIT que se carga en el alta | Una organización puede tener varias empresas, cada una con su CUIT (`TaxId`, ficha de identificación fiscal). ARCA trabaja por CUIT: la conexión puede ser **de cada empresa** (una organización con dos razones sociales factura con cada una) o **de la organización** (una sola CUIT). Lo decide el usuario. Si es por empresa, la CUIT sale de la ficha de la empresa y no se escribe. |
| **Módulo** | `Arca`, opcional, con una carpeta por familia (ADR 0023 y 0025) | Su §3 prevé un módulo `Integrations` con conexiones y credenciales por organización. Decidir si ARCA es un módulo propio, con su `[FeatureGate("arca")]` vendible aparte, o una integración dentro de `Integrations`. Las familias valen en los dos casos. |
| **Datos y aislamiento** | Tablas propias, una conexión por instalación | `ITenantOwned` en el esquema `tenant`, con RLS y su test de aislamiento por ruta; el índice único pasa a ser por organización (o por empresa). Desconectar sigue borrando de verdad. |
| **Secretos** | Anillo propio del módulo; AAD con instalación, clave, propósito, conexión y certificado | Sumar la organización (y la empresa, si va por empresa) al AAD, o usar lo que ya usan para los tokens de WhatsApp por número. La clave privada nunca sale del servidor ni va a un lector. |
| **Delegación (modos 2 y 3)** | El modelo los admite; no se programaron | Encajan mejor allá: una sola CUIT de la plataforma para todas las organizaciones. Su certificado y sus tickets serían **globales** (de la plataforma, no de una organización), y el bloqueo «ya posee un TA válido» afectaría a todas a la vez: el ticket compartido con reserva se vuelve más importante. |
| **Permisos y módulo habilitado** | `arca.read` y `arca.manage`; sin módulo, ARCA no aparece en el menú | Permisos Business de su catálogo y `[FeatureGate]`: apagado responde 404 y `/api/me` no lo lista. Si va por empresa, el alcance de empresa de sus roles. |
| **CUIT** | Validador propio del dígito verificador | Su `TaxId` y `ArgentineCuitValidator` ya existen: se reusan. |
| **Límites** | Tres políticas por operador | Sus límites por organización de la E9; lo que llama a ARCA, con el suyo. |
| **Pantalla** | Administración → Configuración → ARCA | En la configuración de la organización o en la ficha de cada empresa, según la primera fila. Botones con su decisión 43. |
| **Zonas que se pliegan** | El alta pliega las zonas hechas y apaga las siguientes (AC1-A) | Su front prohíbe paneles o filas expandibles salvo excepciones escritas (front `formularios.md` §Prohibido, `Criterio-Dialogos`): sería una excepción más, como las de la E20. |
| **Formularios** | La etapa 1 no depende de formularios; las siguientes sí (motor para documentos y factura C) | Las etapas 2 y 3 de la hoja de ruta de ARCA usan renglones, cálculos, carpetas y el resultado externo en la respuesta: van después de su E19 y sus carpetas. |

**Lo que tiene que decidir el usuario allá:**
- de quién es la conexión: de cada empresa o de la organización;
- ARCA como módulo propio o dentro de `Integrations`;
- las zonas plegables del alta, contra su regla de paneles expandibles.

## 6. Pendiente acá

- **La homologación real,** con un certificado de WSASS para una CUIT del usuario: confirmar los emisores de cada ambiente
  y la condición frente al IVA con CUIT reales, y ver que la homologación devuelva puntos de venta y datos del padrón. Lo
  que salga se suma a [ARCA](../features/arca.md) y vale también para el multitenant.
- **Las etapas 2 a 6** de la [hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md): motor para documentos,
  factura C, factura A y B, delegación guiada y delegación con robot. Ninguna está diseñada en detalle todavía.

## 7. Dónde está cada cosa en ArquitecturaBase

- **Regla vigente:** [ARCA](../features/arca.md). **Operación:** [configurar ARCA](../guides/configurar-arca.md), con los
  pasos dentro de ARCA (WSASS en homologación, producción y renovación). **Retirada:** [quitar ARCA](../guides/quitar-arca.md),
  con el módulo entero, una familia sola y las comprobaciones de las dos pruebas de fuego.
- **Decisiones:** [ADR 0023](../decisions/0023-arca-como-modulo-opcional.md) (módulo y credenciales) y
  [ADR 0025](../decisions/0025-familias-de-servicios-arca.md) (familias).
- **Diseño y plan:** [diseño de la etapa 1](2026-11-03-arca-etapa-1-conexion.md), [plan de implementación](2026-11-03-arca-etapa-1-implementacion.md)
  (nueve tareas, con las diferencias anotadas en cada una) y la [hoja de ruta](2026-10-31-arca-formularios-estandar.md).
- **Diseño del front:** `ArquitecturaBaseFront/docs/design/arca-conexion.md`, con la entrada «ARCA» de `visual-baseline.md`.
- **Tableros:** el lienzo [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), filas «ARCA ·
  conexión»: AC1-A y AC2-A (elegidas), AC1-B y AC2-B (descartadas) y AC3 (estados).
- **Commits:**
  - backend: diseño `ad0ed362`, ADR 0023 y plan `7b3217d4`, ADR 0025 `0ef625a6`; tareas `bb9a2736`, `c88d341d`, `51bfa5f5`,
    `4bb60df5`, `21921cd3`, `b40556d5`, `a43d8779` y `73b56980`; cierre `da8dc584` y `15a1cb7c`;
  - front: `cfa4cbd7`.
- **Código de referencia:**
  - backend, en `Modules/Arca` de cada capa: `ArcaConnection` y `ArcaAccessTicket` (Domain); `ArcaConnectionService`,
    `ArcaConnectionTestService`, `ArcaAccessTicketStore` y `ArcaServiceCatalogPolicy` (Application); `ArcaCredentialService`,
    `ArcaCmsSigner`, `ArcaAccessTicketProvider`, `SoapEnvelope`, `ArcaAuthClient`, `ArcaInvoicingClient` y
    `ArcaRegistryClient` (Infrastructure); los tres controllers (Api);
  - tests: `FakeArca` (un ARCA simulado con los mismos sobres SOAP), `FakeArcaCertificateAuthority`,
    `ArcaAccessTicketProviderTests` (el ticket entre dos hosts) y `ArcaFamilyBoundaryTests`;
  - front, en `src/features/arca/`: `pages/ArcaSettingsPage.tsx`, `connection/` (`SetupZones`, `ActiveSections`, `ArcaZone`,
    `CertificatePicker`, `ServiceRows`, `families.ts`), `registry/CompanySection.tsx` e `invoicing/PointOfSaleZone.tsx`.

## 8. Prompt para el chat guía del multitenant

```text
Leé el traspaso 6 de ArquitecturaBase, en
C:\Users\ezequ\source\repos\ArquitecturaBase\docs\plans\2026-11-06-traspaso-multitenant-arca-conexion.md.
Cuenta la conexión con ARCA que allá se decidió y se programó del 3/11 al 5/11: conectar con el certificado de la
empresa, probar la conexión, traer los datos de la empresa del padrón y elegir el punto de venta, sin emitir. Es un
módulo opcional con una carpeta por familia de servicios, y la clave privada y el ticket de acceso van cifrados. Toca tu
decisión 76 y el módulo Integrations de «Después del bloque». No depende de los traspasos 4 y 5. Los commits todavía no
están en GitHub: leelos en C:\Users\ezequ\source\repos\ArquitecturaBase y ...\ArquitecturaBaseFront.

1. Evaluá el traspaso contra tu hoja de ruta (decisión 76 y «Después del bloque»), tu modelo de organizaciones y
   empresas, tu ficha de identificación fiscal, tus módulos habilitados y tus reglas de secretos. No es código para
   copiar (tu decisión 18) ni hace falta para dibujar (tu decisión 108).
2. Sumá ARCA a tu hoja de ruta como la primera integración, con las etapas de la hoja de ruta de ARCA de allá y las
   decisiones del §2 que adoptes. No armes todavía el plan de una etapa.
3. El §5 dice qué evaluar tema por tema. Los tres puntos del final (de quién es la conexión, ARCA como módulo propio o
   dentro de Integrations, y las zonas plegables del alta) los decide el usuario: preguntáselos de a una pregunta, con
   las opciones y la recomendada marcada. Lo mismo para cualquier otra adaptación que tenga que decidir él.
4. Sumá la lista de «no repetir» del §4 a donde vayan las lecciones de tus integraciones.
5. Decile al usuario qué tableros habría que dibujar. Como ejemplo para adaptar, tiene AC1-A, AC2-A y AC3 en el lienzo de
   ArquitecturaBase (https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), filas «ARCA · conexión».
6. No programes nada. Commits chicos en español, en main, sin push.

Al terminar, contame qué cambió en tus specs, qué decisiones quedaron pendientes y qué tableros hay que dibujar.
```

Este traspaso se retira cuando el multitenant lo haya incorporado a su hoja de ruta.
