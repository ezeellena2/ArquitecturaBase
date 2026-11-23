// Usa el procesador sin parar durante los milisegundos pedidos, como una compilación larga.
const end = Date.now() + Number(process.argv[2] ?? 0);
while (Date.now() < end) {
  // Ocupado a propósito.
}
