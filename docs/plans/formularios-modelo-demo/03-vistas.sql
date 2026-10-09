-- =====================================================================================
-- Formularios por WhatsApp · base de demostración · 3/4 VISTAS
--
-- Vistas en castellano sobre las tablas de app. En la aplicación, la planilla de cada
-- formulario se arma de la misma forma, pero dinámicamente desde sus preguntas; acá
-- hay una vista fija por formulario para que se pueda mirar en DBeaver.
-- =====================================================================================

DO $$
BEGIN
  IF current_database() <> 'formularios_demo' THEN
    RAISE EXCEPTION 'Este script solo se carga en la base formularios_demo (estás en %).', current_database();
  END IF;
END $$;

DROP SCHEMA IF EXISTS vistas CASCADE;
CREATE SCHEMA vistas;
COMMENT ON SCHEMA vistas IS 'Vistas en castellano para consultar sin pelearse con los nombres técnicos.';
SET search_path = app;

-- Ayudas ------------------------------------------------------------------------------

CREATE FUNCTION vistas.numero(p_prefijo text, p_numero integer) RETURNS text
LANGUAGE sql IMMUTABLE AS $$ SELECT p_prefijo || '-' || lpad(p_numero::text, 6, '0') $$;
COMMENT ON FUNCTION vistas.numero(text, integer) IS 'Arma el número que ve la gente: VIS + 24 = VIS-000024.';

CREATE FUNCTION vistas.hora_local(p_utc timestamptz) RETURNS timestamp
LANGUAGE sql IMMUTABLE AS $$ SELECT p_utc AT TIME ZONE 'America/Argentina/Cordoba' $$;
COMMENT ON FUNCTION vistas.hora_local(timestamptz) IS 'La base guarda todo en UTC; esto lo muestra en hora de Argentina.';

CREATE FUNCTION vistas.opcion(p_version_id uuid, p_choice_id uuid) RETURNS text
LANGUAGE sql STABLE AS $$
  SELECT o -> 'label' ->> 'es'
  FROM app."BusinessFormVersions" v
  CROSS JOIN LATERAL jsonb_array_elements(v."SchemaJson" -> 'questions') q
  CROSS JOIN LATERAL jsonb_array_elements(coalesce(q -> 'options', '[]'::jsonb)) o
  WHERE v."Id" = p_version_id AND (o ->> 'choiceId')::uuid = p_choice_id
  LIMIT 1
$$;
COMMENT ON FUNCTION vistas.opcion(uuid, uuid) IS 'La etiqueta de una opción según la versión con la que se respondió.';

CREATE FUNCTION vistas.estado(p_status text) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
  SELECT CASE p_status
    WHEN 'Received' THEN 'Recibida' WHEN 'PendingApproval' THEN 'Por aprobar' WHEN 'Approved' THEN 'Aprobada'
    WHEN 'Rejected' THEN 'Rechazada' WHEN 'Superseded' THEN 'Reemplazada' ELSE p_status END
$$;

-- Formularios y preguntas -------------------------------------------------------------

CREATE VIEW vistas.formularios AS
SELECT d."Name" AS formulario,
       CASE d."Kind" WHEN 'Catalog' THEN 'catálogo' ELSE 'formulario' END AS tipo,
       d."NumberPrefix" AS prefijo,
       v."Number" AS version_vigente,
       CASE
         WHEN d."ActiveVersionId" IS NULL THEN 'Borrador, sin publicar'
         WHEN d."Kind" = 'Catalog' THEN 'Solo en la web'
         WHEN b."State" = 'Published' THEN 'Publicado · versión ' || v."Number"
         ELSE coalesce(b."State", 'Sin publicar en WhatsApp')
       END AS en_whatsapp,
       d."DraftJson" IS NOT NULL AS tiene_cambios_sin_publicar,
       jsonb_array_length(coalesce(v."SchemaJson", d."DraftJson") -> 'questions') AS preguntas,
       (SELECT count(*) FROM app."BusinessFormRecords" r WHERE r."DefinitionId" = d."Id") AS respuestas,
       d."LastRecordNumber" AS ultimo_numero,
       rs."Revision" AS revision_de_reglas
FROM app."BusinessFormDefinitions" d
LEFT JOIN app."BusinessFormVersions" v ON v."Id" = d."ActiveVersionId"
LEFT JOIN app."WhatsAppFlowBindings" b ON b."VersionId" = d."ActiveVersionId"
LEFT JOIN app."BusinessFormRuleSets" rs ON rs."Id" = d."ActiveRuleSetId"
WHERE NOT d."IsDeleted";
COMMENT ON VIEW vistas.formularios IS 'El listado «Diseñar formularios». El cruce con WhatsAppFlowBindings acá es directo; en la aplicación pasa por un puerto, porque son módulos separados.';

