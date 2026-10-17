# Assistant: scope técnico de capacidades locales

El ejecutor abre y libera un scope por ejecución de una capacidad Read o Proposal. Resuelve sólo la fuente tipada de la identidad y versión exactas; no cae a la versión vigente. No comparte DbContext, guarda ni llama al modelo. Las fuentes de Application conservan reglas, permisos y DTO tipados; EF permanece en lectores. El puerto neutral no conoce módulos opcionales y los argumentos del modelo no abren HTTP o SQL genéricos. Hereda Infrastructure y [ADR 0020](../../../docs/decisions/0020-motor-unico-de-ia-con-capacidades.md).
