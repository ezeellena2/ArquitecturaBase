-- =====================================================================================
-- RECORRIDO 1 · El administrador crea y publica «Registrar entrega»
--
-- Cada PASO es lo que el sistema escribiría en la base por una acción del administrador.
-- Cómo usarlo en DBeaver: seleccioná un PASO completo y apretá Alt+X (ejecutar script);
-- después corré sus consultas «MIRÁ» con Ctrl+Enter. Seguí en orden.
-- El PASO 0 borra «Registrar entrega» para poder empezar de nuevo cuando quieras.
-- =====================================================================================

SET search_path = app, vistas, demo, public;


-- =====================================================================================
-- PASO 0 · Empezar de cero (solo para la demo: en el sistema un formulario se borra lógicamente)
-- =====================================================================================
BEGIN;
UPDATE "BusinessFormDefinitions" SET "ActiveVersionId" = NULL, "ActiveRuleSetId" = NULL WHERE "Key" = 'delivery';
DELETE FROM "WhatsAppFlowOperations" WHERE "BindingId" IN (SELECT "Id" FROM "WhatsAppFlowBindings" WHERE "DefinitionId" IN (SELECT "Id" FROM "BusinessFormDefinitions" WHERE "Key" = 'delivery'));
DELETE FROM "WhatsAppFlowBindings" WHERE "DefinitionId" IN (SELECT "Id" FROM "BusinessFormDefinitions" WHERE "Key" = 'delivery');
DELETE FROM "BusinessFormRuleSets" WHERE "DefinitionId" IN (SELECT "Id" FROM "BusinessFormDefinitions" WHERE "Key" = 'delivery');
DELETE FROM "BusinessFormPublicationOperations" WHERE "DefinitionId" IN (SELECT "Id" FROM "BusinessFormDefinitions" WHERE "Key" = 'delivery');
DELETE FROM "BusinessFormVersions" WHERE "DefinitionId" IN (SELECT "Id" FROM "BusinessFormDefinitions" WHERE "Key" = 'delivery');
DELETE FROM "BusinessFormFields" WHERE "DefinitionId" IN (SELECT "Id" FROM "BusinessFormDefinitions" WHERE "Key" = 'delivery');
DELETE FROM "BusinessFormDefinitions" WHERE "Key" = 'delivery';
COMMIT;


-- =====================================================================================
-- PASO 1 · Ana toca «Nuevo formulario» y le pone nombre
-- Toca 1 tabla: BusinessFormDefinitions (+1 fila). El borrador nace vacío.
-- Todavía no hay versión, ni preguntas registradas, ni nada en WhatsApp.
-- =====================================================================================
INSERT INTO "BusinessFormDefinitions"
  ("Id", "Kind", "Key", "Name", "NumberPrefix", "LastRecordNumber", "DraftJson", "DraftRevision", "CreatedAtUtc", "CreatedBy", "IsDeleted")
VALUES
  (uuidv7(), 'Form', 'delivery', 'Registrar entrega', 'ENT', 0,
   '{"format": 1, "title": {"es": "Registrar entrega"}, "submitLabel": {"es": "Confirmar entrega"}, "questions": []}',
   1, now(), (SELECT "Id" FROM "AspNetUsers" WHERE "Email" = 'ana@example.com'), false);

-- MIRÁ 1 · La fila nueva. ActiveVersionId vacío = nunca se publicó.
SELECT "Id", "Kind", "Key", "Name", "NumberPrefix", "DraftRevision", "ActiveVersionId", "LastRecordNumber", jsonb_pretty("DraftJson") AS borrador
FROM "BusinessFormDefinitions" WHERE "Key" = 'delivery';


