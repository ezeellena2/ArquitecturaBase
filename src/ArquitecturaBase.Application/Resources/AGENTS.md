# Resources: textos del backend

Los `.resx` de errores, validación y permisos tienen español e inglés. Los lectores de recursos resuelven textos según el idioma de la petición.

## Al modificar

- Cada clave existe en ambos idiomas. En español usá voseo; identificadores, logs y descripciones técnicas quedan en inglés.
- La clave de un error es su `*Code` estable declarado en Domain. No traducir por la descripción inglesa ni renombrar códigos al corregir un texto.
- Reutilizá mensajes generales de validación. Conservá placeholders y el mismo significado en los dos idiomas.
- Los textos propios de un módulo opcional viven en los recursos del módulo.

## Referencias

- [Paridad de recursos](../../../tests/ArquitecturaBase.Application.UnitTests/Resources/ResourceParityTests.cs).
- [Traducción de códigos](../../../tests/ArquitecturaBase.Application.UnitTests/Resources/ErrorCodeTranslationTests.cs).
- [Errores y localización](../../../docs/architecture/backend.md).
