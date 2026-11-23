# Traspaso 18 · 04 · La forma de trabajo con varias sesiones a la vez

El 22/11 trabajaron a la vez la prueba integral y una decena de sesiones: las cuatro tandas de «Reglas a Configuración», la
pieza única, la respuesta como formulario y el orden de las tablas. Salieron tres cosas que valen para cualquier proyecto
que trabaje así.

## 1. El carril: lo pesado, de a uno

Con cada sesión compilando y corriendo suites por su cuenta, la PC quedaba al 100 %: los tests vencían por tiempo, se
repetían y no avanzaba nadie. El usuario eligió «un carril para lo pesado» (back `6e1f0d11` y `0d464ae5`, front `6be93e65`).

- **Qué pasa por el carril:** compilar la solución, `dotnet test` de un proyecto entero o de la solución (la integración con
  Testcontainers, siempre), el build y los tests enteros del front, la corrida visual, los arneses y las bisecciones.
- **Qué va libre:** una clase de tests sobre lo ya compilado, un archivo de tests del front, `tsc`, el linter, leer, editar y
  git.
- **Cómo funciona:** un mutex con nombre de Windows, compartido por todas las sesiones del usuario, y un archivo que dice quién
  lo tiene. Mientras espera, avisa quién lo tiene y desde cuándo. Si el dueño muere, Windows libera el turno.
- **Lo que enseñó el primer día:** un `npx` que esperaba la confirmación para instalar un paquete trabó el carril unas 15
  horas. Desde `0d464ae5` el comando corre sin entrada, y el carril lo corta si queda quieto 30 minutos o pasa de 4 horas.
- La guía está en `harnesses/carril/README.md`.

## 2. Tandas en paralelo, con una sesión que coordina

Las reglas que pasaron a Configuración ([01](01-reglas-en-configuracion.md)) se programaron en cuatro sesiones, una por
módulo (WhatsApp, Formularios, núcleo con IA y ARCA), porque varias decisiones compartían pantalla dentro de cada módulo:

- **Cada tanda** trabaja en copias, muestra su tablero al usuario antes de programar y pide el visto bueno de la sesión que
  coordina antes de reconstruir la app o de escribir en el front real (que recarga la página del usuario).
- **La que coordina** fija el orden de entrada y frena a las demás mientras una escribe. El usuario puede cambiar el plan: el
  22/11 pidió «aplicarlas antes» del cierre de la prueba, y las tandas entraron de a una.
- **Las migraciones se regeneran encima de la anterior,** en el orden en que entran. Una tanda dejó en el snapshot de EF el
  bloque de una entidad sin la entidad ni su migración, y otra migración generada en ese rato salía con un `DropTable`. El test
  que compara el modelo con el snapshot lo detectó antes de reconstruir, y ahí importa: con EF 10, aplicar las migraciones al
  arrancar habría frenado la app.
- **Quitar una clave de `appsettings` y reconstruir van juntos.** Dos tandas sacaban claves que pasaban a la base (el nombre
  del remitente, los países de WhatsApp): con el código viejo, la app no habría arrancado al reiniciarse.
- **El campo nuevo de un contrato y su lector en el front entran juntos** ([03](03-prueba-integral.md) §4).

## 3. Por cortes, en horas

El orden de las tablas mostró la forma de «un cambio en horas, no en un día»: la regla común y la primera lista en un corte
(`366bedb7`), y las diez listas restantes en cinco commits el mismo día, cada uno con sus tests y entrando a `main` apenas
andaba. Lo visual, las capturas y la corrida completa van al cierre.

## 4. Para el multitenant

- **Ya tenés parte:** tu skill de la puerta corre sola la suite del front y el E2E, no usa la copia de trabajo «porque ahí
  trabajan otros chats», y tu guía de etapa en paralelo trabaja con dos agentes. Lo que falta es el turno general entre
  sesiones, fuera de la puerta.
- **Si trabajás con varias sesiones a la vez,** un turno para lo pesado evita los timeouts falsos. El carril es una solución
  local de esta PC con Windows: lo que vale es la regla (una corrida pesada por vez, sin entrada interactiva y con corte por
  quieto), no el script.
- **Tus migraciones** tienen el mismo riesgo si dos desarrollos las generan en paralelo: regenerá encima de la última que
  entró y corré el test que compara el modelo con el snapshot antes de desplegar.
- **Con tus esquemas estrictos en el front,** un campo nuevo del backend rompe la pantalla si llega antes que su lector.
  Desplegá los dos juntos.
- Nada de esto cambia tus specs de producto: va a tu forma de trabajo (tus `AGENTS.md` o tu guía de desarrollo), si el usuario
  lo quiere.
