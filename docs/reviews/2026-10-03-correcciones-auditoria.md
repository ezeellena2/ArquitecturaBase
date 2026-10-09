# Correcciones de la auditoría del 2026-10-03

Alcance: seis observaciones sobre el backend y el front. Las probabilidades siguientes son condicionales: describen cuándo se reproduce el problema, sin estimar porcentajes de producción porque no hay telemetría de incidencia. La revisión incluye el trabajo local que ya existía al comenzar; no atribuye todos esos cambios a estas correcciones.

## 1. Reintentos sin límite ante excepciones inesperadas

**Qué ocurría:** el límite de retries cubría errores de red conocidos, pero una excepción inesperada de una consulta podía seguir reintentándose mientras la consulta permaneciera activa. Por ejemplo, un `SyntaxError` al interpretar una respuesta mantenía la pantalla cargando y repetía el trabajo.

**Probabilidad:** depende de recibir una respuesta ilegible o de un bug del cliente; sin datos no se puede cuantificar su frecuencia. Ante una excepción persistente de ese tipo, el defecto se reproduce de forma determinista.

**Recorrido corregido:** abrir Usuarios → ejecutar consulta → fallar por red o excepción inesperada → reintentar hasta dos veces → finalizar tras tres intentos como máximo y mostrar recuperación. Los errores HTTP no se reintentan automáticamente; una cancelación deliberada tampoco.

**Evidencia:** `queryClient.test.tsx` cubre el límite para errores inesperados y de red; `httpClient.test.ts` comprueba que una cancelación conserva `AbortError`.

## 2. Una actualización fallida borraba la tabla visible

**Qué ocurría:** había filas válidas en caché, pero un refetch fallido reemplazaba la tabla por un error. Además, el contenido y la notificación podían repetir la misma causa técnica.

**Probabilidad:** cualquier fallo de una actualización después de una carga exitosa disparaba el problema en las tablas afectadas. Su frecuencia depende de la disponibilidad de red y API.

**Recorrido corregido:** cargar Usuarios con Ana → actualizar → recibir un error → conservar Ana y los controles → mostrar una sola notificación con la causa y referencia → recuperar la consulta. Una primera carga fallida, sin datos conocidos, muestra un estado genérico de recuperación. Una respuesta previa vacía también cuenta como datos conocidos.

**Evidencia:** pruebas de `UsersPage`, `RolesPage` y `DataTable`, con fallo inicial, refetch fallido y caché vacía.

## 3. Mensajes con políticas y canales diferentes

**Qué ocurría:** las features llamaban directamente a Sonner o resolvían errores operativos dentro de los formularios. Un mismo hecho podía tener distinta duración, duplicarse o perderse si se cerraba el editor mientras terminaba la petición.

**Probabilidad:** en un flujo que usaba esas rutas de error, la diferencia era reproducible; la frecuencia de errores de guardado depende de cada operación. El caso de cierre durante una petición requiere esa coincidencia temporal.

**Recorrido corregido:** editar un usuario → conservar la validación junto al campo → enviar → mantener el borrador ante un error operativo → emitir un único aviso mediante `notify`. Si el modal se cierra antes de recibir el error, el aviso sigue siendo visible. Dentro de un modal abierto, la notificación admite foco y cierre con teclado o mouse. Un resultado incierto al canjear un enlace exige consultar su estado antes de habilitar otro canje.

**Evidencia:** regla de arquitectura que restringe los imports de Sonner, pruebas de los emisores y de las features, y navegador a 320 y 1440 px con borrador conservado y cierre accesible.

**Límite de alcance:** se cerraron los desvíos del emisor y canal comunes. El [plan del front](../../../ArquitecturaBaseFront/docs/plans/2026-09-30-unificacion-mensajes.md) conserva la revisión más amplia de recuperaciones específicas; no se declara terminado todo su inventario futuro.

## 4. Pruebas de navegador fuera del CI

**Qué ocurría:** una regresión de layout, foco o interacción podía pasar compilación, lint y pruebas unitarias, y llegar al artefacto desplegable porque el pipeline no ejecutaba Playwright.

**Probabilidad:** no todo cambio introduce una regresión; si una regresión sólo era detectable en navegador, el pipeline anterior carecía de esa comprobación.

**Recorrido corregido:** ejecutar CI → instalar Chromium en Windows → ejecutar las pruebas visuales y funcionales → registrar el SHA del front probado → comprobar que el build usa ese mismo SHA. Un fallo bloquea el build dependiente y guarda diagnósticos. Cambiar `main` del front entre ambas fases también bloquea la publicación de un artefacto con código diferente al probado.

**Evidencia:** `.github/workflows/deploy.yml`; ejecución local de las 82 pruebas de navegador. No se ejecutó un pipeline remoto ni un despliegue en Azure.

