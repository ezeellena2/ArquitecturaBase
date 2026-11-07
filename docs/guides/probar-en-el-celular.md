# Probar en el celular

Cómo abrir la app local en un celular Android real y recorrerla con la red y el procesador
limitados. Sirve para lo que las pruebas no ven: la cámara, el toque, el teclado del teléfono y
una subida lenta de fotos. Se hace a mano y con su titular, una vez por desarrollo que lo pida
(por ejemplo, la carga desde la app), como parte del cierre de [validar un cambio](validar-un-cambio.md).

## Qué cubren las pruebas y qué no

- `npm run test:visual` cambia el tamaño de la ventana a 390 y 320 px en un Chromium de
  escritorio. No emula toque ni teclado de teléfono, y el HTTP es un doble: no mide la red.
- Los tests del front simulan la cámara y los cortes. Por ejemplo, un envío cortado se repite con
  el mismo pedido y recibe el primer número (`FormEntryPage.test.tsx`).
- La cámara real, el tamaño real de las fotos y una red lenta quedan para esta guía.

## Abrir la app en el celular

El celular entra por USB al mismo `https://localhost:5173` que usa la compu. No hace falta cambiar
la configuración: el origen, el emisor y las direcciones del cliente web siguen siendo los mismos,
y nada queda expuesto en internet.

1. Levantá la app como siempre (F5 en Visual Studio o `aspire run`).
2. En el celular, prendé las opciones de desarrollador y la **depuración por USB**, y conectalo a la
   compu con el cable. Aceptá el aviso de depuración en el teléfono.
3. En Chrome de la compu, abrí `chrome://inspect/#devices`. Marcá **Discover USB devices** hasta que
   aparezca el celular.
4. Tocá **Port forwarding**, agregá el puerto `5173` con la dirección `localhost:5173`, marcá
   **Enable port forwarding** y tocá **Done**.
5. En Chrome del celular, abrí `https://localhost:5173`. El certificado de desarrollo no es de
   confianza en el teléfono: si Chrome avisa, elegí «Configuración avanzada» y seguí a `localhost`.
   Confirmá en la primera prueba que el ingreso funciona después de ese aviso. Si no funciona,
   anotalo en el plan del desarrollo; no cambies el certificado ni la configuración para
   compensarlo.

**El túnel no sirve para esto.** Solo publica el endpoint `https` de la Api para los webhooks de
Meta ([WhatsApp en local](whatsapp-en-local.md)). En desarrollo la Api no tiene el front en
`wwwroot` y devuelve 404 en `/`. Además, el emisor y las direcciones del cliente web son de
`https://localhost:5173`.

Con iPhone no hay reenvío de puertos desde Windows. Esta guía cubre Android.

## Red lenta y procesador de celular

- En `chrome://inspect`, tocá **inspect** debajo de la pestaña del celular. Se abre DevTools sobre
  el teléfono.
- En **Network**, elegí un perfil lento del desplegable de velocidad (por ejemplo «3G»). **Offline**
  corta la red.
- En **Performance**, bajá la CPU (por ejemplo 4× más lenta) para ver la carga de las pantallas.
- Para revisar el diseño sin teléfono alcanza con el modo dispositivo de DevTools en la compu, con
  los mismos límites, pero no tiene cámara.

## Qué recorrer

Con una cuenta que tenga «Completar formularios» y un formulario con foto y una lista de catálogo:

1. Abrí «Cargar». La lista de catálogo se abre a pantalla completa y busca en el servidor.
2. Tocá «Tomar foto». Debe abrir la cámara trasera. Sacá dos o tres fotos reales y mirá el tamaño
   que muestra cada una.
3. Con la red en «3G», tocá «Enviar». El botón dice «Enviando… N %» y avanza hasta «Enviada».
4. Repetí con otra carga y pasá a **Offline** mientras envía. Debe avisar «No se pudo enviar».
   Volvé a la red y tocá «Enviar» otra vez: recibe un solo número, sin duplicar la respuesta.
5. Mirá que el teclado del teléfono no tape el pie con «Enviar» ni el campo que estás escribiendo.

Anotá el resultado real en el plan del desarrollo, en una línea, con el modelo del celular. Una
subida que no termina o tarda demasiado con fotos reales es un hallazgo para el plan, no algo que
se compensa en la prueba.

## Al terminar

Desmarcá **Enable port forwarding** o desconectá el cable. Si levantaste la app con Aspire,
terminá con `aspire stop`.