-- =====================================================================================
-- PASO 2 · Ana agrega las preguntas y toca «Guardar borrador»
-- Toca 1 tabla: BusinessFormDefinitions (se ACTUALIZA el borrador, no se crean filas).
-- El servidor le da a cada pregunta su fieldId y a cada opción su choiceId: esas
-- identidades ya no cambian aunque después cambie el texto.
-- El WHERE con DraftRevision = 1 es el control de concurrencia: si otra persona guardó
-- antes, no actualiza nada y la aplicación responde 409 («alguien cambió el formulario»).
-- =====================================================================================
UPDATE "BusinessFormDefinitions"
SET "DraftJson" = jsonb_build_object(
      'format', 1,
      'title', jsonb_build_object('es', 'Registrar entrega'),
      'submitLabel', jsonb_build_object('es', 'Confirmar entrega'),
      'questions', jsonb_build_array(
        jsonb_build_object('fieldId', uuidv7(), 'key', 'client', 'type', 'choice', 'label', jsonb_build_object('es', 'Cliente'), 'required', true,
          'options', jsonb_build_array(
            jsonb_build_object('choiceId', uuidv7(), 'label', jsonb_build_object('es', 'Pérez Hnos.')),
            jsonb_build_object('choiceId', uuidv7(), 'label', jsonb_build_object('es', 'Ferretería Sur')),
            jsonb_build_object('choiceId', uuidv7(), 'label', jsonb_build_object('es', 'Obra Centro')))),
        jsonb_build_object('fieldId', uuidv7(), 'key', 'delivery_date', 'type', 'date', 'label', jsonb_build_object('es', 'Fecha de entrega'), 'required', true),
        jsonb_build_object('fieldId', uuidv7(), 'key', 'complete', 'type', 'boolean', 'label', jsonb_build_object('es', '¿Se entregó completo?'), 'required', true),
        jsonb_build_object('fieldId', uuidv7(), 'key', 'packages', 'type', 'decimal', 'label', jsonb_build_object('es', 'Bultos entregados'), 'required', true, 'scale', 0),
        jsonb_build_object('fieldId', uuidv7(), 'key', 'comments', 'type', 'longText', 'label', jsonb_build_object('es', 'Observaciones'), 'required', false, 'maxLength', 600)
      )),
    "DraftRevision" = "DraftRevision" + 1,
    "ModifiedAtUtc" = now(),
    "ModifiedBy" = (SELECT "Id" FROM "AspNetUsers" WHERE "Email" = 'ana@example.com')
WHERE "Key" = 'delivery' AND "DraftRevision" = 1;

-- MIRÁ 2a · El borrador ahora tiene cinco preguntas, cada una con su identidad.
SELECT x.orden, q ->> 'key' AS clave, q -> 'label' ->> 'es' AS pregunta, q ->> 'type' AS tipo, q ->> 'required' AS obligatoria, q ->> 'fieldId' AS field_id
FROM "BusinessFormDefinitions" d
CROSS JOIN LATERAL jsonb_array_elements(d."DraftJson" -> 'questions') WITH ORDINALITY AS x (q, orden)
WHERE d."Key" = 'delivery';

-- MIRÁ 2b · Pero todavía no hay ninguna pregunta registrada ni versión: es solo un borrador.
SELECT (SELECT count(*) FROM "BusinessFormFields" f JOIN "BusinessFormDefinitions" d ON d."Id" = f."DefinitionId" WHERE d."Key" = 'delivery') AS preguntas_registradas,
       (SELECT count(*) FROM "BusinessFormVersions" v JOIN "BusinessFormDefinitions" d ON d."Id" = v."DefinitionId" WHERE d."Key" = 'delivery') AS versiones,
       (SELECT "DraftRevision" FROM "BusinessFormDefinitions" WHERE "Key" = 'delivery') AS revision_del_borrador;


-- =====================================================================================
-- PASO 3 · Ana toca «Publicar en WhatsApp» y confirma «Publicar versión 1»
-- UNA transacción en el módulo de formularios. Toca 4 tablas:
--   BusinessFormFields                +5  las preguntas quedan registradas
--   BusinessFormVersions              +1  la foto fija del borrador (versión 1)
--   BusinessFormPublicationOperations +1  el pedido de publicar, para que lo haga un proceso del servidor
--   BusinessFormDefinitions           se vacía el borrador (ya no hay cambios sin publicar)
-- La pantalla recibe 202 «guardado para publicar». TODAVÍA NO ESTÁ EN WHATSAPP.
-- =====================================================================================
BEGIN;

INSERT INTO "BusinessFormFields" ("Id", "DefinitionId", "Key", "DataKind", "CreatedInVersion")
SELECT (q ->> 'fieldId')::uuid, d."Id", q ->> 'key',
       CASE q ->> 'type' WHEN 'shortText' THEN 'Text' WHEN 'longText' THEN 'Text' WHEN 'decimal' THEN 'Decimal'
                         WHEN 'date' THEN 'Date' WHEN 'boolean' THEN 'Boolean' WHEN 'choice' THEN 'Choice' END,
       1
FROM "BusinessFormDefinitions" d
CROSS JOIN LATERAL jsonb_array_elements(d."DraftJson" -> 'questions') q
WHERE d."Key" = 'delivery'
  AND NOT EXISTS (SELECT 1 FROM "BusinessFormFields" f WHERE f."Id" = (q ->> 'fieldId')::uuid);

