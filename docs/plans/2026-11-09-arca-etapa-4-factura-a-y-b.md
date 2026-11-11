# ARCA · Etapa 4: factura A y B

Diseño del 2026-11-09, decidido el mismo día (decisiones abajo). **Backend y front programados** el 2026-11-09 y el
2026-11-11; faltan la prueba integral, la prueba en homologación y el cierre (tareas 8 y 9). Es la etapa 4 de la
[hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md): la factura A y B y sus notas de crédito, con el IVA por
alícuota, la condición frente al IVA del receptor, el apartado de la RG 5614 y la prueba en homologación con la CUIT del
usuario, que reemplaza al piloto en producción (decisión 3).

Extiende lo que deja la [etapa 3](2026-11-09-arca-etapa-3-factura-c.md) y no lo duplica: la cola de emisión con su
numeración y reconciliación ([ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md)), el cliente de
`FECAESolicitar`, `ArcaVoucherClassPolicy` con la clase y los códigos como datos, la tabla de condiciones por clase, el
autocompletar del padrón, el PDF fiscal con QR, los catálogos de ARCA (las alícuotas incluidas), el puerto con formularios,
el estándar «Factura» y la anulación con nota de crédito. El reparto quedó en la nota compartida de las etapas.

**Tableros:** IV1-A e IV1-B, IV2-A e IV2-B e IV3, en el lienzo provisorio
[Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), en las filas «ARCA · factura A y B»,
debajo de las de la etapa 3. Sólo dibujan lo que cambia respecto de la C en la app y en WhatsApp; el usuario eligió IV1-A
e IV2-A (decisiones 4 y 5). El PDF se revisó con muestras reales (tarea 5). El diseño elegido está en el front, en
`docs/design/arca-factura-a-y-b.md`.

## Normas confirmadas (2026-11-09)

