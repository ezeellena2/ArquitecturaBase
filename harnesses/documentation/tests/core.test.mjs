import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtemp, mkdir, writeFile, readFile, rm, symlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { collectInventory, checkDocuments, prepareChanges, applyChanges, assertUnchanged, validateSchema } from '../../shared/core.mjs';
import { codexInvocation, parseCodexResult } from '../../shared/engines/codex.mjs';
import { claudeInvocation, parseClaudeResult } from '../../shared/engines/claude.mjs';

async function fixture(t) {
  const root = await mkdtemp(path.join(tmpdir(), 'documentation-harness-'));
  t.after(async () => {
    assert.ok(path.resolve(root).startsWith(path.resolve(tmpdir()) + path.sep));
    assert.ok(path.basename(root).startsWith('documentation-harness-'));
    await rm(root, { recursive: true, force: true });
  });
  const repo = path.join(root, 'backend');
  await mkdir(path.join(repo, 'docs'), { recursive: true });
  execFileSync('git', ['init', '--initial-branch=main', repo], { stdio: 'ignore' });
  await writeFile(path.join(repo, '.gitignore'), '.test-artifacts/\n');
  await writeFile(path.join(repo, 'README.md'), '# Inicio\n\n[Regla](docs/rule.md#regla-vigente)\n');
  await writeFile(path.join(repo, 'docs/rule.md'), '# Regla vigente\n\nTexto anterior.\n');
  await writeFile(path.join(repo, 'src.cs'), 'original');
  const repositories = [{ id: 'backend', root: repo, instructionMap: 'docs/map.md' }];
  return { root, repo, repositories, inventory: await collectInventory(repositories) };
}

function change(overrides = {}) {
  return { repository: 'backend', path: 'docs/rule.md', action: 'update', content: '# Regla vigente\n\nTexto nuevo.\n', reason: 'Contrato corregido.', evidence: ['src.cs:1'], ...overrides };
}

test('inventory includes tracked and new files but excludes ignored outputs', async t => {
  const f = await fixture(t);
  await mkdir(path.join(f.repo, '.test-artifacts'));
  await writeFile(path.join(f.repo, '.test-artifacts/ignored.md'), 'ignored');
  const inv = await collectInventory(f.repositories);
  assert.ok(inv.files.some(x => x.path === 'docs/rule.md'));
  assert.ok(!inv.files.some(x => x.path.includes('.test-artifacts')));
  assert.equal(inv.repositories[0].branch, 'main');
});

test('links check destinations and Unicode anchors, excluding fenced examples', async t => {
  const f = await fixture(t);
  const docs = new Map(f.inventory.files.filter(x => x.path.endsWith('.md')).map(x => [x.absolute, x.content]));
  assert.deepEqual(await checkDocuments(docs, f.repositories), []);
  docs.set(path.join(f.repo, 'README.md'), '# Inicio\n\n[Error](docs/rule.md#inexistente)\n```md\n[Ejemplo](no-existe.md)\n```\n');
  const issues = await checkDocuments(docs, f.repositories);
  assert.equal(issues.length, 1);
  assert.equal(issues[0].kind, 'anchor');
});

test('reference links and deleted destinations are checked in the planned tree', async t => {
  const f = await fixture(t);
  const docs = new Map([[path.join(f.repo, 'README.md'), '# Inicio\n[Regla][r]\n[r]: docs/removed.md\n']]);
  const issues = await checkDocuments(docs, f.repositories, new Set([path.join(f.repo, 'docs/removed.md')]));
  assert.equal(issues[0].kind, 'missing');
});

test('broken CLAUDE imports and absent instruction map entries are reported', async t => {
  const f = await fixture(t);
  const docs = new Map([[path.join(f.repo, 'docs/AGENTS.md'), '# Docs\n']]);
  const issues = await checkDocuments(docs, f.repositories);
  assert.ok(issues.some(x => x.kind === 'instructions-pair'));
  assert.ok(issues.some(x => x.kind === 'instructions-map'));
});

