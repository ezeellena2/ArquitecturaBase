# Scopes de consultas locales

El ejecutor técnico abre/libera un scope por herramienta. No comparte DbContext entre fuentes y no guarda. Las reglas y permisos siguen en fuentes Application; las consultas EF siguen en lectores. El puerto neutral no conoce IA ni WhatsApp. Nunca abrir HTTP/SQL genéricos por argumentos de un modelo. Hereda Infrastructure y docs/architecture/backend.md.