CREATE VIEW vistas.preguntas AS
SELECT d."Name" AS formulario,
       v."Number" AS version,
       q.orden,
       q.p -> 'label' ->> 'es' AS pregunta,
       q.p ->> 'type' AS tipo,
       (q.p ->> 'required')::boolean AS obligatoria,
       q.p ->> 'key' AS clave_interna,
       (SELECT string_agg(o -> 'label' ->> 'es', ', ') FROM jsonb_array_elements(coalesce(q.p -> 'options', '[]'::jsonb)) o) AS opciones,
       (q.p ->> 'fieldId')::uuid AS id_pregunta
FROM app."BusinessFormVersions" v
JOIN app."BusinessFormDefinitions" d ON d."Id" = v."DefinitionId"
CROSS JOIN LATERAL jsonb_array_elements(v."SchemaJson" -> 'questions') WITH ORDINALITY AS q (p, orden);
COMMENT ON VIEW vistas.preguntas IS 'Las preguntas de cada versión, leídas del JSON de la versión. id_pregunta es el mismo en todas las versiones.';

-- Respuestas y valores ----------------------------------------------------------------

CREATE VIEW vistas.respuestas AS
SELECT vistas.numero(d."NumberPrefix", r."Number") AS numero,
       d."Name" AS formulario,
       v."Number" AS version,
       vistas.estado(r."Status") AS estado,
       u."DisplayName" AS respondio,
       CASE r."SourceKind" WHEN 'WhatsAppFlow' THEN 'WhatsApp' ELSE 'Web' END AS canal,
       vistas.hora_local(r."SubmittedAtUtc") AS recibida,
       r."ValuesJson" AS valores_json,
       r."Id" AS id_respuesta
FROM app."BusinessFormRecords" r
JOIN app."BusinessFormDefinitions" d ON d."Id" = r."DefinitionId"
JOIN app."BusinessFormVersions" v ON v."Id" = r."VersionId"
JOIN app."AspNetUsers" u ON u."Id" = r."RespondentUserId"
WHERE NOT r."IsDeleted";

CREATE VIEW vistas.valores AS
SELECT rr.numero, rr.formulario, rr.version,
       q.p -> 'label' ->> 'es' AS pregunta,
       f."DataKind" AS clase_de_dato,
       coalesce(val."TextValue", trim_scale(val."DecimalValue")::text, to_char(val."DateValue", 'YYYY-MM-DD'),
                CASE val."BooleanValue" WHEN true THEN 'sí' WHEN false THEN 'no' END,
                vistas.opcion(rr_v."VersionId", val."ChoiceId")) AS valor
FROM app."BusinessFormRecordValues" val
JOIN app."BusinessFormFields" f ON f."Id" = val."FieldId"
JOIN app."BusinessFormRecords" rr_v ON rr_v."Id" = val."RecordId"
JOIN vistas.respuestas rr ON rr.id_respuesta = val."RecordId"
JOIN app."BusinessFormVersions" v ON v."Id" = rr_v."VersionId"
CROSS JOIN LATERAL (SELECT p FROM jsonb_array_elements(v."SchemaJson" -> 'questions') p WHERE (p ->> 'fieldId')::uuid = f."Id") q
UNION ALL
SELECT rr.numero, rr.formulario, rr.version,
       q.p -> 'label' ->> 'es',
       'Reference',
       t."ValuesJson" ->> (q.p ->> 'display')
FROM app."BusinessFormRecordLinks" l
JOIN app."BusinessFormRecords" s ON s."Id" = l."SourceRecordId"
JOIN app."BusinessFormRecords" t ON t."Id" = l."TargetRecordId"
JOIN vistas.respuestas rr ON rr.id_respuesta = s."Id"
JOIN app."BusinessFormVersions" v ON v."Id" = s."VersionId"
CROSS JOIN LATERAL (SELECT p FROM jsonb_array_elements(v."SchemaJson" -> 'questions') p WHERE (p ->> 'fieldId')::uuid = l."FieldId") q;
COMMENT ON VIEW vistas.valores IS 'Cada dato respondido con la etiqueta de su versión. Las respuestas a catálogos salen de RecordLinks.';

