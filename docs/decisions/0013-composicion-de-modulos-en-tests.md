# ADR 0013: composición extensible de módulos en tests

Fecha: 2026-10-02. Estado: aceptada e implementada en esta entrega.

## Contexto

Los ganchos `partial void` del arnés admitían una sola implementación, ya ocupada por
WhatsApp. Agregar IA con otra implementación rompía la compilación y quitar un módulo
debía conservar al otro. La frontera productiva y sus reglas no cambian.

## Decisión

Los tests descubren contribuciones presentes, en orden estable: `IApiTestModule` para
settings, servicios e inventario; `IModuleArchitectureContribution` para locks y excepciones
justificadas. `tests/Shared/ModuleApplicationRegistration.cs`, enlazado en dos proyectos,
descubre registros de Application por su convención pública y firma exacta.
Tipos concretos y dobles viven en `Modules/<M>`; el núcleo del arnés no nombra módulos.

## Consecuencias

Dos módulos coexisten y quitar su carpeta retira su contribución. La factory parcial puede
conservar estado propio por módulo, pero no usa un partial void compartido con dueño único.
La reflexión se limita a tests; DI de producción sigue siendo explícita. El inventario y las
reglas siguen verificando todos los módulos. La retirada se prueba en una copia aislada.

## Alternativas descartadas

- Otro partial void por módulo: el núcleo tendría que nombrarlo y editarse por cada alta/baja.
- Un registro monolítico: acopla la factory y sus reglas a tipos opcionales.
- Escanear DI productiva indiscriminadamente: cambia el contrato canónico sin necesidad.
