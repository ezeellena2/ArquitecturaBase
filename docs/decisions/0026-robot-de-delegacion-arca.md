# ADR 0026: robot de delegación sobre la página de ARCA y la clave fiscal en memoria

## Estado

**Propuesta**, 2026-11-09. Recoge las decisiones 2 y 3 de la [hoja de ruta de ARCA](../plans/2026-10-31-arca-formularios-estandar.md)
y el [plan de la etapa 6](../plans/2026-11-09-arca-etapa-6-robot.md). Pasa a aceptada con el diseño de la etapa. Extiende el
[ADR 0023](0023-arca-como-modulo-opcional.md), que pidió este ADR antes de programar el robot.

## Contexto

En el modo 3 de acceso nadie entra a mano a ARCA: un robot delega los web services del cliente en la CUIT de la plataforma,
da de alta el punto de venta, acepta la delegación del lado de la plataforma y la asigna a su computador fiscal. ARCA no
publica una API para el Administrador de Relaciones, así que el robot maneja la página web con un navegador. Esa página:

- existe sólo en producción y cambia sin aviso;
- puede pedir un captcha o una validación extra;
- bloquea la clave fiscal después de varios ingresos rechazados.

La clave fiscal es personal e intransferible para ARCA y permite actuar en nombre de la persona en todos sus trámites. El
usuario decidió que el robot sea propio, con Playwright para .NET, y que la clave del cliente nunca salga del servidor.

## Decisión

1. **Un worker del módulo ARCA dentro de la Api,** con Playwright para .NET y Chromium. No hay un host aparte ni un
   intermediario. La Application ve un puerto en `Interfaces/Integrations/Connection`; Playwright queda en Infrastructure.
2. **El lado del cliente corre en memoria, en el host que recibió el pedido.** La clave llega en el pedido HTTP y pasa al
   worker por un canal en memoria: nunca viaja por la red interna, por la cola cifrada del
   [ADR 0015](0015-colas-transaccionales-cifradas.md) ni por la base. El avance se guarda sin secretos, con una reserva que
   vence: si el host se cae, la corrida queda interrumpida y el cliente vuelve a poner la clave o sigue con la guía.
3. **El lado de la plataforma es durable.** La aceptación pendiente se guarda en la base y la toma cualquier host con una
   reserva, como el ticket de acceso. La clave de la plataforma es un secreto de la instalación (user-secrets, Key Vault o
   variables protegidas), nunca de la base. Un ingreso rechazado no se reintenta: frena las aceptaciones y avisa.
4. **La clave del cliente:**
   - es un tipo propio que no se imprime ni se serializa, desde el contrato HTTP hasta el robot;
   - se usa sólo para llenar el campo de ingreso y se suelta cuando ARCA acepta el ingreso;
   - Playwright corre sin trazas, HAR, video ni capturas, y sin sus variables de depuración;
   - las excepciones de Playwright se traducen a un motivo estable sin su mensaje, que puede repetir lo escrito;
   - la ruta queda fuera de cualquier registro del cuerpo del pedido, y un error nunca devuelve lo enviado;
   - cada corrida usa un contexto de navegador nuevo, sin perfil persistente, que se cierra siempre, con un plazo total;
   - una prueba corre el robot con una clave conocida y la busca en los logs, las excepciones, la base y la cola.
5. **Cada paso mira si ya está hecho antes de actuar,** así repetir una corrida no duplica nada en ARCA. El trámite se da
   por terminado con la detección de la etapa 5 (la CUIT en `relations` del ticket de acceso), no con lo que muestra la
   pantalla.
6. **Una falla es un motivo estable:** ingreso rechazado, captcha, validación extra, pantalla cambiada (con el paso), ARCA
   no responde o interrumpida. El robot no resuelve captchas ni pide un segundo factor: el cliente sigue con la guía de la
   etapa 5 en el paso donde quedó. Una pantalla cambiada avisa al dueño de la plataforma.
7. **Cada familia aporta sus pasos del lado del cliente,** como aporta su prueba con `IArcaServiceDefinition`
   ([ADR 0025](0025-familias-de-servicios-arca.md)): Facturación da de alta el punto de venta. `Connection` no la nombra.
8. **Apagado por defecto** (`Arca:Robot:Enabled`), hasta que la revisión legal lo habilite. Con el robot prendido y sin
   Chromium, la Api no arranca. Las direcciones de las páginas de ARCA son configuración y los selectores viven en un
   objeto por pantalla.

## Consecuencias

- **Límite reconocido:** .NET no garantiza borrar un `string` de la memoria. La promesa es que la clave no se escribe en
  ningún lado y vive lo menos posible. Un volcado de memoria del proceso podría tenerla; los volcados no se habilitan en
  producción, y quien controla el proceso queda fuera de esta protección, como en el [ADR 0014](0014-credenciales-de-ia-fuera-de-la-base.md).
- **Despliegue:** donde el robot se prende, la imagen de la Api necesita una base con Chromium de la versión que pide el
  paquete de Playwright. Actualizar Playwright es regenerar esa base. Donde está apagado, la imagen de siempre alcanza.
- **Mantenimiento:** cuando ARCA cambia una pantalla, se corrigen su objeto de página y su página simulada. Las pruebas
  automáticas van contra páginas simuladas; la corrida real la hace el usuario con sus cuentas.
- **Diagnóstico sin capturas:** el aviso dice el paso y el motivo, nada de la persona. Reproducir una falla es correr el
  robot con una cuenta propia.
- **Multitenant:** una sola CUIT de plataforma para todas las organizaciones. La aceptación pendiente pasa a ser por
  organización, y un bloqueo de la clave de la plataforma frena a todas: ahí vuelve a evaluarse pausar el robot.

## Alternativas descartadas

- **Afip SDK u otro servicio que corra el robot:** recibe la clave fiscal y los certificados en sus servidores
  (descartado en el ADR 0023).
- **Un host aparte para el robot:** la clave viajaría por la red interna, y suma un proyecto, una imagen y un despliegue
  para una corrida por instalación.
- **Guardar la clave cifrada para reintentar o mandarla por la cola cifrada:** queda en la base, y la base con su anillo
  alcanza para recuperarla. Pedirla otra vez cuesta menos que ese riesgo.
- **Que el dueño escriba su clave en cada aceptación:** contradice la delegación de punta a punta.
- **Capturas, trazas o HAR para diagnosticar:** las trazas y el HAR pueden tener la clave; las capturas, datos de la
  persona.
- **Un servicio que resuelva captchas:** suma un tercero y va contra las reglas de ARCA.
- **Selenium o PuppeteerSharp:** el usuario eligió Playwright, que espera solo a los elementos y fija la versión de
  Chromium.