-- Las planillas (una por formulario) --------------------------------------------------

CREATE VIEW vistas.planilla_registrar_visita AS
SELECT vistas.numero(d."NumberPrefix", r."Number") AS numero,
       max(vistas.opcion(r."VersionId", val."ChoiceId")) FILTER (WHERE f."Key" = 'client') AS cliente,
       max(val."DateValue") FILTER (WHERE f."Key" = 'visit_date') AS fecha_visita,
       max(val."TextValue") FILTER (WHERE f."Key" = 'comments') AS observaciones,
       round(max(val."DecimalValue") FILTER (WHERE f."Key" = 'amount'), 2) AS importe_cobrado,
       u."DisplayName" AS cargo,
       CASE r."SourceKind" WHEN 'WhatsAppFlow' THEN 'WhatsApp' ELSE 'Web' END AS canal,
       vistas.estado(r."Status") AS estado,
       v."Number" AS version,
       vistas.hora_local(r."SubmittedAtUtc") AS recibida
FROM app."BusinessFormRecords" r
JOIN app."BusinessFormDefinitions" d ON d."Id" = r."DefinitionId" AND d."Key" = 'visit'
JOIN app."BusinessFormVersions" v ON v."Id" = r."VersionId"
JOIN app."AspNetUsers" u ON u."Id" = r."RespondentUserId"
LEFT JOIN app."BusinessFormRecordValues" val ON val."RecordId" = r."Id"
LEFT JOIN app."BusinessFormFields" f ON f."Id" = val."FieldId"
WHERE NOT r."IsDeleted"
GROUP BY r."Id", d."NumberPrefix", u."DisplayName", v."Number";
COMMENT ON VIEW vistas.planilla_registrar_visita IS 'La planilla de visitas. importe_cobrado vacío = la pregunta no existía en esa versión (no es cero).';

CREATE VIEW vistas.planilla_registrar_mantenimiento AS
SELECT vistas.numero(d."NumberPrefix", r."Number") AS numero,
       max(vistas.opcion(r."VersionId", val."ChoiceId")) FILTER (WHERE f."Key" = 'equipment') AS equipo,
       max(val."DateValue") FILTER (WHERE f."Key" = 'work_date') AS fecha,
       max(vistas.opcion(r."VersionId", val."ChoiceId")) FILTER (WHERE f."Key" = 'work_type') AS tipo_de_trabajo,
       round(max(val."DecimalValue") FILTER (WHERE f."Key" = 'cost'), 2) AS costo,
       max(val."TextValue") FILTER (WHERE f."Key" = 'notes') AS notas,
       u."DisplayName" AS cargo,
       CASE r."SourceKind" WHEN 'WhatsAppFlow' THEN 'WhatsApp' ELSE 'Web' END AS canal,
       vistas.hora_local(r."SubmittedAtUtc") AS recibida
FROM app."BusinessFormRecords" r
JOIN app."BusinessFormDefinitions" d ON d."Id" = r."DefinitionId" AND d."Key" = 'maintenance'
JOIN app."AspNetUsers" u ON u."Id" = r."RespondentUserId"
LEFT JOIN app."BusinessFormRecordValues" val ON val."RecordId" = r."Id"
LEFT JOIN app."BusinessFormFields" f ON f."Id" = val."FieldId"
WHERE NOT r."IsDeleted"
GROUP BY r."Id", d."NumberPrefix", u."DisplayName";

CREATE VIEW vistas.planilla_pedido_de_materiales AS
SELECT vistas.numero(d."NumberPrefix", r."Number") AS numero,
       (SELECT t."ValuesJson" ->> 'name' FROM app."BusinessFormRecordLinks" l JOIN app."BusinessFormRecords" t ON t."Id" = l."TargetRecordId"
        WHERE l."SourceRecordId" = r."Id" LIMIT 1) AS cliente,
       max(val."TextValue") FILTER (WHERE f."Key" = 'material') AS material,
       trim_scale(max(val."DecimalValue") FILTER (WHERE f."Key" = 'quantity')) AS cantidad,
       bool_or(val."BooleanValue") FILTER (WHERE f."Key" = 'urgent') AS urgente,
       u."DisplayName" AS cargo,
       vistas.hora_local(r."SubmittedAtUtc") AS recibida
