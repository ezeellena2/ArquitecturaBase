-- =====================================================================================
-- Formularios por WhatsApp · base de demostración · 1/4 ESQUEMA
--
-- Sirve para ver y consultar el modelo propuesto. NO es la migración de EF: la real se
-- genera desde las configuraciones del código y vive en el esquema public.
-- Se carga con levantar.ps1 (ver README.md). Volver a cargarlo borra y recrea todo.
-- =====================================================================================

DO $$
BEGIN
  IF current_database() <> 'formularios_demo' THEN
    RAISE EXCEPTION 'Este script solo se carga en la base formularios_demo (estás en %).', current_database();
  END IF;
END $$;

DROP SCHEMA IF EXISTS vistas CASCADE;
DROP SCHEMA IF EXISTS demo CASCADE;
DROP SCHEMA IF EXISTS app CASCADE;

CREATE SCHEMA app;
CREATE SCHEMA vistas;
CREATE SCHEMA demo;

COMMENT ON SCHEMA app IS 'Las tablas, con los nombres que tendrían en el sistema (como las genera EF: PascalCase entre comillas).';
COMMENT ON SCHEMA vistas IS 'Vistas en castellano para consultar sin pelearse con los nombres técnicos.';
COMMENT ON SCHEMA demo IS 'Funciones de ayuda de la demo, como reconstruir la tabla de valores.';

-- Así las consultas no necesitan escribir app. ni vistas. delante de cada tabla (vale para conexiones nuevas).
ALTER DATABASE formularios_demo SET search_path = app, vistas, demo, public;

SET search_path = app;

-- -------------------------------------------------------------------------------------
-- YA EXISTEN EN EL SISTEMA (versión reducida: solo las columnas que usa la demo)
-- -------------------------------------------------------------------------------------

CREATE TABLE "AspNetRoles" (
  "Id"   uuid PRIMARY KEY,
  "Name" varchar(256) NOT NULL UNIQUE
);
COMMENT ON TABLE "AspNetRoles" IS 'YA EXISTE. Roles. En la demo también sirven como grupos para asignar tareas (Supervisor, Administración).';

CREATE TABLE "AspNetUsers" (
  "Id"           uuid PRIMARY KEY,
  "UserName"     varchar(256) NOT NULL,
  "Email"        varchar(256) NOT NULL UNIQUE,
  "DisplayName"  varchar(100) NOT NULL,
  "IsActive"     boolean NOT NULL DEFAULT true,
  "CreatedAtUtc" timestamptz NOT NULL
);
COMMENT ON TABLE "AspNetUsers" IS 'YA EXISTE (reducida). Las cuentas: administradora, empleados, supervisora.';

CREATE TABLE "AspNetUserRoles" (
  "UserId" uuid NOT NULL REFERENCES "AspNetUsers" ("Id"),
  "RoleId" uuid NOT NULL REFERENCES "AspNetRoles" ("Id"),
  PRIMARY KEY ("UserId", "RoleId")
);
COMMENT ON TABLE "AspNetUserRoles" IS 'YA EXISTE. Qué rol tiene cada cuenta.';

CREATE TABLE "WhatsAppContacts" (
  "Id"           uuid PRIMARY KEY,
  "WaId"         varchar(32) NOT NULL UNIQUE,
  "ProfileName"  varchar(256),
  "UserId"       uuid NULL REFERENCES "AspNetUsers" ("Id"),
  "Mode"         varchar(20) NOT NULL DEFAULT 'Manual',
  "ModeRevision" bigint NOT NULL DEFAULT 1
);
COMMENT ON TABLE "WhatsAppContacts" IS 'YA EXISTE (reducida). Cada número que escribió. UserId es el vínculo con una cuenta: sin vínculo no se puede mandar un formulario.';
COMMENT ON COLUMN "WhatsAppContacts"."UserId" IS 'Cuenta vinculada. Solo cambia por un recorrido explícito (perfil o administración), nunca por un mensaje.';

-- -------------------------------------------------------------------------------------
-- 1. QUÉ SE PREGUNTA
-- -------------------------------------------------------------------------------------

