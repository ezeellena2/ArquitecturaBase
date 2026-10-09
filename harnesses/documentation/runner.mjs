// El runner es quien llama al motor. No es un servidor ni se ejecuta solo.
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { createWriteStream } from 'node:fs';
import { readFile, writeFile, mkdir, open, unlink, access } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { collectInventory, assertUnchanged, checkDocuments, plannedDocuments, prepareChanges, applyChanges, validateSchema } from '../shared/core.mjs';
import { codexInvocation, parseCodexResult } from '../shared/engines/codex.mjs';
import { claudeInvocation, parseClaudeResult } from '../shared/engines/claude.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const json = value => JSON.stringify(value, null, 2);

// No shell strings: arguments remain separate, including prompts and schemas.
export function captureProcess(command, args, { cwd, stdin, output, timeoutMs }) {
  return new Promise((resolve, reject) => {
    const stdout = createWriteStream(`${output}.stdout.log`), stderr = createWriteStream(`${output}.stderr.log`);
    const child = spawn(command, args, { cwd, shell: false, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    let spawnError = null, timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      if (process.platform === 'win32' && child.pid) {
        // Only this spawned process tree; never enumerate or stop unrelated processes.
        spawn('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
      } else child.kill('SIGTERM');
    }, timeoutMs);
    child.stdout.pipe(stdout); child.stderr.pipe(stderr);
    child.stdin.on('error', () => {}); // Early auth/argument failure can close stdin first.
    child.on('error', error => { spawnError = error; });
    child.stdin.end(stdin ?? '');
    child.on('close', async code => {
      clearTimeout(timer);
      await Promise.all([new Promise(r => stdout.closed ? r() : stdout.on('close', r)), new Promise(r => stderr.closed ? r() : stderr.on('close', r))]);
      if (spawnError) reject(new Error(`Cannot start ${command}: ${spawnError.message}`));
      else if (timedOut) reject(new Error(`${command} exceeded the configured timeout. Logs: ${output}`));
      else resolve({ command, args, exitCode: code, output });
    });
  });
}

async function invokeEngine({ engine, repositories, schema, schemaPath, directory, prompt, timeoutMs }) {
  const backend = repositories.find(x => x.id === 'backend').root;
  const front = repositories.find(x => x.id === 'front').root;
  const output = path.join(directory, 'engine-result.json');
  const invocation = engine === 'codex'
    ? codexInvocation({ cwd: backend, output, schemaPath })
    : claudeInvocation({ front, schema });
  if (engine === 'codex') {
    // Disable project MCP servers during this local docs job, without changing config.
    let projectConfig = '';
    try { projectConfig = await readFile(path.join(backend, '.codex/config.toml'), 'utf8'); } catch (e) { if (e.code !== 'ENOENT') throw e; }
    const names = new Set([...projectConfig.matchAll(/^\s*\[mcp_servers\.([A-Za-z0-9_-]+)\]\s*$/gm)].map(x => x[1]));
    for (const name of names) invocation.args.push('-c', `mcp_servers.${name}.enabled=false`);
  }
  const result = await captureProcess(invocation.command, invocation.args, { cwd: backend, stdin: prompt, output: path.join(directory, 'engine'), timeoutMs });
  if (result.exitCode !== 0) throw new Error(`${engine} exited with code ${result.exitCode}. Check engine.stdout.log and engine.stderr.log.`);
  return engine === 'codex'
    ? parseCodexResult(await readFile(output, 'utf8'))
    : parseClaudeResult(await readFile(path.join(directory, 'engine.stdout.log'), 'utf8'));
}

async function npmInvocation(args) {
  const nodeFolder = path.dirname(process.execPath);
  const candidates = [process.env.npm_execpath, path.join(nodeFolder, 'node_modules/npm/bin/npm-cli.js'), path.resolve(nodeFolder, '../lib/node_modules/npm/bin/npm-cli.js')].filter(Boolean);
  for (const candidate of candidates) {
    try { await access(candidate); return { command: process.execPath, args: [candidate, ...args] }; } catch { /* Try the next existing installation, never install dependencies. */ }
  }
  throw new Error('Cannot locate npm-cli.js. Use a Node installation including npm.');
}

function reportMarkdown(report) {
  const lines = ['# Auditor documental', '', `Estado: **${report.status}**. Motor: **${report.engine}**. Modo: **${report.mode}**.`, '', report.summary ?? '', '', `Documentos inventariados: ${report.documentCount ?? 0}. Revisados declarados por el agente: ${report.reviewedCount ?? 0}.`, '', '## Cambios aplicados', ''];
  lines.push(...(report.applied?.length ? report.applied.map(x => `- ${x.action}: ${x.repository}/${x.path}`) : ['Ninguno.']));
  for (const [heading, items] of [['Hallazgos', report.findings], ['Pendientes', report.pending], ['Errores estructurales', report.issues?.map(x => `${x.kind}: ${x.file} → ${x.target}`)]]) {
    lines.push('', `## ${heading}`, '', ...(items?.length ? items.map(x => `- ${x}`) : ['Ninguno registrado.']));
  }
  lines.push('', '## Checks del runner', '', ...(report.checks?.map(x => `- ${x.command} ${x.args.join(' ')}: ${x.exitCode === 0 ? 'correcto' : `falló (${x.exitCode})`}`) ?? []));
  if (report.error) lines.push('', '## Error de ejecución', '', report.error);
  lines.push('', 'Las propuestas y logs completos están junto a este informe. Los checks locales no acreditan proveedores externos.', '');
  return lines.join('\n');
}

export async function run({ engine = 'codex', mode = 'audit', checks = 'full', dryRun = false, configPath = path.join(here, 'config.json'), engineRunner = invokeEngine, processRunner = captureProcess, log = console.log } = {}) {
  if (!['codex', 'claude', 'none'].includes(engine) || !['audit', 'update'].includes(mode) || !['structural', 'full'].includes(checks)) throw new Error('Invalid engine, mode or checks.');
  if (engine === 'none' && mode === 'update') throw new Error('Update requires codex or claude.');
  const configFolder = path.dirname(configPath), config = JSON.parse(await readFile(configPath, 'utf8'));
  const repositories = config.repositories.map(x => ({ ...x, root: path.resolve(configFolder, x.root) }));
  const base = path.resolve(configFolder, config.output);
  if (!Number.isFinite(config.timeoutMinutes) || config.timeoutMinutes <= 0) throw new Error('Invalid timeoutMinutes.');
  const timeoutMs = config.timeoutMinutes * 60_000;
  await mkdir(base, { recursive: true });
  const lockPath = path.join(base, 'documentation.lock');
  let lock;
  try { lock = await open(lockPath, 'wx'); } catch (e) { if (e.code === 'EEXIST') throw new Error(`Another run holds ${lockPath}. If it crashed, verify its process ended before removing the lock.`); throw e; }
  const id = `${new Date().toISOString().replaceAll(':', '-')}-${randomUUID().slice(0, 8)}`;
  const directory = path.join(base, id);
  const report = { status: 'incomplete', engine, mode, checksMode: checks, dryRun, applied: [], checks: [], issues: [], findings: [], pending: [] };
  try {
    await lock.writeFile(json({ pid: process.pid, id }));
    await mkdir(directory);
    log(`Resultados: ${directory}`);
    log('1/4 Inventario y controles iniciales');
    const inventory = await collectInventory(repositories);
    if (inventory.repositories.some(x => x.branch !== 'main')) throw new Error('This project works on main. No branch was changed.');
    report.repositories = inventory.repositories;
    report.documentCount = inventory.files.filter(x => x.path.endsWith('.md')).length;
    const { documents } = plannedDocuments(inventory);
    const beforeIssues = await checkDocuments(documents, repositories);
    await writeFile(path.join(directory, 'inventory.json'), json(inventory));
    await writeFile(path.join(directory, 'initial-checks.json'), json(beforeIssues));
    const workflow = await readFile(path.join(here, 'workflow.md'), 'utf8');
    const schemaPath = path.join(here, 'report.schema.json');
    const schema = JSON.parse(await readFile(schemaPath, 'utf8'));
    const prompt = `${workflow}\n\nModo: ${mode}.\nGuía rules: ${path.join(repositories[0].root, config.rules)}\nRepositorios: ${json(inventory.repositories.map(({ id, root, head }) => ({ id, root, head })))}\nInventario de archivos (leé su contenido cuando sea necesario):\n${inventory.files.map(x => `${x.repository}/${x.path}`).join('\n')}\nControles iniciales: ${json(beforeIssues)}\nContrato de respuesta:\n${json(schema)}\n`;
    await writeFile(path.join(directory, 'task.md'), prompt);
    if (dryRun || engine === 'none') {
      report.status = dryRun ? 'prepared' : 'structural-only';
      report.summary = dryRun ? 'Tarea preparada; ningún agente ejecutado y ningún archivo de documentación modificado.' : 'Controles estructurales ejecutados sin análisis semántico de un agente.';
      report.issues = beforeIssues;
    } else {
      log(`2/4 Análisis con ${engine}; el agente solo propone cambios`);
      const result = await engineRunner({ engine, repositories, schema, schemaPath, directory, prompt, timeoutMs });
      validateSchema(result, schema);
      await writeFile(path.join(directory, 'proposal.json'), json(result));
      const knownDocs = new Set(inventory.files.filter(x => x.path.endsWith('.md')).map(x => `${x.repository}/${x.path}`));
      if (result.reviewed.some(x => !knownDocs.has(x))) throw new Error('Agent claimed to review documents absent from the inventory.');
      report.reviewedCount = new Set(result.reviewed).size;
      report.unreviewed = [...knownDocs].filter(x => !result.reviewed.includes(x));
      Object.assign(report, { summary: result.summary, findings: result.findings, pending: result.pending, proposedCount: result.changes.length });
      // Even audit rejects engine side effects and concurrent source changes.
      await assertUnchanged(inventory, repositories);
      const plan = await prepareChanges(result.changes, inventory, repositories);
      log('3/4 Validar propuesta y, en update, aplicar documentos');
      const proposedTree = plannedDocuments(inventory, plan);
      const proposedIssues = await checkDocuments(proposedTree.documents, repositories, proposedTree.deleted);
      await writeFile(path.join(directory, 'proposal-checks.json'), json(proposedIssues));
      if (proposedIssues.length) {
        report.issues = proposedIssues;
        throw new Error(`Proposed documentation has invalid links/instructions (${proposedIssues.length}). No changes applied.`);
      }
      if (mode === 'update') report.applied = await applyChanges(plan, inventory, repositories, path.join(directory, 'recovery'));
      const current = mode === 'update' ? await collectInventory(repositories) : inventory;
      report.issues = await checkDocuments(plannedDocuments(current).documents, repositories);
      report.status = `${mode === 'update' ? 'updated' : 'audited'}${report.unreviewed.length ? '-partial' : ''}`;
    }
    log('4/4 Controles finales');
    // Build/test are runner-owned, never commands supplied by an agent.
    if (!dryRun && checks === 'full') {
      for (const [index, check] of config.checks.entries()) {
        const repo = repositories.find(x => x.id === check.repository);
        if (!repo || !['dotnet', 'npm'].includes(check.command)) throw new Error('Unknown configured check.');
        const invocation = check.command === 'npm' ? await npmInvocation(check.args) : check;
        log(`Check: ${check.repository} ${check.command} ${check.args.join(' ')}`);
        const result = await processRunner(invocation.command, invocation.args, { cwd: repo.root, output: path.join(directory, `check-${index}`), timeoutMs });
        report.checks.push({ ...result, command: check.command, args: check.args });
        if (result.exitCode !== 0) throw new Error(`Check failed: ${check.command} ${check.args.join(' ')}. Changes and recovery files were retained for review.`);
      }
    }
    for (const [index, repo] of repositories.entries()) {
      const result = await processRunner('git', ['-c', `safe.directory=${repo.root.replaceAll('\\', '/')}`, '-C', repo.root, 'diff', '--check'], { cwd: repo.root, output: path.join(directory, `diff-${index}`), timeoutMs });
      report.checks.push(result);
      if (result.exitCode !== 0) throw new Error(`git diff --check failed in ${repo.id}.`);
    }
    if (report.issues.length) throw new Error(`Documentation checks reported ${report.issues.length} issue(s).`);
    report.exitCode = 0;
  } catch (error) {
    if (error.applied) report.applied = error.applied;
    report.status = 'incomplete'; report.error = error.message; report.exitCode = 1;
    log(`Ejecución incompleta: ${error.message}`);
  } finally {
    try {
      await mkdir(directory, { recursive: true });
      await writeFile(path.join(directory, 'report.json'), json(report));
      await writeFile(path.join(directory, 'report.md'), reportMarkdown(report));
    } finally {
      await lock.close();
      await unlink(lockPath);
    }
  }
  log(`Estado: ${report.status}. Informe: ${path.join(directory, 'report.md')}`);
  return { ...report, directory };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const options = {};
  try {
    for (let i = 2; i < process.argv.length; i++) {
      const flag = process.argv[i];
      if (flag === '--dry-run') options.dryRun = true;
      else if (['--engine', '--mode', '--checks'].includes(flag) && process.argv[i + 1]) options[flag.slice(2)] = process.argv[++i];
      else throw new Error(`Unknown or incomplete argument: ${flag}`);
    }
    process.exitCode = (await run(options)).exitCode;
  } catch (e) { console.error(e.message); process.exitCode = 1; }
}
