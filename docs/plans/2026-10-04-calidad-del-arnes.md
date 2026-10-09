# Hallazgos de calidad del arnés — 2026-10-04

Estado: diagnóstico pendiente. Estos hallazgos pertenecen a la verificación del front y no
acreditan por sí solos bugs funcionales del producto. La
[rutina permanente](../guides/validar-un-cambio.md) distingue reglas de trabajo y pendientes.

## Evidencia y alcance

En la revisión inicial del código local, backend pasó build con cero advertencias y las 2175
pruebas, incluida integración PostgreSQL/Redis con Docker; front pasó build, lint, 940 pruebas
Vitest y 116 casos Playwright. La suite de navegador usa datos/HTTP simulados y no acredita
proveedores externos ni persistencia real. Son comprobaciones del árbol local, no del despliegue.

La suite Vitest emitió avisos de `act(...)`, falta de `HydrateFallback` y requests sin handler
MSW en pruebas de navegación. La suite Playwright emitió avisos de hidratación y conexiones
rechazadas del proxy a `/connect/authorize` durante acceso público. Ambos resultados fueron
verdes; el verde no demuestra aislamiento ni ausencia de avisos.

Playwright llegó al último caso y mantuvo su Vite de 5178 activo sin emitir resumen final.
Se verificó el proceso exacto iniciado por esa ejecución y se cerró únicamente ese Vite;
entonces el runner devolvió `116 passed (8.1m)` y exit code 0. No se confirmó si el cierre
pendiente provino de Playwright, del proceso servidor o de las restricciones del entorno
Windows. No atribuirlo a código productivo ni declararlo corregido por haber cerrado el proceso.

## Pendientes y criterio de cierre

| Pendiente | Investigación y cierre |
| --- | --- |
| Avisos de lint en código vendor del laboratorio `design-lab/home-assistant` | La verificación posterior emitió `react/only-export-components` en archivos de biblioteca de ese laboratorio existente. Revisar su ámbito y política como código de referencia; conservar las reglas del código productivo. Cerrar con tratamiento explícito del laboratorio y lint sin esos avisos inesperados. No modificar esta biblioteca ajena al cambio de imports para obtener verde. |
| Avisos de React y requests MSW sin handler en Vitest | Reproducir con los archivos afectados y con la suite completa; revisar ciclo de vida de providers/router, esperas y handlers del escenario. Corregir la causa conservando callbacks y `onUnhandledRequest: error`. Cerrar cuando los casos y la suite terminen sin esos avisos inesperados. |
| Intentos de OIDC fuera del arnés visual | Comprobar qué recorrido dispara el iframe y qué respuesta ficticia debe observar la prueba. Mantener la recuperación real del producto y simular el contrato en el arnés. Cerrar con escenarios públicos y suite sin conexiones externas inesperadas. |
| Cierre automático de Vite en Windows | Repetir y separar resultados de tests de cierre del servidor, con procesos propios identificados. Comprobar salida exitosa sin intervención, liberación de 5178 y ausencia de procesos propios remanentes. No cerrar sesiones de desarrollo ajenas. |

No ocultar consolas, quitar strictness de MSW, desactivar recuperación OIDC productiva ni
aumentar tolerancias/actualizar capturas para borrar la evidencia. El alcance de cada reparación
se define después de reproducir su causa; su esfuerzo todavía no está estimado.

## Desvío estructural corregido en esta revisión

Usuarios y Roles importaban `ForbiddenPage` desde otra feature, contra la frontera vigente.
Se trasladó la misma presentación a `shared/ui`, con sus cuatro consumidores (incluida la ruta
`/sin-permiso`). No se cambian permisos, navegación ni textos. El nuevo control
`featureDependencies.test.ts` falló primero con exactamente esos tres cruces y cubre tanto
casos permitidos como imports/reexports indebidos por alias y rutas relativas, incluidos tipos
e imports dinámicos literales. Este caso está corregido; los pendientes del arnés siguen abiertos.

La acumulación de cambios locales es una condición de organización, no un bug. Se revisa y
consolida por funcionalidad bajo la regla existente de commits chicos, conservando cambios
anteriores y de otras sesiones. No implica autorización para agrupar todo en un commit ni hacer push.

## Verificación de la corrección estructural

El control nuevo pasó sus nueve casos después del movimiento; las regresiones de guards,
Usuarios y Roles pasaron 90 casos. Build del front y la suite completa pasaron, con 949 tests
en 88 archivos; lint terminó con exit code 0 y 48 controles de arquitectura aprobados, conservando
los avisos del laboratorio registrados arriba. Los avisos de React/MSW se conservan como pendientes.

Backend volvió a pasar build con cero advertencias. No se modificaron sus fuentes de código,
tests ni configuración desde la ejecución previa de 2175 tests; esa evidencia se conserva y
no se atribuye una nueva ejecución de integración a este cambio documental. `git diff --check`
pasó en ambos repositorios. No hubo cambios visuales: se trasladó la presentación existente;
no se actualizaron capturas ni se repitió Playwright para acreditar una reparación del arnés.