INSERT INTO "BusinessFormVersions" ("Id", "DefinitionId", "Number", "SchemaFormatVersion", "SchemaJson", "ContentHash", "CreatedAtUtc", "CreatedBy")
SELECT uuidv7(), d."Id", 1, 1, d."DraftJson", encode(sha256(convert_to(d."DraftJson"::text, 'UTF8')), 'hex'), now(),
       (SELECT "Id" FROM "AspNetUsers" WHERE "Email" = 'ana@example.com')
FROM "BusinessFormDefinitions" d WHERE d."Key" = 'delivery';

INSERT INTO "BusinessFormPublicationOperations" ("Id", "DefinitionId", "VersionId", "RequestId", "RequestedBy", "Status", "Stage", "RequestedAtUtc")
SELECT uuidv7(), d."Id", v."Id", uuidv7(), (SELECT "Id" FROM "AspNetUsers" WHERE "Email" = 'ana@example.com'), 'Pending', 'Requested', now()
FROM "BusinessFormDefinitions" d JOIN "BusinessFormVersions" v ON v."DefinitionId" = d."Id" AND v."Number" = 1
WHERE d."Key" = 'delivery';

UPDATE "BusinessFormDefinitions" SET "DraftJson" = NULL, "DraftRevision" = "DraftRevision" + 1, "ModifiedAtUtc" = now()
WHERE "Key" = 'delivery';

COMMIT;

-- MIRÁ 3a · Las cinco preguntas registradas: su Id es el fieldId que tenían en el borrador.
SELECT f."Id", f."Key", f."DataKind", f."CreatedInVersion"
FROM "BusinessFormFields" f JOIN "BusinessFormDefinitions" d ON d."Id" = f."DefinitionId"
WHERE d."Key" = 'delivery';

-- MIRÁ 3b · La versión 1: el JSON completo de lo que se pregunta, congelado.
SELECT v."Number", v."ContentHash", v."CreatedAtUtc", jsonb_pretty(v."SchemaJson") AS esquema
FROM "BusinessFormVersions" v JOIN "BusinessFormDefinitions" d ON d."Id" = v."DefinitionId"
WHERE d."Key" = 'delivery';

-- MIRÁ 3c · El pedido de publicar, pendiente. Y el formulario sigue SIN versión vigente.
SELECT p."Status", p."Stage", p."RequestId", p."RequestedAtUtc", d."ActiveVersionId" AS version_vigente, d."DraftJson" IS NULL AS sin_cambios_pendientes
FROM "BusinessFormPublicationOperations" p JOIN "BusinessFormDefinitions" d ON d."Id" = p."DefinitionId"
WHERE d."Key" = 'delivery';


-- =====================================================================================
-- PASO 4 · Un proceso en segundo plano del servidor toma el pedido (no es una persona: nadie aprueba nada)
-- Toca 1 tabla: BusinessFormPublicationOperations. Reserva el trabajo: sube Fence y
-- guarda su LeaseToken con vencimiento. Si el servidor se reinicia a mitad de camino,
-- otro proceso lo retoma cuando vence la reserva, sin publicar dos veces.
-- =====================================================================================
UPDATE "BusinessFormPublicationOperations" p
SET "Status" = 'Running', "Stage" = 'SendingToChannel', "Fence" = p."Fence" + 1,
    "LeaseToken" = uuidv7(), "LeaseExpiresAtUtc" = now() + interval '2 minutes'
FROM "BusinessFormDefinitions" d
WHERE d."Id" = p."DefinitionId" AND d."Key" = 'delivery' AND p."Status" = 'Pending';

-- MIRÁ 4
SELECT p."Status", p."Stage", p."Fence", p."LeaseToken", p."LeaseExpiresAtUtc"
FROM "BusinessFormPublicationOperations" p JOIN "BusinessFormDefinitions" d ON d."Id" = p."DefinitionId"
WHERE d."Key" = 'delivery';


-- =====================================================================================
-- PASO 5 · El módulo WhatsApp lo publica en Meta
-- El proceso le pasa la versión al módulo WhatsApp por un puerto (fuera de su transacción).
-- 5a) WhatsApp guarda el Flow a publicar y la primera operación: 2 tablas, +1 fila cada una.
--     FlowJson es el esquema traducido a componentes de WhatsApp.
-- =====================================================================================
BEGIN;
INSERT INTO "WhatsAppFlowBindings" ("Id", "DefinitionId", "VersionId", "Culture", "ContentHash", "CompilerVersion", "MetaFlowId", "FlowJson", "State")
SELECT uuidv7(), d."Id", v."Id", 'es', encode(sha256(convert_to(demo.compilar_flow(v."SchemaJson")::text, 'UTF8')), 'hex'), 'compilador-1', NULL,
       demo.compilar_flow(v."SchemaJson"), 'Pending'
