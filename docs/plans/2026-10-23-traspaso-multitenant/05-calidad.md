# 05 · Por qué falló la implementación y qué controlar

## El cambio de programador

**Cómo se programó.** De la Etapa 1 a Catálogos E2, cada etapa la programó un chat de Codex a partir de un resumen con decisiones cerradas. Cada etapa se cerraba con la suite completa en verde (miles de tests), contratos exportados, capturas y recorrido en el navegador.

**Qué encontró el review.** Aun así, los code reviews independientes encontraron en cada etapa errores graves que los tests no veían:
- datos guardados mal sin aviso;
- pérdida de datos;
- pantallas bloqueadas;
- contratos rotos entre el back y el front.

**Qué decidió el usuario.** El 2026-10-23 probó la app y concluyó que «Codex programó bastante mal». Desde ese día programa un chat de Claude, el «Codificador». El detalle de cada hallazgo está en el [anexo E](anexo-e-reviews-y-lecciones.md).

**La lección no es el nombre del programador:** una suite verde no alcanza. Hacen falta un review independiente, la prueba del usuario en la app y reglas de diseño que no induzcan errores.

## Patrones de error que se repitieron

1. **Interpretar números sin el idioma.** «1.500» en español se guardó como 1,5:
   - en la importación de E1;
   - otra vez en el cambio de tipo de un campo, en E2.

   Ya estaba en las lecciones del 17/10.
2. **El front reemplaza al servidor.** El editor validaba en el navegador con reglas propias y deshabilitaba Guardar, así que el servidor, que sí sabía convertir y explicar, nunca recibía el pedido (E2, punto 4).
3. **Un error de negocio tratado como conflicto.** Cualquier 409 ponía el editor en «revisión» y obligaba a descartar todo, cuando solo el conflicto de revisión debía hacerlo (E2, punto 2).
4. **Contratos sin ejemplos reales.** El chat de Inicio estuvo roto desde el 09/10 sin que nadie lo notara:
   - el back devolvía `pending` y los tests del back lo fijaban así;
   - los tests del front usaban dobles inventados con `Pending`;
   - no había una cohorte exportada que los uniera.
5. **Validar todo en cada guardado.** Revalidar los valores guardados contra el estado actual de otra entidad hizo que dar de baja una opción de «Zonas» bloqueara para siempre la edición de «Proveedores» (E2, punto 1).
6. **Reescrituras masivas.** Cada guardado reescribía las 10.000 opciones y su auditoría, aunque no hubiera cambios (E2, punto 10).
7. **Cambios de configuración que rompen sin aviso.** Cambiar el papel o el tipo de un campo que usa un filtro, o el tipo de un dato de las personas con valores cargados, se aceptaba y dejaba los envíos fallando (E2, puntos 7 y 8).
8. **Columnas o traducciones vacías.** En E1, una columna opcional ausente al importar borraba datos. En E2, una traducción vacía `""` dejaba el catálogo sin poder abrirse.
9. **Permisos que filtran.** «En uso» mostraba nombres de formularios a quien no podía leerlos (E2, punto 11).
10. **Interfaz con explicaciones.** Textos debajo de los botones, «Cambios sin guardar», números de revisión y errores en rojo al agregar una fila. Venía de la guía visual (ver [04](04-reglas-del-usuario.md)).
11. **Migraciones con CRLF.** Un filtro de índice en varias líneas dejaba `\r\n` en el snapshot, y en Linux EF iba a ver cambios pendientes. Se agregó `MigrationSqlPortabilityTests`.
12. **Idioma fijo.** Se exigía español en campos, opciones y datos de las personas, y los dos idiomas en catálogos, contra la regla de idioma original.

## Lista de control para cerrar una etapa

Antes de dar una etapa por cerrada, además de la suite completa:

- [ ] **Code review independiente,** con agentes de solo lectura que no programaron la etapa. Cada hallazgo verificado en el código, con su escenario.
- [ ] **La persona usa la app** como la va a usar el cliente: crear, guardar, equivocarse, volver y cambiar de idioma. No alcanza con las escenas visuales.
- [ ] **Todo contrato HTTP que consume el front tiene ejemplos exportados** por el back y un consumidor en el front que los valida con zod estricto.
- [ ] **Números, fechas y decimales con la cultura del pedido.** Lo ambiguo se rechaza, con un test con «1.500» y «1,500».
- [ ] **El front no tapa la validación del servidor:** valida lo editado y deja que el servidor responda lo que solo él sabe.
- [ ] **Un 409 de revisión y un 409 de regla se tratan distinto.**
- [ ] **Cambiar la configuración de algo en uso** se frena con el uso, o pide confirmación y limpia. Nunca deja datos huérfanos.
- [ ] **Guardar sin cambios no escribe.** Un test comprueba que no cambia la auditoría.
- [ ] **Nada exige un idioma fijo.** Se aplica la regla del idioma original.
- [ ] **Interfaz según el [04](04-reglas-del-usuario.md):** sin textos al lado de botones, sin estados de guardado, acciones a la derecha y validación al guardar.
- [ ] **Migraciones portables:** SQL en una línea y sin `\r`.
- [ ] **Permisos de lectura:** nada que nombre otro objeto sin el permiso de leerlo.
