-- =====================================================================================
-- Formularios por WhatsApp · base de demostración · 4/4 CONSULTAS GUIADAS
--
-- Abrí este archivo en DBeaver conectado a formularios_demo. Poné el cursor dentro de una
-- consulta y apretá Ctrl+Enter para correr solo esa. Van de lo simple a lo profundo.
-- Las que cambian datos están dentro de BEGIN ... ROLLBACK: muestran el efecto y lo deshacen.
-- =====================================================================================

SET search_path = app, vistas, demo, public;


-- 1. ¿Qué formularios hay y en qué estado están? (el listado «Diseñar formularios»)
SELECT * FROM vistas.formularios ORDER BY formulario;


-- 2. ¿Cómo cambió «Registrar visita» entre versiones?
--    Mirá id_pregunta: es el mismo en las tres. En la 2, la fecha pasa a obligatoria; en la 3 aparece el importe.
SELECT version, orden, pregunta, tipo, obligatoria, id_pregunta
FROM vistas.preguntas
WHERE formulario = 'Registrar visita'
ORDER BY version, orden;


-- 3. La planilla de visitas, como se vería en la web.
--    importe_cobrado vacío no es cero: la pregunta no existía cuando llegó esa respuesta.
SELECT * FROM vistas.planilla_registrar_visita ORDER BY numero DESC;


-- 4. Así quedó guardada UNA respuesta, tal cual llegó (el documento completo).
SELECT numero, version, estado, jsonb_pretty(valores_json) AS valores_json
FROM vistas.respuestas
WHERE numero = 'VIS-000024';


-- 5. La misma respuesta, pero en la tabla de valores (una fila por pregunta, con el dato en su columna).
SELECT f."Key" AS pregunta, v."TextValue", v."DecimalValue", v."DateValue", v."BooleanValue", v."ChoiceId"
FROM app."BusinessFormRecordValues" v
JOIN app."BusinessFormFields" f ON f."Id" = v."FieldId"
JOIN vistas.respuestas r ON r.id_respuesta = v."RecordId"
WHERE r.numero = 'VIS-000024';


-- 6. Filtrar por número: mantenimientos de compresor con costo de 50.000 o más.
--    Así filtra la planilla por dentro: sobre las columnas tipadas de Valores, con índice, sin leer el JSON.
--    Resultado esperado: MNT-000004, MNT-000007 y MNT-000010.
SELECT vistas.numero(d."NumberPrefix", r."Number") AS numero
FROM app."BusinessFormRecords" r
JOIN app."BusinessFormDefinitions" d ON d."Id" = r."DefinitionId" AND d."Key" = 'maintenance'
WHERE EXISTS (SELECT 1 FROM app."BusinessFormRecordValues" v JOIN app."BusinessFormFields" f ON f."Id" = v."FieldId"
              WHERE v."RecordId" = r."Id" AND f."Key" = 'cost' AND v."DecimalValue" >= 50000)
  AND EXISTS (SELECT 1 FROM app."BusinessFormRecordValues" v JOIN app."BusinessFormFields" f ON f."Id" = v."FieldId"
              WHERE v."RecordId" = r."Id" AND f."Key" = 'equipment'
                AND vistas.opcion(r."VersionId", v."ChoiceId") = 'Compresor')
ORDER BY r."Number";


-- 7. Filtrar por opción y fecha: visitas a Pérez Hnos. en octubre.
SELECT numero, fecha_visita, observaciones, estado
FROM vistas.planilla_registrar_visita
WHERE cliente = 'Pérez Hnos.' AND fecha_visita BETWEEN '2026-10-01' AND '2026-10-31'
ORDER BY numero;


-- 8. ¿Qué tiene que hacer el motor ahora? Lo que no está «Hecha».
--    Hay una pendiente, una en curso, una esperando a la anterior, una sin confirmar y una que falló.
SELECT numero, evento, regla, orden, accion, estado, intentos, error, creada
FROM vistas.trabajos
WHERE estado <> 'Hecha'
ORDER BY creada;


-- 9. ¿Qué tiene para aprobar el grupo Supervisor?
SELECT numero, tarea, para, creada
FROM vistas.tareas
WHERE estado = 'Abierta'
ORDER BY creada;