FROM app."BusinessFormRecords" r
JOIN app."BusinessFormDefinitions" d ON d."Id" = r."DefinitionId" AND d."Key" = 'material_request'
JOIN app."AspNetUsers" u ON u."Id" = r."RespondentUserId"
LEFT JOIN app."BusinessFormRecordValues" val ON val."RecordId" = r."Id"
LEFT JOIN app."BusinessFormFields" f ON f."Id" = val."FieldId"
WHERE NOT r."IsDeleted"
GROUP BY r."Id", d."NumberPrefix", u."DisplayName";
COMMENT ON VIEW vistas.planilla_pedido_de_materiales IS 'El cliente sale del catálogo Clientes por RecordLinks: si cambia su nombre en el catálogo, cambia acá.';

CREATE VIEW vistas.catalogo_clientes AS
SELECT vistas.numero(d."NumberPrefix", r."Number") AS numero,
       r."ValuesJson" ->> 'name' AS nombre,
       r."ValuesJson" ->> 'tax_id' AS cuit,
       vistas.opcion(r."VersionId", (r."ValuesJson" ->> 'zone')::uuid) AS zona,
       (SELECT count(*) FROM app."BusinessFormRecordLinks" l WHERE l."TargetRecordId" = r."Id") AS veces_elegido
FROM app."BusinessFormRecords" r
JOIN app."BusinessFormDefinitions" d ON d."Id" = r."DefinitionId" AND d."Key" = 'clients';
COMMENT ON VIEW vistas.catalogo_clientes IS 'Un catálogo es un formulario más: sus respuestas son las filas que eligen otros formularios.';

-- Lo que pasa después -----------------------------------------------------------------

CREATE VIEW vistas.trabajos AS
SELECT vistas.numero(d."NumberPrefix", r."Number") AS numero,
       d."Name" AS formulario,
       CASE ar."Trigger" WHEN 'Submitted' THEN 'al recibirse' WHEN 'Approved' THEN 'al aprobarse' WHEN 'Rejected' THEN 'al rechazarse' END AS evento,
       ar."RuleId" AS regla,
       ar."ActionIndex" + 1 AS orden,
       ar."ActionKey" AS accion,
       CASE ar."Status"
         WHEN 'Waiting' THEN 'Esperando a la anterior' WHEN 'Pending' THEN 'Pendiente' WHEN 'Running' THEN 'En curso'
         WHEN 'Succeeded' THEN 'Hecha' WHEN 'Failed' THEN 'Falló' WHEN 'Uncertain' THEN 'Sin confirmar' END AS estado,
       ar."Attempt" AS intentos,
       ar."ErrorCode" AS error,
       rs."Revision" AS revision_de_reglas,
       vistas.hora_local(ar."CreatedAtUtc") AS creada,
       vistas.hora_local(ar."CompletedAtUtc") AS terminada,
       ar."ResultJson" AS resultado,
       ar."IdempotencyKey" AS clave_unica,
       ar."Id" AS id_ejecucion
FROM app."BusinessFormActionRuns" ar
JOIN app."BusinessFormRecords" r ON r."Id" = ar."RecordId"
JOIN app."BusinessFormDefinitions" d ON d."Id" = r."DefinitionId"
JOIN app."BusinessFormRuleSets" rs ON rs."Id" = ar."RuleSetId";
COMMENT ON VIEW vistas.trabajos IS 'La cola del motor: cada acción disparada por una regla, con su estado.';

CREATE VIEW vistas.tareas AS
SELECT vistas.numero(d."NumberPrefix", r."Number") AS numero,
       d."Name" AS formulario,
       CASE t."Kind" WHEN 'Approval' THEN 'Aprobar' END AS tarea,
       ro."Name" AS para,
       CASE t."Status" WHEN 'Open' THEN 'Abierta' WHEN 'Approved' THEN 'Aprobada' WHEN 'Rejected' THEN 'Rechazada' ELSE 'Cancelada' END AS estado,
       u."DisplayName" AS decidio,
       t."Reason" AS motivo,
       vistas.hora_local(t."CreatedAtUtc") AS creada,
       vistas.hora_local(t."DecidedAtUtc") AS decidida
FROM app."BusinessFormTasks" t
JOIN app."BusinessFormRecords" r ON r."Id" = t."RecordId"
JOIN app."BusinessFormDefinitions" d ON d."Id" = r."DefinitionId"
JOIN app."AspNetRoles" ro ON ro."Id" = t."AssigneeRoleId"
LEFT JOIN app."AspNetUsers" u ON u."Id" = t."DecidedBy";

