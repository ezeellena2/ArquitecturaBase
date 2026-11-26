# ARCA: los web services que siguen

Hoja de ruta del 2026-10-31, condensada el 2026-11-26. Las seis etapas (conexión, motor para documentos, factura C, factura
A y B, delegación guiada y delegación con robot) y los arreglos de la revisión de arquitectura están programados: la regla
vive en [ARCA](../features/arca.md) y, la del motor, en [formularios](../features/formularios.md#cálculos--ca1-a-2026-11-09);
lo que falta probar con ARCA real está en [verificación y producción](verificacion-y-produccion.md#arca-homologación-y-cuentas-reales).
Es la parte 2 de los [proyectos de documentos](../decisions/0022-lectura-de-documentos-con-ia.md) y amplía el
[estudio de integraciones](integraciones/estudio-2026-10-22.md). Lo que queda acá son propuestas sin programar; las
decisiones, las etapas, el diseño y los arreglos se recuperan desde la [procedencia](../history/README.md#cierre-del-2026-11-26).

## Qué servicios de ARCA entran

Del [catálogo de web services](https://www.arca.gob.ar/ws/documentacion/catalogo.asp) (48 servicios) y de los
[web services de factura electrónica](https://www.arca.gob.ar/ws/documentacion/ws-factura-electronica.asp) (7), consultados
el 2026-10-31:

| Familia | Servicios | Para nosotros |
| --- | --- | --- |
| Base | WSAA: ticket de acceso con certificado o delegación | La conexión; no es un formulario |
| Consultas | Constancia de inscripción, padrón A4, A10 y A13, A100 (tablas), constatación de comprobantes (WSCDC) y apócrifos | Autocompletar y validar la CUIT, catálogos con datos de ARCA y verificar facturas recibidas |
| Facturación | wsfev1 (A, B, C y M, sin ítems), wsmtxca (A y B con ítems), wsfexv1 (exportación), wsbfev1 (bienes de capital) y wsct (turismo) | Formularios de emisión |
| Granos | WSCPE (carta de porte) y WSLPG (liquidación primaria) | Formularios de emisión |
| Hacienda | WSLSP (bovinos, porcinos y aves; [manual 2.0.6 del 23/06/2026](https://www.afip.gob.ar/ws/WSLSP/manual_wslsp_2.0.6.pdf)) y WSREMCARNE ([remito cárnico](https://www.arca.gob.ar/actividadesAgropecuarias/sector-pecuario/), que se vincula con la factura) | Formularios de emisión |
| Otros sectores | Lechería (WSLUM), tabaco (WSLTV), caña (WSLCA) y remitos de harina y de azúcar | Si un cliente los pide |
| No aplica | Los 11 de Aduana y Grandes Operadores; los reservados a bancos u organismos (deuda, VEP, DNRPA y Mi Argentina); DDJJ, F931 y seguros de caución | Fuera |

**El DT-e no es de ARCA:** lo emite SENASA en [SIGSA](https://www.argentina.gob.ar/node/150794). No encontramos un web
service público documentado; se pide a SENASA y se trata como una integración aparte.

**wsfev1 no recibe los ítems**, solo los totales y el IVA por alícuota. Los renglones hacen falta para el PDF, que tiene que
describir lo vendido.

## Lo que sigue

Cada familia nueva es una carpeta propia del módulo ([ADR 0025](../decisions/0025-familias-de-servicios-arca.md)) y tiene
su diseño, sus tableros y su plan antes de programarse. De a una:

- **Hacienda:** WSLSP con porcinos y el remito cárnico (WSREMCARNE), que se vincula con la factura.
- **Granos:** la carta de porte (WSCPE) y la liquidación primaria (WSLPG).
- **Consultas:** la constatación de facturas recibidas (WSCDC).
- **Facturación:** la exportación (wsfexv1), la factura de crédito MiPyME, facturar al cobrar y cargar la factura por
  audio con la IA.
- **Otros sectores**, si un cliente los pide.
