// Únicamente el protocolo de Codex CLI. No decide las reglas de documentación.
export function codexInvocation({ cwd, output, schemaPath }) {
  return { command: 'codex', args: ['exec', '--ignore-user-config', '--sandbox', 'read-only', '--ephemeral', '--cd', cwd, '--output-schema', schemaPath, '--output-last-message', output, '--json', '-'] };
}

export function parseCodexResult(text) {
  return JSON.parse(text);
}