CREATE VIEW vistas.documentos AS
SELECT vistas.numero(d."NumberPrefix", r."Number") AS numero,
       doc."FileName" AS archivo,
       doc."LayoutKey" AS diseno,
       doc."DataSnapshotJson" ->> 'approvedBy' AS aprobada_por,
       vistas.hora_local(doc."IssuedAtUtc") AS emitido,
       doc."DataSnapshotJson" AS copia_fija
FROM app."BusinessFormDocuments" doc
JOIN app."BusinessFormRecords" r ON r."Id" = doc."RecordId"
JOIN app."BusinessFormDefinitions" d ON d."Id" = r."DefinitionId";

CREATE VIEW vistas.historial AS
SELECT rr.numero, rr.recibida AS cuando, 1 AS paso, 'Llegó por ' || rr.canal || ' (versión ' || rr.version || '), de ' || rr.respondio AS que_paso
FROM vistas.respuestas rr
UNION ALL
SELECT tr.numero, coalesce(tr.terminada, tr.creada), 2,
       'Regla ' || tr.regla || ' ' || tr.evento || ' → ' || tr.accion || ': ' || lower(tr.estado) ||
       CASE WHEN tr.intentos > 1 THEN ' (' || tr.intentos || ' intentos)' ELSE '' END ||
       coalesce(' · ' || tr.error, '')
FROM vistas.trabajos tr
UNION ALL
SELECT ta.numero, ta.decidida, 3, ta.decidio || ' la ' || CASE ta.estado WHEN 'Aprobada' THEN 'aprobó' ELSE 'rechazó' END || coalesce(': ' || ta.motivo, '')
FROM vistas.tareas ta WHERE ta.decidida IS NOT NULL
UNION ALL
SELECT dc.numero, dc.emitido, 4, 'Se emitió ' || dc.archivo
FROM vistas.documentos dc;
COMMENT ON VIEW vistas.historial IS 'Todo lo que le pasó a cada respuesta. Filtrá por numero y ordená por cuando y paso.';

-- Canal WhatsApp ----------------------------------------------------------------------

CREATE VIEW vistas.sesiones_whatsapp AS
SELECT d."Name" AS formulario,
       v."Number" AS version,
       e."DisplayName" AS empleado,
       coalesce(o."DisplayName", 'el asistente') AS lo_mando,
       CASE s."State" WHEN 'Sent' THEN 'Esperando respuesta' WHEN 'Completed' THEN 'Respondida' WHEN 'Expired' THEN 'Venció' ELSE 'Revocada' END AS estado,
       vistas.hora_local(s."CreatedAtUtc") AS enviada,
       vistas.hora_local(s."ExpiresAtUtc") AS vence,
       vistas.hora_local(s."CompletedAtUtc") AS respondida,
       (SELECT vistas.numero(d."NumberPrefix", r."Number") FROM app."BusinessFormRecords" r WHERE r."Id" = s."CompletedRecordId") AS respuesta,
       b."MetaFlowId" AS flow_en_meta
FROM app."WhatsAppFlowSessions" s
JOIN app."WhatsAppFlowBindings" b ON b."Id" = s."BindingId"
JOIN app."BusinessFormDefinitions" d ON d."Id" = s."DefinitionId"
JOIN app."BusinessFormVersions" v ON v."Id" = s."VersionId"
JOIN app."AspNetUsers" e ON e."Id" = s."EmployeeUserId"
LEFT JOIN app."AspNetUsers" o ON o."Id" = s."OperatorUserId";
COMMENT ON VIEW vistas.sesiones_whatsapp IS 'Cada formulario mandado por WhatsApp. El cruce con formularios usa las referencias opacas de la sesión.';

CREATE VIEW vistas.cola_de_salida AS
SELECT vistas.hora_local(m."RequestedAtUtc") AS pedido,
       c."ProfileName" AS para,
       CASE m."Origin" WHEN 'Operator' THEN 'operador' WHEN 'Assistant' THEN 'asistente' ELSE 'regla de un formulario' END AS origen,
       CASE m."Kind" WHEN 'Text' THEN 'texto' WHEN 'Flow' THEN 'formulario' ELSE 'documento' END AS tipo,
       m."Body" AS texto,
       m."State" AS estado,
       m."RequestId" AS pedido_id
FROM app."WhatsAppOutgoingMessages" m
JOIN app."WhatsAppContacts" c ON c."Id" = m."ContactId";
COMMENT ON VIEW vistas.cola_de_salida IS 'Lo que sale por WhatsApp. Si lo pidió una regla, pedido_id es el id de la ejecución (idempotencia).';