for (const badPath of ['../outside.md', '/outside.md', 'C:/outside.md', 'docs/../README.md', 'src.cs', 'harnesses/README.md', '.git/config.md', 'docs/nul.md', 'docs/a:stream.md', 'docs/bad?.md', 'docs/bad|.md']) {
  test(`proposal rejects disallowed path: ${badPath}`, async t => {
    const f = await fixture(t);
    await assert.rejects(prepareChanges([change({ path: badPath })], f.inventory, f.repositories));
  });
}

test('duplicate targets and absent evidence are rejected before writing', async t => {
  const f = await fixture(t);
  await assert.rejects(prepareChanges([change(), change()], f.inventory, f.repositories), /Duplicate/);
  await assert.rejects(prepareChanges([change({ evidence: [] })], f.inventory, f.repositories), /evidence/);
  assert.match(await readFile(path.join(f.repo, 'docs/rule.md'), 'utf8'), /anterior/);
});

test('new untracked documentation cannot be deleted and instructions cannot be deleted', async t => {
  const f = await fixture(t);
  await mkdir(path.join(f.repo, 'docs/plans'));
  await writeFile(path.join(f.repo, 'docs/plans/new.md'), '# Nuevo\n');
  const baseline = await collectInventory(f.repositories);
  await assert.rejects(prepareChanges([change({ action: 'delete', path: 'docs/plans/new.md', content: null })], baseline, f.repositories), /Git/);
  await assert.rejects(prepareChanges([change({ action: 'delete', path: 'AGENTS.md', content: null })], f.inventory, f.repositories));
});

test('a recoverable clean plan can be retired only after fixing its references', async t => {
  const f = await fixture(t);
  await mkdir(path.join(f.repo, 'docs/plans'));
  await writeFile(path.join(f.repo, 'docs/plans/old.md'), '# Plan\n');
  await writeFile(path.join(f.repo, 'README.md'), '# Inicio\n[Plan](docs/plans/old.md)\n');
  execFileSync('git', ['-C', f.repo, 'add', '.'], { stdio: 'ignore' });
  execFileSync('git', ['-C', f.repo, '-c', 'user.name=Harness test', '-c', 'user.email=harness@example.invalid', 'commit', '-m', 'fixture'], { stdio: 'ignore' });
  const baseline = await collectInventory(f.repositories);
  const deletion = change({ action: 'delete', path: 'docs/plans/old.md', content: null });
  const broken = await prepareChanges([deletion], baseline, f.repositories);
  await assert.rejects(applyChanges(broken, baseline, f.repositories, path.join(f.root, 'backups')), /documentation/);
  const complete = await prepareChanges([deletion, change({ path: 'README.md', content: '# Inicio\n[Regla](docs/rule.md)\n' })], baseline, f.repositories);
  await applyChanges(complete, baseline, f.repositories, path.join(f.root, 'backups'));
  await assert.rejects(readFile(path.join(f.repo, 'docs/plans/old.md')), /ENOENT/);
});

test('a tracked plan with uncommitted decisions cannot be deleted', async t => {
  const f = await fixture(t);
  await mkdir(path.join(f.repo, 'docs/plans'));
  await writeFile(path.join(f.repo, 'docs/plans/old.md'), '# Plan\n');
  execFileSync('git', ['-C', f.repo, 'add', '.'], { stdio: 'ignore' });
  execFileSync('git', ['-C', f.repo, '-c', 'user.name=Harness test', '-c', 'user.email=harness@example.invalid', 'commit', '-m', 'fixture'], { stdio: 'ignore' });
  await writeFile(path.join(f.repo, 'docs/plans/old.md'), '# Plan\nDecisión nueva.\n');
  const baseline = await collectInventory(f.repositories);
  await assert.rejects(prepareChanges([change({ action: 'delete', path: 'docs/plans/old.md', content: null })], baseline, f.repositories), /uncommitted/);
});

test('existing local edits are preserved when one allowed proposal is applied', async t => {
  const f = await fixture(t);
  await writeFile(path.join(f.repo, 'local.md'), '# Trabajo ajeno\n');
  const baseline = await collectInventory(f.repositories);
  const plan = await prepareChanges([change()], baseline, f.repositories);
  await applyChanges(plan, baseline, f.repositories, path.join(f.root, 'backups'));
  assert.match(await readFile(path.join(f.repo, 'docs/rule.md'), 'utf8'), /nuevo/);
  assert.equal(await readFile(path.join(f.repo, 'local.md'), 'utf8'), '# Trabajo ajeno\n');
  assert.equal(await readFile(path.join(f.repo, 'src.cs'), 'utf8'), 'original');
});