CREATE TABLE "BusinessFormDefinitions" (
  "Id"               uuid PRIMARY KEY,
  "Kind"             varchar(20) NOT NULL CHECK ("Kind" IN ('Form', 'Catalog')),
  "Key"              varchar(64) NOT NULL,
  "Name"             varchar(120) NOT NULL,
  "NumberPrefix"     varchar(10) NOT NULL,
  "LastRecordNumber" integer NOT NULL DEFAULT 0,
  "DraftJson"        jsonb NULL,
  "DraftRevision"    integer NOT NULL DEFAULT 1,
  "ActiveVersionId"  uuid NULL,
  "ActiveRuleSetId"  uuid NULL,
  "CreatedAtUtc"     timestamptz NOT NULL,
  "CreatedBy"        uuid NOT NULL REFERENCES "AspNetUsers" ("Id"),
  "ModifiedAtUtc"    timestamptz NULL,
  "ModifiedBy"       uuid NULL REFERENCES "AspNetUsers" ("Id"),
  "IsDeleted"        boolean NOT NULL DEFAULT false
);
CREATE UNIQUE INDEX "UX_BusinessFormDefinitions_Key" ON "BusinessFormDefinitions" ("Key") WHERE NOT "IsDeleted";
CREATE UNIQUE INDEX "UX_BusinessFormDefinitions_NumberPrefix" ON "BusinessFormDefinitions" ("NumberPrefix");
COMMENT ON TABLE "BusinessFormDefinitions" IS 'Formularios. Uno por formulario o catálogo. Tiene el borrador en edición y apunta a la versión y a las reglas vigentes.';
COMMENT ON COLUMN "BusinessFormDefinitions"."Kind" IS 'Form = formulario común. Catalog = catálogo (clientes, productos): sus respuestas son las opciones de otros formularios.';
COMMENT ON COLUMN "BusinessFormDefinitions"."NumberPrefix" IS 'Prefijo de la numeración (VIS, MNT). Único y no se recicla.';
COMMENT ON COLUMN "BusinessFormDefinitions"."LastRecordNumber" IS 'Último número asignado. Se incrementa bajo lock, en el mismo commit que guarda la respuesta.';
COMMENT ON COLUMN "BusinessFormDefinitions"."DraftJson" IS 'Borrador del editor (mismo formato que una versión). NULL si no hay cambios sin publicar.';
COMMENT ON COLUMN "BusinessFormDefinitions"."ActiveVersionId" IS 'Versión que se manda hoy. NULL si nunca se publicó.';
COMMENT ON COLUMN "BusinessFormDefinitions"."ActiveRuleSetId" IS 'Revisión vigente de «Cuando llega una respuesta». NULL si el formulario no tiene reglas.';

CREATE TABLE "BusinessFormVersions" (
  "Id"                  uuid PRIMARY KEY,
  "DefinitionId"        uuid NOT NULL REFERENCES "BusinessFormDefinitions" ("Id"),
  "Number"              integer NOT NULL,
  "SchemaFormatVersion" integer NOT NULL,
  "SchemaJson"          jsonb NOT NULL,
  "ContentHash"         varchar(64) NOT NULL,
  "CreatedAtUtc"        timestamptz NOT NULL,
  "CreatedBy"           uuid NOT NULL REFERENCES "AspNetUsers" ("Id"),
  UNIQUE ("DefinitionId", "Number"),
  UNIQUE ("Id", "DefinitionId")
);
COMMENT ON TABLE "BusinessFormVersions" IS 'Versiones. Una foto fija por publicación: qué se pregunta y cómo se valida. Nunca se edita. NO guarda reglas, vistas ni acciones.';
COMMENT ON COLUMN "BusinessFormVersions"."SchemaJson" IS 'Preguntas en orden, con tipo, etiqueta, ayuda, obligatoria, opciones (choiceId estable) y límites. Reserva grupos y referencias desde el formato 1.';
COMMENT ON COLUMN "BusinessFormVersions"."CreatedAtUtc" IS 'Cuándo se congeló, al confirmar «Publicar». Queda vigente recién cuando WhatsApp confirma: ver PublicationOperations.';
COMMENT ON COLUMN "BusinessFormVersions"."SchemaFormatVersion" IS 'Versión del formato del JSON. Permite leer versiones viejas si el formato evoluciona.';

ALTER TABLE "BusinessFormDefinitions"
  ADD CONSTRAINT "FK_Definitions_ActiveVersion"
  FOREIGN KEY ("ActiveVersionId", "Id") REFERENCES "BusinessFormVersions" ("Id", "DefinitionId");

CREATE TABLE "BusinessFormFields" (
  "Id"               uuid PRIMARY KEY,
  "DefinitionId"     uuid NOT NULL REFERENCES "BusinessFormDefinitions" ("Id"),
  "Key"              varchar(64) NOT NULL,
  "DataKind"         varchar(20) NOT NULL CHECK ("DataKind" IN ('Text', 'Decimal', 'Date', 'Boolean', 'Choice', 'Reference', 'Group')),
  "CreatedInVersion" integer NULL,
  "RetiredInVersion" integer NULL,
  UNIQUE ("DefinitionId", "Key"),
  UNIQUE ("Id", "DefinitionId")
);
COMMENT ON TABLE "BusinessFormFields" IS 'Preguntas. La identidad de cada pregunta a lo largo de las versiones. Se registra al publicar (el borrador ya trae su fieldId). Cambiar la etiqueta no la cambia; cambiar el tipo crea otra.';
COMMENT ON COLUMN "BusinessFormFields"."Key" IS 'Clave interna estable (en inglés). Nunca se muestra ni se renombra. Es la clave en ValuesJson.';
COMMENT ON COLUMN "BusinessFormFields"."DataKind" IS 'Clase de dato: decide en qué columna de Valores se guarda. Texto corto y largo son Text.';

