# WhatsApp: arnés de Flow

Dobles de HTTP/puertos con datos ficticios, incluida una fuente `IStructuredFormSource` neutral para selección/envío y puertos capturadores neutrales para probar el webhook/respuesta, sin referencias a BusinessForms ni red Meta real. No registrar en src. Preservar redacción y pruebas de fase/fence/privacidad. Retirar con el módulo.

export-k7-http.ps1 ejecuta la suite completa y, sólo en verde, copia al front cuerpos HTTP sintéticos de preparación y respuesta, con procedencia y hashes automáticos. Meta y el puerto de registros de ese productor son capturadores; la persistencia BusinessForms real y rollback de referencias se prueban por separado.
