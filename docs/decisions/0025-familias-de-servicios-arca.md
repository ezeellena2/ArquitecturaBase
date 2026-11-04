# ADR 0025: familias de servicios dentro del módulo ARCA

## Estado

**Aceptada**, 2026-11-04, por decisión del usuario. Rige desde la etapa 1 de ARCA. Complementa el
[ADR 0023](0023-arca-como-modulo-opcional.md), que fija el módulo y sus credenciales.

## Contexto

ARCA publica unos 55 web services. Unos 25 sirven para formularios, agrupados en familias: padrón, facturación, granos,
hacienda, lechería y otras ([hoja de ruta](../plans/2026-10-31-arca-formularios-estandar.md#qué-servicios-de-arca-entran)).
Todos comparten la conexión (certificado, clave, delegación y ticket de acceso por servicio), pero cada familia tiene sus
propias operaciones, datos y pantallas. Cada empresa usa solo algunas: una de granos no necesita hacienda.

Las reglas de arquitectura comparan cada clase quitándole el segmento `Modules.<M>`
([módulos opcionales](../architecture/backend.md#módulos-opcionales)). Por eso un servicio vive en `…/Services/…` y un
controlador en `…/Controllers/…`, y una familia no puede ser una carpeta arriba de las de capa. Además, un módulo no puede
usar a otro.

## Decisión

1. **Un solo módulo ARCA, con una carpeta por familia en cada capa,** como las áreas del núcleo: `Services/Invoicing/`,
   `Models/Registry/`, `Persistence/Repositories/Grains/`, `Contracts/Livestock/`, y en el front `features/arca/<familia>/`.
   `Connection` es la base común: conexión, certificados, tickets, anillo y el sobre SOAP. Las clases `*Errors` de cada
   familia van en la raíz de `Domain/Modules/Arca/`, porque ahí las exige la regla de errores.
2. **Una familia usa solo `Connection`**, nunca a otra familia. `Connection` no nombra a ninguna familia. Lo verifica una
   prueba de arquitectura del módulo.
3. **Cada familia se presenta con `IArcaServiceDefinition`,** una interfaz de `Connection` que la familia implementa y
   registra con `TryAddEnumerable`. Es un puerto de `Interfaces/Channels/Connection`, y el adaptador de cada familia va
   en `Channels/<Familia>`. Declara:
   - sus servicios de ARCA (el código WSN, como `wsfe` o `wscpe`) y su nombre;
   - cómo se prueba cada uno;
   - si su configuración propia está completa (Facturación, por ejemplo, necesita el punto de venta).

   El ticket de acceso se guarda por código WSN, no por una lista cerrada.
4. **Cada empresa elige qué servicios usa.** La conexión guarda los servicios elegidos; solo esos se autorizan, se prueban
   y muestran sus pantallas. La conexión está activa cuando el certificado está vigente, las pruebas de los elegidos salen
   bien y la configuración de cada familia elegida está completa.
5. **Cada familia es dueña de lo suyo:** sus tablas, sus rutas (`api/arca/<familia>/…`), sus errores
   (`Arca.<Familia>.<Motivo>`), su zona en la pantalla de ARCA y, más adelante, sus estándares y catálogos.

## Consecuencias

- **Quitar una familia,** sin tocar las demás, es:
  - borrar sus carpetas en cada capa, en los tests y en el front;
  - sacar su línea de cada registro, su clase de errores y sus claves `Arca.<Familia>.*`;
  - generar la migración que borra sus tablas.

  Quitar el módulo entero sigue la guía de siempre. `quitar-arca.md` cuenta los dos casos.
- **En la etapa 1 hay tres familias:**
  - `Connection`;
  - `Registry`: el padrón y los datos de la empresa;
  - `Invoicing`: `FEDummy` y el punto de venta.

  Granos, hacienda y las demás llegan con sus etapas, sin cambiar la base.
- **Agregar una familia** es crear sus carpetas, implementar `IArcaServiceDefinition` y sumar su línea a cada registro.
- **Las reglas:** `ArcaFamilyBoundaryTests` verifica que una familia use solo `Connection`, con casos de control. Las
  reglas que comparaban el namespace exacto de `Interfaces/Persistence`, `Interfaces/Services` (la de controllers) y
  `Persistence/{Repositories,Readers}`
  revisan también sus subcarpetas, así un contrato o un repositorio de una familia no queda afuera.

## Alternativas descartadas

- **Un módulo opcional por familia** (`Modules/ArcaInvoicing`, `Modules/ArcaGrains`): un módulo no puede usar a otro, así
  que la conexión tendría que ir al núcleo o duplicarse. Permitir módulos que dependen de otro cambia las reglas de todo
  el repo.
- **La familia arriba de las carpetas de capa** (`Modules/Arca/Invoicing/Services`): las reglas de ubicación dejarían de
  reconocer los servicios, controladores y repositorios.
- **Que la instalación use todas las familias que trae:** la prueba marcaría «sin autorizar» los servicios que la empresa
  no habilitó en ARCA, y la conexión quedaría en «requiere atención» sin motivo.