Fuentes oficiales leídas el 2026-11-09: el [manual de WSFEv1 v4.7](https://www.arca.gob.ar/ws/documentacion/manuales/manual-desarrollador-ARCA-COMPG.pdf)
de producción (revisión del 1/9/2026), el [manual v4.8 de homologación](https://www.arca.gob.ar/fe/ayuda/documentos/wsfev1-RG-4291.pdf)
(revisión del 1/12/2026), el Boletín Oficial y [argentina.gob.ar/normativa](https://www.argentina.gob.ar/normativa).

- **Condición frente al IVA del receptor ([RG 5616](https://www.boletinoficial.gob.ar/pdf/aviso/primera/318374/20250624)).**
  La norma la exige desde el 15/4/2025. Hoy, si falta, ARCA observa con el código 10245 y no rechaza. El manual v4.8 dice,
  con fecha 01/12/2026: «Será obligatorio el campo Condición Frente al IVA del receptor», y deja en desuso la 10245.
  - Ya hoy se rechaza un valor inexistente (10242) o uno que no corresponde a la clase (10243). El 10246 es el rechazo por
    falta.
  - La norma no excluye las notas de crédito, y el campo va en todo comprobante: les aplica (inferencia).
  - Fuera del manual no hay un comunicado de ARCA con la fecha; la prensa (iProfesional, 9/9/2026) da la misma.
- **Condiciones que admite cada clase** (anexo del manual):

  | Código | Condición | A | B | C |
  | --- | --- | --- | --- | --- |
  | 1 | IVA responsable inscripto | Sí | | Sí |
  | 4 | IVA sujeto exento | | Sí | Sí |
  | 5 | Consumidor final | | Sí | Sí |
  | 6 | Responsable monotributo | Sí | | Sí |
  | 7 | Sujeto no categorizado | | Sí | Sí |
  | 8 y 9 | Proveedor y cliente del exterior | | Sí | Sí |
  | 10 | IVA liberado (Ley 19.640) | | Sí | Sí |
  | 13 | Monotributista social | Sí | | Sí |
  | 15 | IVA no alcanzado | | Sí | Sí |
  | 16 | Monotributo trabajador independiente promovido | Sí | | Sí |

- **Códigos:** factura A 1 y nota de crédito A 3; factura B 6 y nota de crédito B 8. La A exige `DocTipo` 80, CUIT (10013).
- **El array `Iva`, también en la B:**
  - Una entrada por alícuota (10022), con `Id`, `BaseImp` e `Importe`. El manual confirma 3 = 0 %, 4 = 10,5 %, 5 = 21 % y
    6 = 27 %; los demás (5 % y 2,5 %) salen de `FEParamGetTiposIva`, que la etapa 3 trae como catálogo.
  - Con `ImpNeto` mayor que 0 el array es obligatorio (10070). Un neto al 0 % va con el `Id` 3 e importe 0.
  - La suma de `BaseImp` es `ImpNeto` (10061) y la suma de `Importe` es `ImpIVA` (10023). Tolerancia: 0,01 por alícuota.
  - Cada `Importe` corresponde a su `BaseImp` por la alícuota, con un error de 0,01 como mucho (10051).
  - Lo exento va en `ImpOpEx` y lo no gravado en `ImpTotConc`, fuera del array.
  - `ImpTotal` es la suma de los cinco importes (10048). Ninguno es negativo. Redondeo half-even.
- **Notas de crédito A y B.** Asocian sólo comprobantes de su clase (10040). En `CbtesAsoc` son obligatorios el tipo, el
  punto de venta y el número; la CUIT y la fecha, no (fuera de la FCE). Si la nota supera el importe de la factura, ARCA sólo
  observa (10237). Las notas no pasan por los rechazos del padrón del receptor (10247 y 10248).
- **Receptor sin identificar.** Desde $10.000.000 hay que identificarlo ([RG 5866/2026](https://www.argentina.gob.ar/normativa/nacional/norma-427092/texto),
  vigente desde el 1/7/2026; antes, la RG 5700/2025). Por debajo va `DocTipo` 99 con `DocNro` 0 (10015). Vale igual para la
  C, que la etapa 3 ya resuelve con el rechazo de ARCA.
- **Apartado de la [RG 5614](https://www.argentina.gob.ar/normativa/nacional/norma-407183/texto)** (Régimen de Transparencia
  Fiscal al Consumidor, Ley 27.743), obligatorio desde el 1/4/2025.
  - Lo lleva el comprobante de un responsable inscripto a exentos, no alcanzados y consumidores finales.
  - Va en el espacio inferior izquierdo, con el título «Régimen de Transparencia Fiscal al Consumidor (Ley 27.743)». Debajo,
    «IVA Contenido» y «Otros Impuestos Nacionales Indirectos», «cada uno con su correspondiente valor».
  - WSFEv1 no tiene un campo para esto: es sólo del PDF.
  - En «Otros» irían los impuestos internos. Sin tributos, se muestra 0 (interpretación de «cada uno con su valor»).
  - La norma no nombra las notas de crédito B: se les aplica igual (inferencia).
  - La C no discrimina IVA ([Anexo II de la RG 1415](https://biblioteca.afip.gob.ar/pdfp/ANEXO_II_RG1415_V4444.pdf)) y no lo
    lleva.
- **La A a un monotributista** lleva la leyenda del art. 20 de la [RG 5003/2021](https://www.argentina.gob.ar/normativa/nacional/resoluci%C3%B3n-5003-2021-350574/actualizacion):
  «El crédito fiscal discriminado en el presente comprobante, sólo podrá ser computado a efectos del Régimen de
  Sostenimiento e Inclusión Fiscal para Pequeños Contribuyentes de la Ley Nº 27.618». El manual la nombra en la observación
  10217.
- **La A con leyenda** ([RG 5762/2025](https://www.argentina.gob.ar/normativa/nacional/resoluci%C3%B3n-5762-2025-417981/texto),
  vigente desde el 1/12/2025) reemplazó a la M: tipos 51 a 54, clase ALEY. ARCA decide por los datos del emisor; si no lo
  autoriza a emitir A, rechaza con el 10000 (mensaje 04, o el 09 para la A con leyenda). Queda fuera de esta etapa.
- **Fechas:** las ventanas de `CbteFch` no cambian para A y B (±5 días para productos y ±10 para servicios).
- **Padrón en la A:** una CUIT inactiva (10247), no confiable (10248) o apócrifa (10195) rechaza. Un receptor sin IVA ni
  monotributo activo sólo observa (10063). Falta confirmar si ARCA cruza la condición informada con la del padrón.

## Alcance

- **El tipo lo decide el sistema:** con un emisor responsable inscripto, A si el receptor es 1, 6, 13 o 16, y B si no. La
  persona no lo elige. Un emisor monotributista o exento sigue con la C de la etapa 3.
- **IVA por alícuota** en cada renglón y en los totales, y el array `Iva` en el pedido.
- **La condición frente al IVA del receptor** informada siempre y controlada contra la clase antes de encolar.
- **Nota de crédito A y B** por el total, que referencia la factura en `CbtesAsoc`, con la misma anulación de la etapa 3.
- **PDF:** IVA discriminado en la A, la leyenda de la RG 5003 en la A a un monotributista, y precios con IVA y el apartado de
  la RG 5614 en la B.
- **Prueba en homologación** con la CUIT responsable inscripta del usuario, que la corre con su clave fiscal. Reemplaza al
  piloto en producción de la hoja de ruta (decisión 3).

## Diseño

### Tipo de comprobante

- Las filas A y B de `ArcaVoucherClassPolicy`, que en la etapa 3 devuelven `Arca.Invoicing.ClassNotSupported`: emisor
  responsable inscripto con receptor 1, 6, 13 o 16 → A; con 4, 5, 7, 8, 9, 10 o 15 → B. La tabla sale del catálogo de
  `FEParamGetCondicionIvaReceptor`, como en la etapa 3.
- **La A pide CUIT:** un cliente sin CUIT no puede ser 1, 6, 13 ni 16. La validación de la etapa 3 (condición de la clase)
  lo cubre.
- **Consumidor final** (con DNI o sin identificar) es la condición 5: B.

### IVA en los renglones y en los totales

- Cada renglón tiene su alícuota, elegida del catálogo de alícuotas de ARCA más «Exento» y «No gravado» (decisión 2),
  con 21 % por defecto.
- **El cálculo**, puro y en el servidor (el front sólo muestra la vista previa de los cálculos de la etapa 2):
  - importe del renglón = cantidad × precio unitario, redondeado a 2 decimales con half-even;
  - por alícuota, la base es la suma de los renglones y el IVA, la base por la alícuota, redondeado igual;
  - si el formulario dice «Los precios incluyen IVA» (decisión 1), por alícuota la base es la suma dividida por (1 + alícuota), redondeada,
    y el IVA es la suma menos la base. El error contra base × alícuota es como mucho 0,005 × 1,27, dentro de la tolerancia
    de 0,01 de la 10051;
  - `ImpNeto` es la suma de las bases, `ImpIVA` la de los IVA y `ImpTotal` la de todo, con `ImpTrib` en 0.
- Va en un archivo propio de `Domain/Modules/Arca/Invoicing/`, que la etapa 3 no tiene. La C no lo necesita: su `ImpNeto`
  es el subtotal.

### El pedido a ARCA

Sobre el cliente de `FECAESolicitar` de la etapa 3: el `CbteTipo` de la clase, `DocTipo` 80 en la A y el array `Iva` en la
A y la B. La condición del receptor ya va desde la etapa 3. Los rechazos nuevos (10243, 10051, 10061, 10023, 10070 y el
10000 con el mensaje 04) se traducen a un motivo estable, como los demás.

### Nota de crédito A y B

La anulación de la etapa 3 sin cambios de recorrido: la nota de crédito es de la clase de la factura (3 para la A, 8 para la
B), por el total, con los mismos renglones, alícuotas, importes y receptor, y la factura en `CbtesAsoc`. La parcial sigue
para después.

### PDF fiscal

Sobre el diseño fijo de la etapa 3:

- **A:** la letra A con su código; los renglones con precio unitario e importe sin IVA y la alícuota; al pie, el neto
  gravado, el IVA de cada alícuota, lo exento y lo no gravado si hay, y el total. Con receptor 6, 13 o 16, la leyenda de la
  RG 5003.
- **B:** la letra B con su código; precios e importes con IVA incluido; el total. Con receptor 4, 5 o 15, el apartado de la
  RG 5614 abajo a la izquierda, con el IVA contenido (`ImpIVA`) y los otros impuestos indirectos en 0.
- La nota de crédito A o B sigue las mismas reglas que su factura.

### App y WhatsApp

- **App:** la tabla de renglones suma la columna «Alícuota» y el pie muestra el neto, el IVA por alícuota y el total. El
  «¿La emito?» dice «Factura A» o «Factura B» y el IVA.
- **WhatsApp:** la pantalla de cada renglón suma la alícuota, y el resumen, el IVA.
- **Tableros IV**, sólo con lo que cambia: la alícuota en el renglón (app y WhatsApp), los totales y el «¿La emito?» con
  IVA, el PDF de la A y de la B, y la nota de crédito A y B si difiere de la C.

### Plazo de la RG 5616

La etapa 3 informa la condición del receptor desde la primera emisión, y nada se emite antes de ella. Además, la
homologación ya rechaza sin la condición (manual v4.8), así que sus pruebas lo cubren. El riesgo es otro: si la etapa 3 no
llega a producción antes del 1/12/2026, no hay emisión, no una emisión rechazada. Si cambia el plan de la etapa 3, se avisa
al usuario.

## Decisiones del usuario (2026-11-09)

Se preguntaron de a una, con la recomendada primero.

1. **El precio de cada renglón.** El usuario eligió (2026-11-09) que **lo elija cada formulario**: la copia del estándar
   «Factura» tiene «Los precios incluyen IVA», y el servidor separa o suma el IVA. Descartadas: siempre sin IVA y siempre
   con IVA.
2. **Exento y no gravado.** El usuario eligió (2026-11-09) que **sean opciones de la alícuota del renglón**, junto a las de
   ARCA: una factura puede mezclar renglones gravados y exentos. Van a `ImpOpEx` e `ImpTotConc` y tienen su renglón en los
   totales del PDF. Descartada: sólo alícuotas gravadas por ahora.
3. **El piloto.** El usuario lo corre con **su CUIT responsable inscripta** (2026-11-09). Ante qué emitir en producción,
   contestó que no hace falta: **todas las pruebas van en homologación**, que admite las que hagan falta. El piloto en
   producción de la hoja de ruta sale de la etapa. La prueba: una factura A (a responsable inscripto y a monotributista,
   con la leyenda) y una B a consumidor final, con alícuotas distintas y un renglón exento, cada una anulada con su nota de
   crédito. Lo que homologación no muestra (los datos reales del padrón y si ARCA autoriza al emisor a la clase A o le pide
   la A con leyenda) se ve en la primera factura real, fuera de la etapa.
4. **Dónde se ve el IVA al cargar (IV1).** El usuario eligió (2026-11-09) **IV1-A**: la tabla de ítems de la C suma sólo la
   columna «Alícuota» (una lista que viene en 21 %, con las de ARCA más «Exento» y «No gravado»), y el IVA aparece una vez,
   en el total: neto gravado, el IVA de cada alícuota, lo exento y el total. La (i) de «Precio unitario» dice si es sin o
   con IVA, según el formulario. Descartada: IV1-B, el IVA y el importe con IVA en cada ítem.
5. **Dónde se elige «Los precios incluyen IVA» (IV2).** El usuario eligió (2026-11-09) **IV2-A**: la casilla «El precio
   incluye IVA» en la pregunta «Precio unitario» del bloque de ítems, debajo de «Se carga varias veces», con su (i).
   Descartada: IV2-B, una zona «Factura» del formulario.

**Sin opciones (IV3):** el «¿La emito?» de la app suma el neto, el IVA y lo exento en la A, y el IVA contenido en la B; en
WhatsApp, la pantalla de cada ítem suma «Alícuota» y el resumen de EM2-A, el IVA entre paréntesis.

## Tareas

Del 1 al 7, programadas; el 8 y el 9 van después de la prueba integral, que se hace una sola vez al final de la tanda.

1. **Plan, normas, decisiones y tableros IV.** Hechos el 2026-11-09.
2. **El desglose del IVA por alícuota** con TDD, en Domain, según las decisiones 1 y 2. **Hecho el 2026-11-09**
   (33b631f3): `ArcaVatBreakdown.Calculate` recibe los importes de los renglones ya redondeados (los calcula la etapa 3) con
   su `ArcaVatTreatment` (una `ArcaVatRate`, exento o no gravado) y si incluyen IVA, y devuelve los cinco importes y el
   array `Iva` (`ArcaVatRateTotal`). Un importe negativo o con más de 2 decimales es un error del sistema: la carga los
   rechaza antes. 21 pruebas, entre ellas las tolerancias de la 10051 para todos los importes de 0,01 a 50 con cada
   alícuota.
3. **Las filas A y B** de la clase y el pedido con `Iva` y `DocTipo` 80. **Hecho el 2026-11-09** (38ebe5dc), sobre la
   cola de la etapa 3: `ArcaVoucherOrderItem` y `ArcaVoucherLine` llevan su `ArcaVoucherVat` (código de alícuota, exento o no
   gravado), el pedido dice si los precios incluyen IVA, `ArcaVatRates` fija el porcentaje de cada código y
   `ArcaVoucherOrderPolicy.ForClass` pide CUIT en la A y el IVA de cada renglón en la A y la B. Se retiró
   `Arca.Invoicing.ClassNotSupported`. La vista previa devuelve también el IVA. Las pruebas de integración (una A con su
   nota de crédito y una B a consumidor final contra `FakeArca`) compilan y corren en la verificación de cierre.
4. **Nota de crédito A y B.** Sale de la anulación de la etapa 3, que ya copia la clase y los renglones de la factura; la
   cubre la prueba de la A (tarea 3).
5. **PDF:** IVA discriminado, la leyenda de la RG 5003 y el apartado de la RG 5614. **Hecho el 2026-11-09** (890f04bf). En
   lugar de un tablero, se revisaron dos PDF de muestra dibujados por el renderer real (una A a monotributista con exento y
   una B a consumidor final); el diseño fijo es el de la etapa 3 y el contenido lo fija la norma.
6. *(con las etapas 2 y 3)* **La alícuota en el estándar «Factura»** y «Los precios incluyen IVA». **El emisor ya los lee**
   (2026-11-09): `ArcaInvoiceOrderPolicy` toma `vatRate` de cada ítem (el código de ARCA como texto, «exempt» o «not-taxed»)
   y `pricesIncludeVat` del formulario («true»), las claves acordadas con la etapa 3. **Hecho el 2026-11-11:** el estándar
   ata «Alícuota» (etapa 3, 2315d5ba) sobre «Alícuotas de IVA» con Exento y No gravado (tarea 8 de la etapa 3); la
   casilla «El precio incluye IVA» de «Precio unitario» y la «Alícuota» que empieza en 21 % son genéricas del motor (el
   estándar declara `toggles` y la opción con que empieza, 8fb5a9a0), y un ítem sin alícuota va al 21 % (bcdeaf9e). La (i)
   de «Precio unitario» en la carga dice «Sin IVA» o «Con IVA» con lo que declara la casilla (`whenOff` y `whenOn`).
7. **Front.** El 2026-11-11 el coordinador de la tanda pidió terminarlo ya (la prueba integral se hace al final, una sola) y
   la etapa 4 tomó también el front de la tarea 14 de la etapa 3 (EM1-A, EM2-A, NC1-A y CA1), porque son las mismas pantallas.
   **Hecho el 2026-11-11**, con el diseño en el front (`docs/design/arca-factura-a-y-b.md`, ba5e2fb0):
   - la acción «Emitir comprobante» en el editor de reglas, con sus límites y los datos de su resultado en «Agregar dato»
     (front c34630f6);
   - el «¿La emito?» de la app con IVA (EM2-A con IV3) y el total discriminado de IV1-A mientras se carga, los dos con la
     vista previa del emisor (front 18e31307; la vista previa usa el cupo de lectura, ae1a6f23);
   - la casilla «El precio incluye IVA» en el editor y la «Alícuota» en 21 % en cada ítem nuevo (front 43397c5d);
   - la zona «Comprobante» del detalle (EM1-A): estado, número, CAE, «PDF fiscal», la revisión de una emisión y «Esperando
     que lo confirmes» con «Emitir» y «No emitir»; «Anular con nota de crédito» y «Anulándose» (NC1-A) (front 0815da57);
   - los catálogos de sólo lectura con «Actualizado por ARCA» y su falla, y «Completar con ARCA» en el modal de una opción de
     «Clientes» (CA1) (front 9da76575);
   - la (i) de «Precio unitario» en la carga, «Sin IVA» o «Con IVA» según la casilla (backend f089f494, front 9fb8a70f).

   - «Mandar copia por correo» (`SendCopy`, backend de la etapa 3 en 81ccfaae) en el editor de reglas y la actividad
     (front e7b7476c);
   - el «¿La emito?» de WhatsApp con los importes que discrimina entre paréntesis, como pide IV3 (backend 7f4b2a97).

   Queda para la prueba integral lo que necesita ARCA real o la app levantada.
8. **Prueba en homologación de A y B** con la CUIT del usuario, que la corre con su clave fiscal.
9. **Cierre:** la verificación de `AGENTS.md`, `docs/features/arca.md`, la fila de la etapa 4 en la hoja de ruta, el retiro
   de este plan con la auditoría y el traspaso al multitenant ofrecido.

## Pruebas

- **Unitarias:** el desglose (una y varias alícuotas, 0 %, exento y no gravado, precios con y sin IVA, bordes del half-even
  y las tolerancias de 10051, 10061 y 10023); la clase A o B para cada condición; el PDF de la A y la B con y sin leyendas.
- **De integración con `FakeArca`:** una A y una B autorizadas con su array `Iva`; los rechazos 10243 y 10000; la nota de
  crédito de cada clase con `CbtesAsoc`.
- **Homologación real:** la prueba de la decisión 3, corrida por el usuario con su CUIT.
- **Sin piloto en producción** (decisión 3).

### Prueba a mano, en la prueba integral

Con la app levantada y la conexión de ARCA en homologación con la CUIT responsable inscripta del usuario
([configurar ARCA](../guides/configurar-arca.md): certificado de WSASS con el pedido que genera la app, `wsfe` y la constancia
asociados, y un punto de venta de homologación). El usuario corre los pasos con su clave fiscal; los datos de prueba se los
indica la sesión, sin cargarlos por él.

1. **La empresa responsable inscripta.** Después de la prueba de la conexión, «Empresa» dice «IVA Responsable Inscripto».
2. **La casilla del precio (IV2-A).** Agregar «Factura» desde la biblioteca. En el editor, la pregunta «Precio unitario» del
   bloque tiene «El precio incluye IVA» debajo de «Se carga varias veces», con su (i). Dejarla sin marcar y publicar.
3. **La factura A (IV1-A).** Cargar desde la app una factura a un cliente responsable inscripto, con tres ítems: uno al 21 %,
   uno al 10,5 % y uno exento. La columna «Alícuota» viene en 21 %, la (i) de «Precio unitario» dice «Sin IVA» y la zona
   «Total» muestra el neto gravado, el IVA de cada alícuota, lo exento y el total.
4. **El «¿La emito?» de la A (IV3).** «Enviar» abre el resumen con «Factura A», el neto, el IVA, lo exento y el total.
   «Emitir factura»: la zona «Comprobante» trae número y CAE.
5. **El PDF de la A.** Cada ítem con su alícuota, el neto gravado, el IVA de cada alícuota, lo exento y el total; el QR abre
   la página de ARCA.
6. **La A a un monotributista.** Otra factura a un cliente monotributista: el PDF lleva la leyenda de la RG 5003.
7. **La B con precios con IVA.** Marcar «El precio incluye IVA», publicar y cargar una factura a consumidor final con un ítem al
   21 % y uno exento: el total es la suma de los precios, el «¿La emito?» dice «Factura B» con el IVA contenido, y el PDF
   muestra los precios con IVA y el recuadro de la RG 5614 abajo a la izquierda.
8. **Anular la A y la B.** «Anular» en cada una: la nota de crédito A y la B salen por el total, con el mismo IVA, y la
   respuesta queda anulada cuando ARCA las autoriza. El PDF de cada nota dice qué factura anula.
9. **La B por WhatsApp.** Cargar una factura por WhatsApp: la pantalla de cada ítem trae «Alícuota» y el resumen que llega
   dice el IVA entre paréntesis. «Emitir».

## Fuera de la etapa

La A con leyenda (51 a 54), la factura de crédito MiPyME, los tributos (percepciones e impuestos internos: `ImpTrib` en 0 y
«Otros impuestos» en 0), otras monedas, la B a clientes del exterior y la exportación, la B al no categorizado con la CUIT
genérica (pide el tributo 13), las notas de débito y la nota de crédito parcial.