## 5. Claves legibles y envíos pendientes sólo en memoria

**Qué ocurría:** persistir el key ring de Data Protection en la base no cifraba por sí solo sus claves. Además, un correo o mensaje de acceso encolado únicamente en memoria se perdía si el proceso terminaba antes de enviarlo; también podía salir antes de que se confirmara el caso de uso.

**Probabilidad:** las claves eran legibles en una copia de la base con ese formato. Aprovecharlas exige acceso a esa copia y a los datos protegidos relevantes; no equivale a una explotación automática. Un reinicio dentro de la ventana entre enqueue y envío perdía los pendientes en memoria de forma determinista.

**Recorrido corregido:** solicitar un código → agregar estado de negocio y payload cifrado en la misma transacción → rollback elimina ambos, commit conserva ambos → reiniciar antes de reservar → otro consumidor obtiene el pendiente → confirmar una reserva exclusiva → enviar fuera de la transacción → eliminar al completar. Dos hosts no reservan la misma fila. Los vencidos se eliminan incluso si su módulo está apagado.

Las claves se protegen con DPAPI del usuario en Development Windows o con un PFX externo en los otros ambientes productivos. El seed vuelve a cifrar las claves antiguas sin cambiar su identidad; una cookie previa sigue siendo descifrable. Los respaldos anteriores en texto claro no se corrigen retroactivamente.

**Límites:** una caída después de reservar puede dejar incierto el resultado externo; la fila no se reenvía automáticamente y caduca. No se promete entrega exactamente una vez. La migración está generada y probada, pero no se aplicó a la base del usuario. El PFX y sus secretos de despliegue deben provisionarse fuera del repositorio antes de desplegar.

**Evidencia:** pruebas de integración de cola transaccional, rollback, capacidad, cifrado, dos consumidores y retención; pruebas de claves en reposo y compatibilidad de cookies; [ADR 0015](../decisions/0015-colas-transaccionales-cifradas.md), [ADR 0016](../decisions/0016-proteccion-de-claves-data-protection.md).

## 6. Trabajo local sin puntos de recuperación en Git

**Qué ocurría:** había cientos de cambios locales acumulados, incluidos archivos nuevos, eliminaciones y desarrollo anterior a esta auditoría. El último commit no representaba el estado que se estaba probando.

**Probabilidad:** esa falta de registro era una condición ya existente. La pérdida requiere un incidente adicional, como borrar archivos, restaurar el checkout o perder el disco; no corresponde asignarle un porcentaje sin evidencia.

**Recorrido preparado:** verificar ambos repositorios → revisar archivos y secretos evidentes → conservar el trabajo previo → registrar commits locales en español por área → comprobar `main` y los cambios todavía abiertos. Los commits proporcionan historia y comparación local; un respaldo remoto sigue requiriendo push, que no se ejecuta sin un pedido explícito.

**Estado del cierre:** pendiente de delimitar el desarrollo concurrente. Durante la verificación final, el chat «Revisar plan de configuración LLM» empezó a implementar Información del negocio en los mismos repositorios. No se hicieron commits que incorporen ese trabajo en curso ni se descartó ningún archivo. Hay un plan local de agrupación para completar el registro cuando se estabilice o se acuerde su exclusión.

## Validación

- Backend, estado de las seis correcciones anterior al nuevo desarrollo concurrente: `dotnet test`, 2.109 correctos, cero fallos y cero omitidos; integración con PostgreSQL y Redis reales en Docker.
- Build posterior: `dotnet build ArquitecturaBase.slnx --artifacts-path .test-artifacts/build-review-isolated`, cero advertencias y cero errores. La salida aislada evita los DLL bloqueados por la API abierta en Visual Studio. Este build no acredita los tests de Información del negocio todavía en desarrollo.
- En esa salida aislada: cinco pruebas de cola transaccional y dos de claves en reposo correctas, con Docker. No se repitió la suite completa del desarrollo concurrente.
- Front: 875 pruebas en 82 archivos; lint y 39 comprobaciones de arquitectura; build correcto.
- Navegador: 82 pruebas Playwright; se conservaron las capturas aprobadas. La comprobación adicional de cierre con mouse a 320 px también pasó.
- EF: sin cambios pendientes del modelo después de generar la migración de entregas durables.
- Arnés documental: 37 pruebas. Controles estructurales de documentación y `git diff --check` sin errores.
- No se inició Aspire, no se hizo push y no se desplegó en Azure.

Los logs de esta ejecución permanecen como artefactos locales ignorados en `.test-artifacts/` de cada repositorio. Las pruebas acreditan los recorridos cubiertos; no aportan métricas de incidencia de producción.