CREATE TABLE "BusinessFormPublicationOperations" (
  "Id"                uuid PRIMARY KEY,
  "DefinitionId"      uuid NOT NULL REFERENCES "BusinessFormDefinitions" ("Id"),
  "VersionId"         uuid NOT NULL,
  "RequestId"         uuid NOT NULL,
  "RequestedBy"       uuid NOT NULL REFERENCES "AspNetUsers" ("Id"),
  "Status"            varchar(20) NOT NULL CHECK ("Status" IN ('Pending', 'Running', 'Completed', 'Failed', 'Uncertain')),
  "Stage"             varchar(30) NOT NULL,
  "Fence"             bigint NOT NULL DEFAULT 0,
  "LeaseToken"        uuid NULL,
  "LeaseExpiresAtUtc" timestamptz NULL,
  "ErrorCode"         varchar(100) NULL,
  "RequestedAtUtc"    timestamptz NOT NULL,
  "CompletedAtUtc"    timestamptz NULL,
  UNIQUE ("DefinitionId", "RequestId"),
  FOREIGN KEY ("VersionId", "DefinitionId") REFERENCES "BusinessFormVersions" ("Id", "DefinitionId")
);
COMMENT ON TABLE "BusinessFormPublicationOperations" IS 'Publicaciones. El trabajo de «Publicar en WhatsApp»: sobrevive a un reinicio y no se hace dos veces (mismo RequestId).';
COMMENT ON COLUMN "BusinessFormPublicationOperations"."Fence" IS 'Sube cada vez que un trabajador reserva el trabajo. Un trabajador viejo no puede pisar el resultado de uno nuevo.';

-- -------------------------------------------------------------------------------------
-- 2. QUÉ SE RESPONDIÓ
-- -------------------------------------------------------------------------------------

CREATE TABLE "BusinessFormRecords" (
  "Id"                 uuid PRIMARY KEY,
  "DefinitionId"       uuid NOT NULL REFERENCES "BusinessFormDefinitions" ("Id"),
  "VersionId"          uuid NOT NULL,
  "Number"             integer NOT NULL,
  "Status"             varchar(20) NOT NULL CHECK ("Status" IN ('Received', 'PendingApproval', 'Approved', 'Rejected', 'Superseded')),
  "ValuesJson"         jsonb NOT NULL,
  "RespondentUserId"   uuid NOT NULL REFERENCES "AspNetUsers" ("Id"),
  "SourceKind"         varchar(20) NOT NULL CHECK ("SourceKind" IN ('WhatsAppFlow', 'Web')),
  "SourceId"           varchar(100) NOT NULL,
  "SubmittedAtUtc"     timestamptz NOT NULL,
  "SupersedesRecordId" uuid NULL REFERENCES "BusinessFormRecords" ("Id"),
  "Revision"           integer NOT NULL DEFAULT 1,
  "IsDeleted"          boolean NOT NULL DEFAULT false,
  UNIQUE ("DefinitionId", "Number"),
  UNIQUE ("SourceKind", "SourceId"),
  UNIQUE ("Id", "DefinitionId"),
  FOREIGN KEY ("VersionId", "DefinitionId") REFERENCES "BusinessFormVersions" ("Id", "DefinitionId")
);
CREATE INDEX "IX_Records_Definition_Submitted" ON "BusinessFormRecords" ("DefinitionId", "SubmittedAtUtc" DESC, "Id");
CREATE INDEX "IX_Records_Definition_Status" ON "BusinessFormRecords" ("DefinitionId", "Status", "Id");
COMMENT ON TABLE "BusinessFormRecords" IS 'Respuestas. Una por envío: el documento completo, tal como llegó. Los valores no se modifican; solo cambia el estado.';
COMMENT ON COLUMN "BusinessFormRecords"."ValuesJson" IS 'Lo respondido, por clave de pregunta. Opción = choiceId, decimal = texto, fecha = YYYY-MM-DD. Una pregunta sin responder no aparece. Las referencias a catálogos van en RecordLinks.';
COMMENT ON COLUMN "BusinessFormRecords"."VersionId" IS 'La versión con la que se respondió. Las etiquetas se leen de ahí, aunque el formulario cambie.';
COMMENT ON COLUMN "BusinessFormRecords"."SourceId" IS 'Origen (sesión del Flow o pedido web). Único con SourceKind: la misma entrega no se guarda dos veces.';
COMMENT ON COLUMN "BusinessFormRecords"."Revision" IS 'Protege los cambios de estado concurrentes (aprobar dos veces a la vez).';

