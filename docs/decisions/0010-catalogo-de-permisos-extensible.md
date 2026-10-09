# 0010. Catálogo de permisos extensible

## Estado

Aceptada, 2026-10-02, como parte del desarrollo del chat autorizado por el usuario.

## Contexto

El catálogo estático del núcleo no puede nombrar un módulo opcional. La atención
requiere permisos propios, visibles en Roles, validados al asignarlos y sembrados
en Admin, conservando la posibilidad de quitar el módulo.

## Decisión

`IPermissionCatalog` compone `Permissions.All` y las fuentes de módulos registradas
con `TryAddEnumerable` mediante `IPermissionCatalogSource`. Roles, sus validadores,
el seed y la lectura de permisos efectivos consumen ese catálogo.

## Consecuencias

Las fuentes resuelven textos según la cultura actual; códigos repetidos fallan al
componer. Sin una fuente, sus claims históricos no habilitan operaciones. El seed
incorpora los permisos disponibles a Admin e invalida después del commit como antes.
Cada módulo declara sus códigos y recursos dentro de su carpeta.

## Alternativas descartadas

- Agregar constantes del módulo a `Permissions`: rompe la frontera opcional.
- Autorizar el chat con permisos de usuarios: mezcla responsabilidades.
- Autorizar por nombre de rol: impide delegar lectura y respuesta por separado.
