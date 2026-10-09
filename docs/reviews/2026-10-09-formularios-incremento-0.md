# Formularios: revisión del incremento 0

Fecha: 2026-10-09. Entrega para revisión del coordinador; contrato PROPUESTA y sólo modelo puro implementado. No cierra etapa 1 ni autoriza consumidores. Trabajo directo en main, sin ramas, commits o push; árbol previamente modificado conservado.

## Alcance y decisiones

[Contrato propuesto](../plans/formularios-etapa-1-contrato.md): crear/listar/obtener/guardar, formato 1/seis tipos, identidad/revisión y diferencias con el relevamiento del frontend. Inventario futuro de publicación/envío/captura es referencia del plan; no es implementación ni ampliación del primer contrato. Prefijo ya dibujado en e1-1, bloqueo/errores pendientes. Permisos de tres segmentos chocan con guía/test de dos: fuentes y alternativas quedan documentadas, sin cambios al catálogo/pruebas. Explorar compartir un Flow físico no aprueba pool ni cardinalidad 1:1 irreversible.

Código nuevo: cuatro clases/enum en Domain/Modules/BusinessForms y FormDraftModelTests. Recursos centrales: sólo dos claves Forms.Draft nuevas en cada idioma. AGENTS/CLAUDE del módulo, mapa y premisas enlazan únicamente el bloque implementado; [fuente funcional](../features/formularios.md) delimita lo que existe. No se editaron frontend, demo ni planes de coordinación.

## TDD y verificación automática

Comando del modelo:

```powershell
dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj -- --filter-class ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms.FormDraftModelTests
```

1. Scaffolding inicial no compiló por CA1720 (Decimal) y CA1822 (ChangeType sin instancia). Se corrigieron sólo esos obstáculos: Number representa decimal y el stub todavía recreaba la identidad. Este fallo no se cuenta como rojo de comportamiento.
2. Sandbox bloqueó NamedPipeClient de Microsoft Testing Platform, sin ejecutar pruebas. Repetición autorizada fuera del sandbox, mismo arnés.
3. Rojo de comportamiento: 12 casos, 8 correctos y 4 fallidos, exit 2. Fallaron A_stale_revision_is_rejected_without_advancing_the_draft; Reserved_or_unknown_types_cannot_create_an_active_question para Group y 999; Keeping_the_type_preserves_identity_and_changing_it_creates_another. El stub avanzaba siempre, aceptaba tipos reservados y recreaba identidad del mismo tipo.
4. Implementación: comparación de revisión, whitelist de seis tipos y retorno de la instancia para mismo tipo. Verde: 12/12 correctos, 0 omitidos, exit 0.
5. Build restringido terminó exit 1 sin diagnóstico (0 advertencias/0 errores); no se informa como exitoso. Repetición fuera del sandbox: dotnet build ArquitecturaBase.slnx correcto, 0 advertencias, 0 errores, 20,37 segundos.
6. Docker accesible fuera del sandbox, Server.Version 28.5.1. Suite completa: EN CURSO. Domain/Application ya pasaron; Architecture falló en Program_composes_every_module_with_a_registration_per_layer: BusinessForms: Program.cs does not name the module. Integración todavía en curso; reemplazar por resultado agregado real antes de entrega final.

## Revisión de arquitectura y comportamiento

Domain sigue BCL + Result de su capa; no accede a EF/HTTP/WhatsApp. Valores inmutables, sin Entity que obligue una configuración/migración no autorizada. Sin DI en Domain; no se crean capas/puertos vacíos. Errores declarados en Domain, recursos es/en. Tests observan revisión vieja/vigente, tipos habilitados/reservados/desconocidos, UUID v7, conservación, reemplazo y copia de identidad.

Revisión manual: patrón RoleService/UoW, módulos opcionales, PagedRequest/PagedResult y chat/cola/webhook/linker reales. El contrato conserva cursores y recuperación existente, separa cola/aceptación de entrega, rechaza raw tokens/valores como fallback y evita joins entre módulos. Una suite verde no demuestra calidad del futuro compiler ni publicación/captura.

Diff: revisión acotada a archivos propios y dos claves añadidas en recursos; no atribuir cambios previos del árbol. Whitespace/enlaces: git diff --check acotado sin errores; Git avisó LF→CRLF por su configuración vigente. Todos los enlaces relativos de los cinco documentos nuevos resuelven; sin trailing whitespace. Se revisaron las cuatro piezas de Domain, test y las dos claves nuevas por idioma. Los 72 renglones agregados por recurso frente a HEAD incluyen trabajo previo; sólo 6 corresponden a este incremento.

## Pendientes explícitos

Revisión del coordinador de contrato y bloque antes de siguiente incremento. Resolver permisos, límites/cultura, recuperación de alta/mapa incierto, capability/ausencia, destino de Guardar, ayuda/opciones, variantes elegidas y decisiones listadas en contrato. No hay ChoiceId/mapa persistido, ownership, JSON parser, entidades, UoW, endpoints, EF, workers ni integración chat implementados. Prueba manual de formularios/Meta ausente; no se llamó a Meta, no se publicó ni envió nada. No se levantó Aspire; no corresponde aspire stop. Los cuatro pendientes documentales ajenos del front permanecen fuera de alcance.

### Hallazgo de composición

ModuleBoundaryTests.ModuleProblems (líneas 231–256) no exige registro DI a Domain, pero sí exige que Program.cs nombre todo módulo descubierto, incluso uno sólo de valores puros. El bloque nuevo dispara esa regla. No se agregó un using/comentario nominal para hacer pasar el detector ni registros vacíos, no se alteró la prueba y no se adelantó la implementación de Application. El incremento queda pendiente de resolver esta composición bajo revisión del coordinador; no se presenta cerrado ni con suite verde. Opciones a revisar: composición útil al implementar la capa siguiente, o ajuste explícito de la regla para módulos únicamente Domain si se aprueba con la evidencia/decisión correspondientes.