CREATE TABLE "BusinessFormRecordValues" (
  "Id"                  uuid PRIMARY KEY DEFAULT uuidv7(),
  "RecordId"            uuid NOT NULL,
  "DefinitionId"        uuid NOT NULL,
  "FieldId"             uuid NOT NULL,
  "RowId"               uuid NULL,
  "Ordinal"             integer NOT NULL DEFAULT 0,
  "TextValue"           varchar(4000) NULL,
  "NormalizedTextValue" varchar(4000) NULL,
  "DecimalValue"        numeric(18, 4) NULL,
  "DateValue"           date NULL,
  "BooleanValue"        boolean NULL,
  "ChoiceId"            uuid NULL,
  FOREIGN KEY ("RecordId", "DefinitionId") REFERENCES "BusinessFormRecords" ("Id", "DefinitionId") ON DELETE CASCADE,
  FOREIGN KEY ("FieldId", "DefinitionId") REFERENCES "BusinessFormFields" ("Id", "DefinitionId"),
  CHECK (num_nonnulls("TextValue", "DecimalValue", "DateValue", "BooleanValue", "ChoiceId") = 1)
);
CREATE UNIQUE INDEX "UX_RecordValues_Context" ON "BusinessFormRecordValues"
  ("RecordId", "FieldId", COALESCE("RowId", '00000000-0000-0000-0000-000000000000'::uuid), "Ordinal");
CREATE INDEX "IX_RecordValues_Decimal" ON "BusinessFormRecordValues" ("DefinitionId", "FieldId", "DecimalValue", "RecordId") WHERE "DecimalValue" IS NOT NULL;
CREATE INDEX "IX_RecordValues_Date" ON "BusinessFormRecordValues" ("DefinitionId", "FieldId", "DateValue", "RecordId") WHERE "DateValue" IS NOT NULL;
CREATE INDEX "IX_RecordValues_Choice" ON "BusinessFormRecordValues" ("DefinitionId", "FieldId", "ChoiceId", "RecordId") WHERE "ChoiceId" IS NOT NULL;
CREATE INDEX "IX_RecordValues_Text" ON "BusinessFormRecordValues" ("DefinitionId", "FieldId", "NormalizedTextValue", "RecordId") WHERE "NormalizedTextValue" IS NOT NULL;
CREATE INDEX "IX_RecordValues_Boolean" ON "BusinessFormRecordValues" ("DefinitionId", "FieldId", "BooleanValue", "RecordId") WHERE "BooleanValue" IS NOT NULL;
COMMENT ON TABLE "BusinessFormRecordValues" IS 'Valores. Una fila por respuesta y pregunta, con el dato en su columna tipada. Es un índice para filtrar y ordenar: se reconstruye desde ValuesJson con demo.reconstruir_valores().';
COMMENT ON COLUMN "BusinessFormRecordValues"."RowId" IS 'Para preguntas dentro de un grupo que se repite (líneas de un remito). NULL fuera de grupos.';
COMMENT ON COLUMN "BusinessFormRecordValues"."NormalizedTextValue" IS 'Texto en minúsculas para buscar sin distinguir mayúsculas.';

CREATE TABLE "BusinessFormCaptureReceipts" (
  "Id"             uuid PRIMARY KEY,
  "SourceKind"     varchar(20) NOT NULL,
  "SourceId"       varchar(100) NOT NULL,
  "BodyHash"       varchar(64) NOT NULL,
  "VersionId"      uuid NOT NULL REFERENCES "BusinessFormVersions" ("Id"),
  "Status"         varchar(20) NOT NULL CHECK ("Status" IN ('Accepted', 'Rejected')),
  "RecordId"       uuid NULL REFERENCES "BusinessFormRecords" ("Id"),
  "ErrorCode"      varchar(100) NULL,
  "ReceivedAtUtc"  timestamptz NOT NULL,
  UNIQUE ("SourceKind", "SourceId"),
  CHECK (("Status" = 'Accepted' AND "RecordId" IS NOT NULL) OR ("Status" = 'Rejected' AND "ErrorCode" IS NOT NULL))
);
COMMENT ON TABLE "BusinessFormCaptureReceipts" IS 'Comprobantes. Si la misma entrega vuelve a llegar, devuelve el mismo resultado (la misma respuesta o el mismo rechazo) en vez de crear otra.';

CREATE TABLE "BusinessFormRecordLinks" (
  "Id"             uuid PRIMARY KEY DEFAULT uuidv7(),
  "SourceRecordId" uuid NOT NULL REFERENCES "BusinessFormRecords" ("Id") ON DELETE CASCADE,
  "FieldId"        uuid NOT NULL REFERENCES "BusinessFormFields" ("Id"),
  "RowId"          uuid NULL,
  "Ordinal"        integer NOT NULL DEFAULT 0,
  "TargetRecordId" uuid NOT NULL REFERENCES "BusinessFormRecords" ("Id")
);
CREATE INDEX "IX_RecordLinks_Target" ON "BusinessFormRecordLinks" ("FieldId", "TargetRecordId", "SourceRecordId");
COMMENT ON TABLE "BusinessFormRecordLinks" IS 'Vínculos. Cuando una pregunta elige de un catálogo: esta respuesta apunta a una respuesta del catálogo (el pedido → el cliente).';

