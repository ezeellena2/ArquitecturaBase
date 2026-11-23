// Espera a que se cierre su entrada, como un comando que pregunta algo. Si nadie la cierra, sale con 3 a los 10 s.
const timer = setTimeout(() => process.exit(3), 10000);
process.stdin.on('data', () => {});
process.stdin.on('end', () => {
  clearTimeout(timer);
  console.log('stdin closed');
  process.exit(0);
});
process.stdin.resume();