-- 10. Todo lo que le pasó a una respuesta, en orden. Probá con VIS-000024, VIS-000021, VIS-000005 o MNT-000010.
SELECT cuando, que_paso
FROM vistas.historial
WHERE numero = 'VIS-000021'
ORDER BY cuando, paso;


-- 11. ¿Qué regla disparó cada acción? Cruza la ejecución con la definición de la regla en su revisión.
--     VIS-000024 usó la revisión 2 de las reglas, que suma r3 (importe mayor a 100.000).
SELECT t.numero, t.revision_de_reglas, t.regla, t.evento, t.accion, rule -> 'if' AS condicion, t.estado
FROM vistas.trabajos t
JOIN app."BusinessFormActionRuns" ar ON ar."Id" = t.id_ejecucion
JOIN app."BusinessFormRuleSets" rs ON rs."Id" = ar."RuleSetId"
CROSS JOIN LATERAL jsonb_array_elements(rs."RulesJson" -> 'rules') rule
WHERE rule ->> 'id' = ar."RuleId" AND t.numero = 'VIS-000024'
ORDER BY t.regla, t.orden;


-- 12. Las reglas vigentes de un formulario, legibles.
SELECT rule ->> 'id' AS regla, rule ->> 'when' AS cuando, rule -> 'if' AS si, a.ordinal AS orden, a.accion ->> 'action' AS hacer, a.accion -> 'config' AS configuracion
FROM app."BusinessFormDefinitions" d
JOIN app."BusinessFormRuleSets" rs ON rs."Id" = d."ActiveRuleSetId"
CROSS JOIN LATERAL jsonb_array_elements(rs."RulesJson" -> 'rules') rule
CROSS JOIN LATERAL jsonb_array_elements(rule -> 'do') WITH ORDINALITY AS a (accion, ordinal)
WHERE d."Key" = 'visit'
ORDER BY regla, orden;


-- 13. ¿Qué cambió entre la versión 2 y la 3? Compara pregunta por pregunta usando su identidad.
WITH v2 AS (SELECT * FROM vistas.preguntas WHERE formulario = 'Registrar visita' AND version = 2),
     v3 AS (SELECT * FROM vistas.preguntas WHERE formulario = 'Registrar visita' AND version = 3)
SELECT coalesce(v3.pregunta, v2.pregunta) AS pregunta,
       CASE WHEN v2.id_pregunta IS NULL THEN 'nueva'
            WHEN v3.id_pregunta IS NULL THEN 'quitada'
            WHEN v2.pregunta <> v3.pregunta OR v2.obligatoria <> v3.obligatoria THEN 'cambiada'
            ELSE 'igual' END AS cambio
FROM v2 FULL JOIN v3 ON v3.id_pregunta = v2.id_pregunta
ORDER BY coalesce(v3.orden, v2.orden);


-- 14. Catálogos: los pedidos con su cliente, y cuántas veces se eligió cada cliente.
SELECT * FROM vistas.planilla_pedido_de_materiales ORDER BY numero;
SELECT * FROM vistas.catalogo_clientes ORDER BY numero;


-- 15. WhatsApp: formularios mandados que no terminaron en respuesta.
SELECT formulario, empleado, lo_mando, estado, enviada, vence
FROM vistas.sesiones_whatsapp
WHERE estado <> 'Respondida';


-- 16. Lo que salió por WhatsApp el 8 de octubre. «regla de un formulario» = lo pidió una ejecución.
SELECT pedido, para, origen, tipo, texto, estado
FROM vistas.cola_de_salida
WHERE pedido::date = '2026-10-08'
ORDER BY pedido;


-- 17. Valores es un índice derivado: se borra y se vuelve a armar desde el JSON, y queda igual.
SELECT count(*) AS valores_antes FROM app."BusinessFormRecordValues";
SELECT demo.reconstruir_valores() AS valores_reconstruidos;
SELECT count(*) AS valores_despues FROM app."BusinessFormRecordValues";