-- -------------------------------------------------------------------------------------
-- 3. QUÉ PASA DESPUÉS
-- -------------------------------------------------------------------------------------

CREATE TABLE "BusinessFormRuleSets" (
  "Id"             uuid PRIMARY KEY,
  "DefinitionId"   uuid NOT NULL REFERENCES "BusinessFormDefinitions" ("Id"),
  "Revision"       integer NOT NULL,
  "RulesJson"      jsonb NOT NULL,
  "ActivatedAtUtc" timestamptz NOT NULL,
  "ActivatedBy"    uuid NOT NULL REFERENCES "AspNetUsers" ("Id"),
  UNIQUE ("DefinitionId", "Revision"),
  UNIQUE ("Id", "DefinitionId")
);
COMMENT ON TABLE "BusinessFormRuleSets" IS 'Reglas. «Cuando llega una respuesta», con revisiones propias. Cambiarlas no obliga a republicar en WhatsApp.';
COMMENT ON COLUMN "BusinessFormRuleSets"."RulesJson" IS 'Lista de reglas: when (evento), if (condición opcional), do (acciones en orden, del catálogo).';

ALTER TABLE "BusinessFormDefinitions"
  ADD CONSTRAINT "FK_Definitions_ActiveRuleSet"
  FOREIGN KEY ("ActiveRuleSetId", "Id") REFERENCES "BusinessFormRuleSets" ("Id", "DefinitionId");

CREATE TABLE "BusinessFormActionRuns" (
  "Id"                uuid PRIMARY KEY,
  "RecordId"          uuid NOT NULL REFERENCES "BusinessFormRecords" ("Id"),
  "RuleSetId"         uuid NOT NULL REFERENCES "BusinessFormRuleSets" ("Id"),
  "RuleId"            varchar(40) NOT NULL,
  "ActionIndex"       integer NOT NULL,
  "Trigger"           varchar(20) NOT NULL CHECK ("Trigger" IN ('Submitted', 'Approved', 'Rejected')),
  "ActionKey"         varchar(60) NOT NULL,
  "ConfigJson"        jsonb NOT NULL,
  "IdempotencyKey"    varchar(200) NOT NULL UNIQUE,
  "DependsOnRunId"    uuid NULL REFERENCES "BusinessFormActionRuns" ("Id"),
  "Status"            varchar(20) NOT NULL CHECK ("Status" IN ('Waiting', 'Pending', 'Running', 'Succeeded', 'Failed', 'Uncertain')),
  "Attempt"           integer NOT NULL DEFAULT 0,
  "NextAttemptAtUtc"  timestamptz NULL,
  "Fence"             bigint NOT NULL DEFAULT 0,
  "LeaseToken"        uuid NULL,
  "LeaseExpiresAtUtc" timestamptz NULL,
  "ResultJson"        jsonb NULL,
  "ErrorCode"         varchar(100) NULL,
  "CreatedAtUtc"      timestamptz NOT NULL,
  "CompletedAtUtc"    timestamptz NULL
);
CREATE INDEX "IX_ActionRuns_Queue" ON "BusinessFormActionRuns" ("Status", "NextAttemptAtUtc", "Id") WHERE "Status" IN ('Pending', 'Failed');
CREATE INDEX "IX_ActionRuns_Record" ON "BusinessFormActionRuns" ("RecordId", "CreatedAtUtc");
COMMENT ON TABLE "BusinessFormActionRuns" IS 'Ejecuciones. Cada acción por hacer, como trabajo durable. Es la cola del motor: se crean en el mismo commit que la respuesta (o la decisión) y un trabajador las ejecuta después.';
COMMENT ON COLUMN "BusinessFormActionRuns"."IdempotencyKey" IS 'respuesta + evento + regla + acción. Garantiza que la misma acción no se cree dos veces (nunca dos PDFs por la misma aprobación).';
COMMENT ON COLUMN "BusinessFormActionRuns"."ConfigJson" IS 'Copia de la configuración de la acción en la revisión de reglas que la disparó.';
COMMENT ON COLUMN "BusinessFormActionRuns"."DependsOnRunId" IS 'Acción anterior de la misma regla. Mientras no termine, esta queda Waiting.';
COMMENT ON COLUMN "BusinessFormActionRuns"."Status" IS 'Waiting (espera a la anterior), Pending, Running, Succeeded, Failed (se reintenta si es seguro), Uncertain (se comprueba antes de repetir).';

