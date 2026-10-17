# WhatsApp: arnés de Flow

Dobles de HTTP/puertos con datos ficticios, incluida una fuente `IStructuredFormSource` neutral para selección/envío y puertos capturadores neutrales para probar el webhook/respuesta, sin referencias a BusinessForms ni red Meta real. No registrar en src. Preservar redacción y pruebas de fase/fence/privacidad. Retirar con el módulo.

export-k7-http.ps1 ejecuta la suite completa y, sólo en verde, copia al front cuerpos HTTP sintéticos de preparación y respuesta, con procedencia y hashes automáticos. Meta y el puerto de registros de ese productor son capturadores; la persistencia BusinessForms real y rollback de referencias se prueban por separado.

WhatsAppFormActionScenario prepara base propia, actores y contactos con puertos reales y dobles de transporte/almacenamiento; se retira con el módulo. WhatsAppFormActionContractCapture conserva bytes HTTP intactos sólo con WHATSAPP_FORM_ACTIONS_HTTP_OUTPUT. El exportador central incorpora los hashes de escenario, prueba y capturador en la cohorte whatsapp-form-actions-http.

ModeAssistantOperations devuelve únicamente disponibilidad ficticia para probar permisos HTTP de cambio de modo; no interpreta ni ejecuta operaciones IA.

WhatsAppAudioContractCapture conserva bytes HTTP reales de WhatsAppChatTests.Audio sólo al configurar WHATSAPP_AUDIO_HTTP_OUTPUT; el escenario usa texto sintético explícito y no invoca Meta ni IA. No completar ni reescribir los cuerpos exportados.

WhatsAppAssistantScenario crea una base PostgreSQL aislada, cuentas/teléfonos confirmados, publicación Flow por el canal real y un inbound de negocio procesado. WhatsAppAssistantOperationsDouble sólo encamina estados del contrato neutral junto con WhatsAppAssistantFormSource; no interpreta mensajes ni acredita decisiones IA. WhatsAppAssistantContractCapture escribe bytes HTTP intactos para WHATSAPP_ASSISTANT_HTTP_OUTPUT, sin completar o fabricar JSON.
