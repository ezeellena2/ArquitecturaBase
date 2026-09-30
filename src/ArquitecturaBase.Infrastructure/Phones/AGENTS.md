# Phones: interpretación y presentación

`PhoneNumberParser` adapta entradas por país y número a la representación canónica, formateada y enmascarada.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/identidad.md`](../../../docs/features/identidad.md) (los números de teléfono: el 9 de los celulares argentinos y el enmascarado).

## Al modificar

- La normalización la hace el backend. No duplicar este algoritmo en front ni en servicios de negocio.
- Conservá E.164 para identidad y formato legible para mostrar, con tratamiento de móviles argentinos.
- Validación y logs usan la máscara cuando corresponde; no registrar números completos.

## Verificación y ejemplos

- [Pruebas de formatos y países](../../../tests/ArquitecturaBase.Api.IntegrationTests/Phones).