CREATE TABLE "BusinessFormTasks" (
  "Id"              uuid PRIMARY KEY,
  "RecordId"        uuid NOT NULL REFERENCES "BusinessFormRecords" ("Id"),
  "CreatedByRunId"  uuid NOT NULL REFERENCES "BusinessFormActionRuns" ("Id"),
  "Kind"            varchar(20) NOT NULL CHECK ("Kind" IN ('Approval')),
  "AssigneeRoleId"  uuid NOT NULL REFERENCES "AspNetRoles" ("Id"),
  "Status"          varchar(20) NOT NULL CHECK ("Status" IN ('Open', 'Approved', 'Rejected', 'Cancelled')),
  "DecidedBy"       uuid NULL REFERENCES "AspNetUsers" ("Id"),
  "DecidedAtUtc"    timestamptz NULL,
  "Reason"          varchar(500) NULL,
  "CreatedAtUtc"    timestamptz NOT NULL,
  "Revision"        integer NOT NULL DEFAULT 1
);
CREATE INDEX "IX_Tasks_Open" ON "BusinessFormTasks" ("AssigneeRoleId", "CreatedAtUtc") WHERE "Status" = 'Open';
COMMENT ON TABLE "BusinessFormTasks" IS 'Tareas. Lo que espera a una persona. Hoy: aprobar. Decidir dispara los eventos Approved o Rejected.';
COMMENT ON COLUMN "BusinessFormTasks"."AssigneeRoleId" IS 'Grupo al que se le muestra. Para decidir hace falta además el permiso forms.records.approve.';

CREATE TABLE "BusinessFormDocuments" (
  "Id"               uuid PRIMARY KEY,
  "RecordId"         uuid NOT NULL REFERENCES "BusinessFormRecords" ("Id"),
  "CreatedByRunId"   uuid NOT NULL UNIQUE REFERENCES "BusinessFormActionRuns" ("Id"),
  "LayoutKey"        varchar(60) NOT NULL,
  "DataSnapshotJson" jsonb NOT NULL,
  "FileName"         varchar(200) NOT NULL,
  "StorageKey"       varchar(400) NOT NULL,
  "ContentHash"      varchar(64) NOT NULL,
  "IssuedAtUtc"      timestamptz NOT NULL
);
COMMENT ON TABLE "BusinessFormDocuments" IS 'Documentos. El PDF emitido: copia fija de los datos (con etiquetas y nombres del momento) y la referencia al archivo.';

-- -------------------------------------------------------------------------------------
-- 4. CANAL WHATSAPP (módulo aparte: guarda IDs de formularios como referencias opacas, sin FK)
-- -------------------------------------------------------------------------------------

CREATE TABLE "WhatsAppFlowBindings" (
  "Id"              uuid PRIMARY KEY,
  "DefinitionId"    uuid NOT NULL,
  "VersionId"       uuid NOT NULL,
  "Culture"         varchar(10) NOT NULL,
  "ContentHash"     varchar(64) NOT NULL,
  "CompilerVersion" varchar(20) NOT NULL,
  "MetaFlowId"      varchar(40) NULL,
  "FlowJson"        jsonb NULL,
  "State"           varchar(20) NOT NULL CHECK ("State" IN ('Pending', 'Published', 'Failed', 'Uncertain', 'Deprecated')),
  "PublishedAtUtc"  timestamptz NULL,
  UNIQUE ("VersionId", "Culture", "CompilerVersion")
);
COMMENT ON TABLE "WhatsAppFlowBindings" IS 'Flows publicados. Qué versión está en Meta y con qué ID. DefinitionId y VersionId son referencias opacas: el módulo WhatsApp no conoce las tablas de formularios.';

CREATE TABLE "WhatsAppFlowOperations" (
  "Id"             uuid PRIMARY KEY,
  "BindingId"      uuid NOT NULL REFERENCES "WhatsAppFlowBindings" ("Id"),
  "Kind"           varchar(20) NOT NULL CHECK ("Kind" IN ('Create', 'Upload', 'Publish', 'Deprecate')),
  "RequestId"      uuid NOT NULL UNIQUE,
  "Status"         varchar(20) NOT NULL CHECK ("Status" IN ('Pending', 'Running', 'Succeeded', 'Failed', 'Uncertain')),
  "ExternalId"     varchar(60) NULL,
  "Fence"          bigint NOT NULL DEFAULT 0,
  "CreatedAtUtc"   timestamptz NOT NULL,
  "CompletedAtUtc" timestamptz NULL
);
COMMENT ON COLUMN "WhatsAppFlowBindings"."FlowJson" IS 'El JSON exacto que se subió a Meta: cada pregunta convertida en un componente de WhatsApp (Dropdown, DatePicker, TextArea…).';
COMMENT ON TABLE "WhatsAppFlowOperations" IS 'Operaciones del Flow. Crear, subir y publicar en Meta, cada paso con su resultado guardado antes del siguiente.';

