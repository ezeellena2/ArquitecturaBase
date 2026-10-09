# Validación del chat con Playwright — 2026-10-02

Revisión posterior a la [separación de acceso y atención](2026-10-02-separacion-acceso-whatsapp-validacion.md),
solicitada para comprobar el chat antes de continuar su desarrollo. Trabajo local en `main`
de ambos repositorios; se conservaron los cambios previos y concurrentes, sin commits ni push.

## Resultado

| Comprobación | Resultado |
| --- | --- |
| Front: `node node_modules/@playwright/test/cli.js test` | 72 correctos, 0 fallidos en la corrida final. |
| Front: `npm run test` | 830 correctos en 78 archivos. |
| Front: `npm run lint` | Correcto; 38 pruebas de arquitectura. |
| Front: `npm run build` | Correcto. |
| Backend: `dotnet test --artifacts-path .test-artifacts/browser-validation` | 2057 correctos, 0 fallidos, 0 omitidos. |
| Backend: `dotnet build ArquitecturaBase.slnx --artifacts-path .test-artifacts/browser-validation --no-restore` | 0 advertencias, 0 errores. |
| Archivos modificados en esta revisión: `git diff --check` | Sin errores de espacios. Los archivos nuevos también se revisaron con `--no-index`. |

Playwright usó Chromium y la configuración habitual con Vite en 5178. El servidor propio
se apagó al terminar las pruebas; en Windows fue necesario detenerlo para completar el
cierre del runner. No se levantó Aspire ni se cerraron instancias ajenas. Los scripts npm
se ejecutaron mediante su CLI de Node por el fallo del wrapper local.

## Flujos comprobados

La nueva suite del front `src/test/visual/whatsapp-flow.spec.ts` usa la entrada `session.html`,
el router, el proveedor OIDC y los componentes reales, con respuestas HTTP simuladas.

- Enter agrega una línea; Ctrl+Enter guarda una única operación con `requestId`.
- El envío pendiente bloquea nuevas respuestas y la actualización automática muestra
  Delivered cuando el doble HTTP informa ese estado.
- Si se pierde la respuesta del POST después de guardar, se conserva el borrador y se
  consulta la operación desde la notificación, sin repetir el envío.
- Un operador con lectura solamente abre el historial y tiene el editor deshabilitado.
  Sin permiso de lectura, la ruta no solicita conversaciones.
- Cerrar sesión e ingresar mediante código WhatsApp completa el callback OIDC; no envía
  una respuesta de atención ni cambia el modo Manual de la conversación.

La suite existente también comprueba eventos Access sin cuerpo secreto, atención Manual,
es/en, tamaños de escritorio y celular, borradores, paginado y posición de lectura.
Se revisó visualmente la captura generada `test-results/visual/whatsapp-flow-delivered.png`.
No se actualizaron capturas de referencia para hacer pasar pruebas.

## Corrección encontrada

La acción «Consultar envío» de la notificación conservaba un callback de un render
anterior en el que la mutación todavía estaba pendiente. Su condición impedía iniciar
la consulta, aunque el POST ya había terminado con respuesta incierta.

Se agregó primero un test unitario que falló con cero consultas, además de reproducirlo
en Playwright. `ChatComposer` ahora usa referencias al estado actual de envío y consulta
para decidir si puede consultar, y evita consultas simultáneas. El contrato HTTP, el
requestId y la política de no reenviar automáticamente se conservan. El test unitario
y la prueba de navegador pasan después de la corrección.

Una primera corrida completa tuvo 71 correctos y un fallo en la nueva prueba de entrega:
con el reloj controlado, esa prueba pulsaba «Actualizar» esperando que refrescara el
historial, cuando el botón consulta el listado. Se corrigió la prueba para avanzar el
reloj y comprobar el polling del historial. La corrida final completa dio 72 correctos.

## Alcance y pendientes

Los tests de integración del backend usaron PostgreSQL 18.3 y Redis 8.6 reales mediante
Docker, con Meta simulado. El navegador no se conectó a la API de la instalación ni
se enviaron mensajes a un teléfono real. Los resultados no acreditan entrega real,
configuración externa de Meta ni despliegue.

El arnés existente emite avisos de HydrateFallback, variables de color y conexión
rechazada del proxy para renovación silenciosa en algunas pruebas de la portada,
que no levantan el backend. Las compilaciones no tuvieron advertencias.

La comprobación real de mensaje entrante, respuesta manual, código de acceso e invitación
sigue pendiente según la [guía local](../guides/whatsapp-en-local.md) y
[verificación y producción](../plans/verificacion-y-produccion.md).
La IA continúa como siguiente diseño: sugerencias revisadas y enviadas por una persona;
esta revisión no agrega proveedor, credenciales ni respuestas automáticas.