-- 18. La base se defiende sola. Corré cada intento con Ctrl+Enter: los tres fallan con un error que explica por qué,
--     y no se guarda nada.
--     a) La misma entrega de WhatsApp no se puede guardar dos veces.
INSERT INTO app."BusinessFormRecords" ("Id", "DefinitionId", "VersionId", "Number", "Status", "ValuesJson", "RespondentUserId", "SourceKind", "SourceId", "SubmittedAtUtc")
SELECT uuidv7(), r."DefinitionId", r."VersionId", 99, 'Received', r."ValuesJson", r."RespondentUserId", r."SourceKind", r."SourceId", now()
FROM app."BusinessFormRecords" r JOIN vistas.respuestas x ON x.id_respuesta = r."Id" WHERE x.numero = 'VIS-000024';

--     b) Un valor no puede tener dos datos a la vez (texto y número).
INSERT INTO app."BusinessFormRecordValues" ("RecordId", "DefinitionId", "FieldId", "Ordinal", "TextValue", "DecimalValue")
SELECT v."RecordId", v."DefinitionId", v."FieldId", 5, 'hola', 1 FROM app."BusinessFormRecordValues" v LIMIT 1;

--     c) Una respuesta no puede apuntar a una versión de OTRO formulario.
UPDATE app."BusinessFormRecords" r SET "VersionId" = (SELECT v."Id" FROM app."BusinessFormVersions" v JOIN app."BusinessFormDefinitions" d ON d."Id" = v."DefinitionId" WHERE d."Key" = 'maintenance')
WHERE r."Id" = (SELECT id_respuesta FROM vistas.respuestas WHERE numero = 'VIS-000001');


-- 19. Simulá que Carla aprueba VIS-000023, como lo haría el sistema en UNA transacción:
--     cierra la tarea, cambia el estado de la respuesta y crea las ejecuciones de «al aprobarse».
--     Seleccioná desde BEGIN hasta ROLLBACK y apretá Alt+X (ejecutar script). Termina en ROLLBACK: mirá los
--     resultados y no queda nada guardado. Cambiá ROLLBACK por COMMIT si querés conservarlo.
BEGIN;

UPDATE app."BusinessFormTasks" t
SET "Status" = 'Approved', "DecidedBy" = (SELECT "Id" FROM app."AspNetUsers" WHERE "Email" = 'carla@example.com'),
    "DecidedAtUtc" = now(), "Revision" = t."Revision" + 1
WHERE t."RecordId" = (SELECT id_respuesta FROM vistas.respuestas WHERE numero = 'VIS-000023') AND t."Status" = 'Open';

UPDATE app."BusinessFormRecords" r SET "Status" = 'Approved', "Revision" = r."Revision" + 1
WHERE r."Id" = (SELECT id_respuesta FROM vistas.respuestas WHERE numero = 'VIS-000023') AND r."Status" = 'PendingApproval';

INSERT INTO app."BusinessFormActionRuns"
  ("Id", "RecordId", "RuleSetId", "RuleId", "ActionIndex", "Trigger", "ActionKey", "ConfigJson", "IdempotencyKey", "Status", "Attempt", "NextAttemptAtUtc", "CreatedAtUtc")
SELECT uuidv7(), r."Id", rs."Id", rule ->> 'id', (a.ordinal - 1)::int, 'Approved', a.accion ->> 'action', a.accion -> 'config',
       r."Id" || ':Approved:' || (rule ->> 'id') || ':' || (a.ordinal - 1),
       CASE WHEN a.ordinal = 1 THEN 'Pending' ELSE 'Waiting' END, 0, now(), now()
FROM app."BusinessFormRecords" r
JOIN app."BusinessFormDefinitions" d ON d."Id" = r."DefinitionId"
JOIN app."BusinessFormRuleSets" rs ON rs."Id" = d."ActiveRuleSetId"
CROSS JOIN LATERAL jsonb_array_elements(rs."RulesJson" -> 'rules') rule
CROSS JOIN LATERAL jsonb_array_elements(rule -> 'do') WITH ORDINALITY AS a (accion, ordinal)
WHERE r."Id" = (SELECT id_respuesta FROM vistas.respuestas WHERE numero = 'VIS-000023')
  AND rule ->> 'when' = 'Approved';

SELECT numero, estado FROM vistas.respuestas WHERE numero = 'VIS-000023';
SELECT numero, evento, regla, orden, accion, estado FROM vistas.trabajos WHERE numero = 'VIS-000023' ORDER BY creada, orden;

ROLLBACK;