FROM "BusinessFormDefinitions" d JOIN "BusinessFormVersions" v ON v."DefinitionId" = d."Id" AND v."Number" = 1
WHERE d."Key" = 'delivery';

INSERT INTO "WhatsAppFlowOperations" ("Id", "BindingId", "Kind", "RequestId", "Status", "Fence", "CreatedAtUtc")
SELECT uuidv7(), b."Id", 'Create', uuidv7(), 'Running', 1, now()
FROM "WhatsAppFlowBindings" b JOIN "BusinessFormDefinitions" d ON d."Id" = b."DefinitionId"
WHERE d."Key" = 'delivery';
COMMIT;

-- 5b) Cada llamada a Meta se hace FUERA de las transacciones, y su resultado se guarda
--     antes de la siguiente: crear (Meta devuelve el ID), subir el JSON, publicar.
BEGIN;
UPDATE "WhatsAppFlowOperations" o SET "Status" = 'Succeeded', "ExternalId" = '9000000000000201', "CompletedAtUtc" = now()
FROM "WhatsAppFlowBindings" b JOIN "BusinessFormDefinitions" d ON d."Id" = b."DefinitionId"
WHERE o."BindingId" = b."Id" AND d."Key" = 'delivery' AND o."Kind" = 'Create';
UPDATE "WhatsAppFlowBindings" b SET "MetaFlowId" = '9000000000000201'
FROM "BusinessFormDefinitions" d WHERE d."Id" = b."DefinitionId" AND d."Key" = 'delivery';
COMMIT;

INSERT INTO "WhatsAppFlowOperations" ("Id", "BindingId", "Kind", "RequestId", "Status", "ExternalId", "Fence", "CreatedAtUtc", "CompletedAtUtc")
SELECT uuidv7(), b."Id", k.kind, uuidv7(), 'Succeeded', b."MetaFlowId", 1, now(), now()
FROM "WhatsAppFlowBindings" b JOIN "BusinessFormDefinitions" d ON d."Id" = b."DefinitionId"
CROSS JOIN (VALUES ('Upload'), ('Publish')) AS k (kind)
WHERE d."Key" = 'delivery';

UPDATE "WhatsAppFlowBindings" b SET "State" = 'Published', "PublishedAtUtc" = now()
FROM "BusinessFormDefinitions" d WHERE d."Id" = b."DefinitionId" AND d."Key" = 'delivery';

-- MIRÁ 5a · El Flow publicado y su ID en Meta.
SELECT b."State", b."MetaFlowId", b."Culture", b."CompilerVersion", b."PublishedAtUtc"
FROM "WhatsAppFlowBindings" b JOIN "BusinessFormDefinitions" d ON d."Id" = b."DefinitionId"
WHERE d."Key" = 'delivery';

-- MIRÁ 5b · Los tres pasos contra Meta, cada uno con su resultado.
SELECT o."Kind", o."Status", o."ExternalId", o."CreatedAtUtc", o."CompletedAtUtc"
FROM "WhatsAppFlowOperations" o JOIN "WhatsAppFlowBindings" b ON b."Id" = o."BindingId" JOIN "BusinessFormDefinitions" d ON d."Id" = b."DefinitionId"
WHERE d."Key" = 'delivery' ORDER BY o."CreatedAtUtc", o."Kind";

-- MIRÁ 5c · El JSON que se subió a Meta: cada pregunta convertida en un componente.
SELECT c ->> 'type' AS componente_whatsapp, c ->> 'name' AS pregunta, c ->> 'label' AS etiqueta, c -> 'data-source' AS opciones
FROM "WhatsAppFlowBindings" b JOIN "BusinessFormDefinitions" d ON d."Id" = b."DefinitionId"
CROSS JOIN LATERAL jsonb_array_elements(b."FlowJson" -> 'screens' -> 0 -> 'layout' -> 'children' -> 0 -> 'children') c
WHERE d."Key" = 'delivery';


-- =====================================================================================
-- PASO 6 · Formularios confirma y activa la versión 1
-- UNA transacción en el módulo de formularios. Toca 2 tablas:
--   BusinessFormPublicationOperations  queda Completed / Activated
--   BusinessFormDefinitions            ActiveVersionId = versión 1
-- Recién ahora la pantalla dice «Publicado · versión 1» y se puede mandar desde el chat.
-- =====================================================================================
BEGIN;
UPDATE "BusinessFormPublicationOperations" p
SET "Status" = 'Completed', "Stage" = 'Activated', "LeaseToken" = NULL, "LeaseExpiresAtUtc" = NULL, "CompletedAtUtc" = now()
FROM "BusinessFormDefinitions" d
WHERE d."Id" = p."DefinitionId" AND d."Key" = 'delivery' AND p."Status" = 'Running';