CREATE TABLE "WhatsAppOutgoingMessages" (
  "Id"             uuid PRIMARY KEY,
  "ContactId"      uuid NOT NULL REFERENCES "WhatsAppContacts" ("Id"),
  "Origin"         varchar(20) NOT NULL CHECK ("Origin" IN ('Operator', 'Assistant', 'FormAction')),
  "OperatorUserId" uuid NULL REFERENCES "AspNetUsers" ("Id"),
  "Kind"           varchar(20) NOT NULL CHECK ("Kind" IN ('Text', 'Flow', 'Document')),
  "Body"           varchar(4096) NOT NULL,
  "RequestId"      uuid NOT NULL,
  "State"          varchar(20) NOT NULL,
  "RequestedAtUtc" timestamptz NOT NULL,
  "SentAtUtc"      timestamptz NULL,
  UNIQUE ("ContactId", "RequestId"),
  CHECK ("Origin" <> 'Operator' OR "OperatorUserId" IS NOT NULL)
);
COMMENT ON TABLE "WhatsAppOutgoingMessages" IS 'YA EXISTE (reducida y ampliada). La cola de salida del chat. Se amplía con Origin y Kind; OperatorUserId pasa a opcional para los envíos del sistema.';
COMMENT ON COLUMN "WhatsAppOutgoingMessages"."RequestId" IS 'Idempotencia. Cuando lo pide una ejecución de formularios, es el Id de esa ejecución: repetirla no manda dos mensajes.';
COMMENT ON COLUMN "WhatsAppOutgoingMessages"."Origin" IS 'NUEVA. Operator (lo mandó una persona desde el chat), Assistant (lo ofreció el asistente), FormAction (lo pidió una regla).';

CREATE TABLE "WhatsAppFlowSessions" (
  "Id"                uuid PRIMARY KEY,
  "BindingId"         uuid NOT NULL REFERENCES "WhatsAppFlowBindings" ("Id"),
  "DefinitionId"      uuid NOT NULL,
  "VersionId"         uuid NOT NULL,
  "ContactId"         uuid NOT NULL REFERENCES "WhatsAppContacts" ("Id"),
  "EmployeeUserId"    uuid NOT NULL REFERENCES "AspNetUsers" ("Id"),
  "OperatorUserId"    uuid NULL REFERENCES "AspNetUsers" ("Id"),
  "OutgoingMessageId" uuid NULL REFERENCES "WhatsAppOutgoingMessages" ("Id"),
  "TokenHash"         varchar(64) NOT NULL UNIQUE,
  "State"             varchar(20) NOT NULL CHECK ("State" IN ('Sent', 'Completed', 'Expired', 'Revoked')),
  "ExpiresAtUtc"      timestamptz NOT NULL,
  "CreatedAtUtc"      timestamptz NOT NULL,
  "CompletedAtUtc"    timestamptz NULL,
  "CompletedRecordId" uuid NULL
);
COMMENT ON TABLE "WhatsAppFlowSessions" IS 'Sesiones. Cada formulario mandado a alguien: fija versión, contacto y cuenta. El token viaja a Meta; acá solo se guarda su huella.';
COMMENT ON COLUMN "WhatsAppFlowSessions"."OperatorUserId" IS 'Quién lo mandó desde el chat. NULL si lo ofreció el asistente.';
COMMENT ON COLUMN "WhatsAppFlowSessions"."CompletedRecordId" IS 'Respuesta guardada (referencia opaca, sin FK a formularios).';

CREATE TABLE "WhatsAppMessages" (
  "Id"            uuid PRIMARY KEY,
  "ContactId"     uuid NOT NULL REFERENCES "WhatsAppContacts" ("Id"),
  "Direction"     varchar(20) NOT NULL CHECK ("Direction" IN ('Inbound', 'Outbound')),
  "Kind"          varchar(20) NOT NULL CHECK ("Kind" IN ('Text', 'FlowReply', 'Interactive', 'Document')),
  "Body"          varchar(4096) NULL,
  "WaMessageId"   varchar(256) NOT NULL UNIQUE,
  "OccurredAtUtc" timestamptz NOT NULL,
  "FlowSessionId" uuid NULL REFERENCES "WhatsAppFlowSessions" ("Id")
);
COMMENT ON TABLE "WhatsAppMessages" IS 'YA EXISTE (reducida y ampliada). Mensajes recibidos y enviados. Suma el tipo FlowReply y el vínculo con la sesión.';

-- -------------------------------------------------------------------------------------
-- Ayuda: la tabla de valores se reconstruye desde el JSON de cada respuesta
-- -------------------------------------------------------------------------------------

CREATE FUNCTION demo.reconstruir_valores(p_definition_id uuid DEFAULT NULL)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
  filas integer;
