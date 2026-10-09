// Claude devuelve un sobre: el contrato común está en structured_output.
export function claudeInvocation({ front, schema }) {
  return { command: 'claude', args: ['--safe-mode', '-p', '--tools', 'Read,Grep,Glob', '--allowedTools', 'Read,Grep,Glob', '--disable-slash-commands', '--add-dir', front, '--output-format', 'json', '--json-schema', JSON.stringify(schema)] };
}

export function parseClaudeResult(text) {
  const result = JSON.parse(text);
  if (result.is_error) throw new Error(`Claude failed: ${result.result ?? result.subtype}`);
  if (!result.structured_output) throw new Error('Claude returned no structured_output; inspect engine logs.');
  return result.structured_output;
}