UPDATE "BusinessFormDefinitions" d SET "ActiveVersionId" = v."Id"
FROM "BusinessFormVersions" v
WHERE v."DefinitionId" = d."Id" AND v."Number" = 1 AND d."Key" = 'delivery';
COMMIT;

-- MIRÁ 6 · Como lo ve el administrador en «Diseñar formularios».
SELECT * FROM vistas.formularios WHERE formulario = 'Registrar entrega';


-- =====================================================================================
-- PASO 7 · (Etapa 3) Ana configura «Cuando llega una respuesta» y toca «Guardar reglas»
-- No toca la versión ni WhatsApp. UNA transacción, 2 tablas:
--   BusinessFormRuleSets     +1  revisión 1 de las reglas
--   BusinessFormDefinitions  ActiveRuleSetId = esa revisión
-- =====================================================================================
BEGIN;
INSERT INTO "BusinessFormRuleSets" ("Id", "DefinitionId", "Revision", "RulesJson", "ActivatedAtUtc", "ActivatedBy")
SELECT uuidv7(), d."Id", 1,
  '{"rules": [
     {"id": "e1", "when": "Submitted", "do": [
       {"action": "whatsapp.message", "config": {"to": "respondent", "text": "Recibimos la entrega {record.number}. Gracias, {respondent.firstName}."}}]},
     {"id": "e2", "when": "Submitted", "if": {"field": "complete", "op": "eq", "value": false}, "do": [
       {"action": "notify.group", "config": {"group": "Administración", "text": "La entrega {record.number} quedó incompleta."}}]}
   ]}'::jsonb,
  now(), (SELECT "Id" FROM "AspNetUsers" WHERE "Email" = 'ana@example.com')
FROM "BusinessFormDefinitions" d WHERE d."Key" = 'delivery';

UPDATE "BusinessFormDefinitions" d SET "ActiveRuleSetId" = rs."Id"
FROM "BusinessFormRuleSets" rs WHERE rs."DefinitionId" = d."Id" AND rs."Revision" = 1 AND d."Key" = 'delivery';
COMMIT;

-- MIRÁ 7 · Las reglas, legibles.
SELECT rule ->> 'id' AS regla, rule ->> 'when' AS cuando, rule -> 'if' AS si, a ->> 'action' AS hacer, a -> 'config' AS configuracion
FROM "BusinessFormDefinitions" d
JOIN "BusinessFormRuleSets" rs ON rs."Id" = d."ActiveRuleSetId"
CROSS JOIN LATERAL jsonb_array_elements(rs."RulesJson" -> 'rules') rule
CROSS JOIN LATERAL jsonb_array_elements(rule -> 'do') a
WHERE d."Key" = 'delivery';


-- =====================================================================================
-- RESUMEN · Cuántas filas dejó «Registrar entrega» en cada tabla
-- =====================================================================================
WITH d AS (SELECT "Id" FROM "BusinessFormDefinitions" WHERE "Key" = 'delivery')
SELECT 'BusinessFormDefinitions' AS tabla, count(*) AS filas FROM "BusinessFormDefinitions" WHERE "Id" IN (SELECT "Id" FROM d)
UNION ALL SELECT 'BusinessFormFields', count(*) FROM "BusinessFormFields" WHERE "DefinitionId" IN (SELECT "Id" FROM d)
UNION ALL SELECT 'BusinessFormVersions', count(*) FROM "BusinessFormVersions" WHERE "DefinitionId" IN (SELECT "Id" FROM d)
UNION ALL SELECT 'BusinessFormPublicationOperations', count(*) FROM "BusinessFormPublicationOperations" WHERE "DefinitionId" IN (SELECT "Id" FROM d)
UNION ALL SELECT 'WhatsAppFlowBindings', count(*) FROM "WhatsAppFlowBindings" WHERE "DefinitionId" IN (SELECT "Id" FROM d)
UNION ALL SELECT 'WhatsAppFlowOperations', count(*) FROM "WhatsAppFlowOperations" WHERE "BindingId" IN (SELECT "Id" FROM "WhatsAppFlowBindings" WHERE "DefinitionId" IN (SELECT "Id" FROM d))
UNION ALL SELECT 'BusinessFormRuleSets', count(*) FROM "BusinessFormRuleSets" WHERE "DefinitionId" IN (SELECT "Id" FROM d);