BEGIN
  DELETE FROM app."BusinessFormRecordValues" v
  WHERE p_definition_id IS NULL OR v."DefinitionId" = p_definition_id;

  INSERT INTO app."BusinessFormRecordValues"
    ("RecordId", "DefinitionId", "FieldId", "Ordinal", "TextValue", "NormalizedTextValue", "DecimalValue", "DateValue", "BooleanValue", "ChoiceId")
  SELECT r."Id", r."DefinitionId", f."Id", 0,
         CASE WHEN f."DataKind" = 'Text' THEN e.value #>> '{}' END,
         CASE WHEN f."DataKind" = 'Text' THEN lower(e.value #>> '{}') END,
         CASE WHEN f."DataKind" = 'Decimal' THEN (e.value #>> '{}')::numeric END,
         CASE WHEN f."DataKind" = 'Date' THEN (e.value #>> '{}')::date END,
         CASE WHEN f."DataKind" = 'Boolean' THEN (e.value #>> '{}')::boolean END,
         CASE WHEN f."DataKind" = 'Choice' THEN (e.value #>> '{}')::uuid END
  FROM app."BusinessFormRecords" r
  CROSS JOIN LATERAL jsonb_each(r."ValuesJson") AS e
  JOIN app."BusinessFormFields" f ON f."DefinitionId" = r."DefinitionId" AND f."Key" = e.key
  WHERE (p_definition_id IS NULL OR r."DefinitionId" = p_definition_id)
    AND jsonb_typeof(e.value) <> 'null'
    AND f."DataKind" IN ('Text', 'Decimal', 'Date', 'Boolean', 'Choice');

  GET DIAGNOSTICS filas = ROW_COUNT;
  RETURN filas;
END;
$$;
COMMENT ON FUNCTION demo.reconstruir_valores(uuid) IS 'Borra y vuelve a armar la tabla de valores desde ValuesJson. Demuestra que Valores es un índice derivado, no un segundo origen.';

CREATE FUNCTION demo.compilar_flow(p_schema jsonb) RETURNS jsonb
LANGUAGE sql IMMUTABLE AS $$
  SELECT jsonb_build_object(
    'version', '7.1',
    'screens', jsonb_build_array(jsonb_build_object(
      'id', 'FORM',
      'title', p_schema -> 'title' ->> 'es',
      'terminal', true,
      'layout', jsonb_build_object('type', 'SingleColumnLayout', 'children', jsonb_build_array(jsonb_build_object(
        'type', 'Form', 'name', 'form',
        'children',
          coalesce((SELECT jsonb_agg(
             CASE q ->> 'type'
               WHEN 'choice' THEN jsonb_build_object('type', 'Dropdown', 'name', q ->> 'key', 'label', q -> 'label' ->> 'es', 'required', (q ->> 'required')::boolean,
                 'data-source', (SELECT jsonb_agg(jsonb_build_object('id', o ->> 'choiceId', 'title', o -> 'label' ->> 'es')) FROM jsonb_array_elements(q -> 'options') o))
               WHEN 'date' THEN jsonb_build_object('type', 'DatePicker', 'name', q ->> 'key', 'label', q -> 'label' ->> 'es', 'required', (q ->> 'required')::boolean)
               WHEN 'longText' THEN jsonb_build_object('type', 'TextArea', 'name', q ->> 'key', 'label', q -> 'label' ->> 'es', 'required', (q ->> 'required')::boolean, 'max-length', (q ->> 'maxLength')::int)
               WHEN 'decimal' THEN jsonb_build_object('type', 'TextInput', 'input-type', 'number', 'name', q ->> 'key', 'label', q -> 'label' ->> 'es', 'required', (q ->> 'required')::boolean)
               WHEN 'boolean' THEN jsonb_build_object('type', 'RadioButtonsGroup', 'name', q ->> 'key', 'label', q -> 'label' ->> 'es', 'required', (q ->> 'required')::boolean,
                 'data-source', '[{"id": "true", "title": "Sí"}, {"id": "false", "title": "No"}]'::jsonb)
               ELSE jsonb_build_object('type', 'TextInput', 'input-type', 'text', 'name', q ->> 'key', 'label', q -> 'label' ->> 'es', 'required', (q ->> 'required')::boolean)
             END ORDER BY x.orden)
           FROM jsonb_array_elements(p_schema -> 'questions') WITH ORDINALITY AS x (q, orden)), '[]'::jsonb)
          || jsonb_build_array(jsonb_build_object(
               'type', 'Footer',
               'label', p_schema -> 'submitLabel' ->> 'es',
               'on-click-action', jsonb_build_object('name', 'complete', 'payload',
                 coalesce((SELECT jsonb_object_agg(q ->> 'key', '${form.' || (q ->> 'key') || '}') FROM jsonb_array_elements(p_schema -> 'questions') q), '{}'::jsonb))))
      )))
    ))
  )
$$;
COMMENT ON FUNCTION demo.compilar_flow(jsonb) IS 'Imita al compilador del módulo WhatsApp: convierte el esquema de una versión en el Flow JSON de Meta. En el sistema real es código C#; la versión exacta del formato la fija su perfil.';
