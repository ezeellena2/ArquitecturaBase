// Trabajo simulado para los tests del carril: anota cuándo empieza y termina, espera y sale con el código pedido.
// Uso: node work.mjs <archivo> <etiqueta> <milisegundos> [código de salida]
import { appendFileSync } from 'node:fs';

const [file, label, ms = '0', exitCode = '0'] = process.argv.slice(2);
appendFileSync(file, `start ${label} ${Date.now()}\n`);
setTimeout(() => {
  appendFileSync(file, `end ${label} ${Date.now()}\n`);
  process.exit(Number(exitCode));
}, Number(ms));
