import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { run, captureProcess } from '../runner.mjs';

async function fixture(t) {
  const root = await mkdtemp(path.join(tmpdir(), 'documentation-runner-'));
  t.after(async () => {
    assert.ok(path.resolve(root).startsWith(path.resolve(tmpdir()) + path.sep));
    assert.ok(path.basename(root).startsWith('documentation-runner-'));
    await rm(root, { recursive: true, force: true });
  });
  for (const name of ['backend', 'front']) {
    const repo = path.join(root, name);
    await mkdir(path.join(repo, 'docs'), { recursive: true });
    execFileSync('git', ['init', '--initial-branch=main', repo], { stdio: 'ignore' });
    await writeFile(path.join(repo, '.gitignore'), '.test-artifacts/\n');
    await writeFile(path.join(repo, 'README.md'), '# Inicio\n[Regla](docs/rule.md)\n');
    await writeFile(path.join(repo, 'docs/rule.md'), '# Regla\nAnterior.\n');
    await writeFile(path.join(repo, 'src.cs'), 'original');
  }
  const configPath = path.join(root, 'config.json');
  await writeFile(configPath, JSON.stringify({
    repositories: ['backend', 'front'].map(id => ({ id, root: id, instructionMap: 'docs/map.md' })),
    output: 'backend/.test-artifacts/results', rules: 'docs/rule.md', timeoutMinutes: 1,
    checks: [{ repository: 'backend', command: 'dotnet', args: ['build'] }]
  }));
  return { root, configPath, options: { configPath, checks: 'structural', log: () => {} } };
}

const proposal = (overrides = {}) => ({ summary: 'Regla contrastada.',
  reviewed: ['backend/README.md', 'backend/docs/rule.md', 'front/README.md', 'front/docs/rule.md'],
  findings: [], pending: [], changes: [{ repository: 'backend', path: 'docs/rule.md', action: 'update',
    reason: 'Corregir descripción.', evidence: ['backend/src.cs:1'], content: '# Regla\nActualizada.\n' }], ...overrides });

for (const engine of ['codex', 'claude']) {
  test(`${engine}: update applies validated documentation and keeps code unchanged`, async t => {
    const f = await fixture(t);
    const report = await run({ ...f.options, engine, mode: 'update', engineRunner: async invocation => {
      assert.equal(invocation.engine, engine);
      assert.match(invocation.prompt, /Inventario/);
      return proposal();
    } });
    assert.equal(report.status, 'updated');
    assert.equal(report.exitCode, 0);
    assert.equal(report.applied.length, 1);
    assert.match(await readFile(path.join(f.root, 'backend/docs/rule.md'), 'utf8'), /Actualizada/);
    assert.equal(await readFile(path.join(f.root, 'backend/src.cs'), 'utf8'), 'original');
    const recovery = JSON.parse(await readFile(path.join(report.directory, 'recovery/originals.json')));
    assert.match(recovery[0].original, /Anterior/);
  });
}

test('audit records a proposal without applying it', async t => {
  const f = await fixture(t);
  const report = await run({ ...f.options, engineRunner: async () => proposal() });
  assert.equal(report.status, 'audited');
  assert.deepEqual(report.applied, []);
  assert.match(await readFile(path.join(f.root, 'backend/docs/rule.md'), 'utf8'), /Anterior/);
});

test('audit rejects a proposal with broken links before reporting success', async t => {
  const f = await fixture(t), result = proposal();
  result.changes[0].content = '# Regla\n[Error](missing.md)\n';
  const report = await run({ ...f.options, engineRunner: async () => result });
  assert.equal(report.status, 'incomplete');
  assert.equal(report.exitCode, 1);
  assert.match(await readFile(path.join(f.root, 'backend/docs/rule.md'), 'utf8'), /Anterior/);
});

test('dry run never calls the agent or full checks', async t => {
  const f = await fixture(t);
  const report = await run({ ...f.options, checks: 'full', dryRun: true,
    engineRunner: async () => { assert.fail('Agent must not execute'); },
    processRunner: async (command, args) => { assert.equal(command, 'git'); return { command, args, exitCode: 0 }; }
  });
  assert.equal(report.status, 'prepared');
  assert.ok((await readFile(path.join(report.directory, 'task.md'), 'utf8')).includes('backend/docs/rule.md'));
});

test('partial agent coverage is declared in the report', async t => {
  const f = await fixture(t);
  const report = await run({ ...f.options, engineRunner: async () => proposal({ reviewed: ['backend/docs/rule.md'] }) });
  assert.equal(report.status, 'audited-partial');
  assert.equal(report.unreviewed.length, 3);
});

test('agent side effects abort application and preserve evidence', async t => {
  const f = await fixture(t);
  const report = await run({ ...f.options, mode: 'update', engineRunner: async () => {
    await writeFile(path.join(f.root, 'backend/src.cs'), 'concurrent');
    return proposal();
  } });
  assert.equal(report.status, 'incomplete');
  assert.match(report.error, /changed/);
  assert.match(await readFile(path.join(f.root, 'backend/docs/rule.md'), 'utf8'), /Anterior/);
});

test('failed full checks retain applied documents and recovery without claiming completion', async t => {
  const f = await fixture(t);
  const report = await run({ ...f.options, checks: 'full', mode: 'update', engineRunner: async () => proposal(),
    processRunner: async (command, args) => ({ command, args, exitCode: 1 }) });
  assert.equal(report.status, 'incomplete');
  assert.equal(report.applied.length, 1);
  assert.match(report.error, /Check failed/);
  assert.match(await readFile(path.join(report.directory, 'recovery/originals.json'), 'utf8'), /Anterior/);
});

test('one lock excludes a second run and is released after completion', async t => {
  const f = await fixture(t);
  let release, entered;
  const ready = new Promise(resolve => { entered = resolve; });
  const first = run({ ...f.options, engineRunner: async () => {
    entered(); await new Promise(resolve => { release = resolve; }); return proposal();
  } });
  await ready;
  await assert.rejects(run({ ...f.options, engine: 'none' }), /Another run/);
  release(); await first;
  assert.equal((await run({ ...f.options, engine: 'none' })).status, 'structural-only');
});

test('process capture writes logs and reports failures to start', async t => {
  const f = await fixture(t);
  const result = await captureProcess(process.execPath, ['-e', 'process.stdout.write("ok"); process.stderr.write("detail")'],
    { cwd: f.root, output: path.join(f.root, 'process'), timeoutMs: 5000 });
  assert.equal(result.exitCode, 0);
  assert.equal(await readFile(path.join(f.root, 'process.stdout.log'), 'utf8'), 'ok');
  await assert.rejects(captureProcess('documentation-harness-missing-executable', [],
    { cwd: f.root, output: path.join(f.root, 'missing'), timeoutMs: 5000 }), /Cannot start/);
});