test('a broken proposed link prevents every write', async t => {
  const f = await fixture(t);
  const plan = await prepareChanges([change({ content: '# Regla vigente\n[Error](missing.md)' })], f.inventory, f.repositories);
  await assert.rejects(applyChanges(plan, f.inventory, f.repositories, path.join(f.root, 'backups')), /links|documentation/i);
  assert.match(await readFile(path.join(f.repo, 'docs/rule.md'), 'utf8'), /anterior/);
});

test('concurrent changes to source or documentation abort before applying', async t => {
  const f = await fixture(t);
  const plan = await prepareChanges([change()], f.inventory, f.repositories);
  await writeFile(path.join(f.repo, 'src.cs'), 'concurrent');
  await assert.rejects(assertUnchanged(f.inventory, f.repositories), /changed/);
  await assert.rejects(applyChanges(plan, f.inventory, f.repositories, path.join(f.root, 'backups')), /changed/);
  assert.match(await readFile(path.join(f.repo, 'docs/rule.md'), 'utf8'), /anterior/);
});

test('a junction to an external folder is rejected before proposing a write', async t => {
  const f = await fixture(t);
  const external = path.join(f.root, 'external');
  await mkdir(external);
  await symlink(external, path.join(f.repo, 'docs/escape'), process.platform === 'win32' ? 'junction' : 'dir');
  await assert.rejects(prepareChanges([change({ action: 'create', path: 'docs/escape/new.md' })],
    f.inventory, f.repositories), /escapes/);
  await assert.rejects(readFile(path.join(external, 'new.md')), /ENOENT/);
});

test('a partial application records successful writes when a later target changes', async t => {
  const f = await fixture(t);
  const baseline = f.inventory;
  const plan = await prepareChanges([change(), change({ path: 'README.md', content: '# Inicio\n' })], baseline, f.repositories);
  // Deterministic race at the per-file guard, after the initial full inventory check.
  let reads = 0;
  const second = plan[1];
  Object.defineProperty(second, 'beforeHash', { get() { reads++; return 'changed'; } });
  let failure;
  try { await applyChanges(plan, baseline, f.repositories, path.join(f.root, 'backups')); }
  catch (error) { failure = error; }
  assert.ok(reads > 0);
  assert.equal(failure.applied.length, 1);
  assert.match(await readFile(path.join(f.repo, 'docs/rule.md'), 'utf8'), /nuevo/);
  assert.match(await readFile(path.join(f.repo, 'README.md'), 'utf8'), /Regla/);
});

test('schema rejects fabricated shapes and unknown properties', () => {
  const schema = { type: 'object', additionalProperties: false, required: ['summary'], properties: { summary: { type: 'string' } } };
  assert.doesNotThrow(() => validateSchema({ summary: 'Done' }, schema));
  assert.throws(() => validateSchema({ summary: 4 }, schema));
  assert.throws(() => validateSchema({ summary: 'Done', extra: true }, schema));
});

test('Codex uses read-only mode and its own structured response file', () => {
  const options = { cwd: 'repo', output: 'result.json', schemaPath: 'schema.json' };
  const invocation = codexInvocation(options);
  assert.ok(invocation.args.includes('read-only'));
  assert.ok(!invocation.args.includes('danger-full-access'));
  assert.deepEqual(parseCodexResult('{"summary":"ok"}'), { summary: 'ok' });
});

test('Claude has only reading tools and normalizes structured_output', () => {
  const invocation = claudeInvocation({ front: 'front', schema: { type: 'object' } });
  assert.ok(invocation.args.includes('Read,Grep,Glob'));
  assert.ok(!invocation.args.includes('--dangerously-skip-permissions'));
  assert.deepEqual(parseClaudeResult('{"is_error":false,"structured_output":{"summary":"ok"}}'), { summary: 'ok' });
  assert.throws(() => parseClaudeResult('{"is_error":true,"result":"Auth required"}'), /Auth required/);
  assert.throws(() => parseClaudeResult('{"result":"plain text"}'), /structured/);
});
