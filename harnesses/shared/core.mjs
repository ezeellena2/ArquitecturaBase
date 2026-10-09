// Controles comunes: no dependen de Claude ni de Codex.
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readFile, writeFile, mkdir, lstat, realpath, unlink } from 'node:fs/promises';
import path from 'node:path';

export const hash = bytes => createHash('sha256').update(bytes).digest('hex');
export const git = (root, args) => execFileSync('git', ['-c', `safe.directory=${root.replaceAll('\\', '/')}`, '-C', root, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 16 * 1024 * 1024 });

async function exists(file) {
  try { await lstat(file); return true; } catch (e) { if (e.code === 'ENOENT') return false; throw e; }
}

// También rechaza junctions/symlinks que escapen del repositorio.
async function contained(root, file) {
  let ancestor = file;
  while (!(await exists(ancestor))) ancestor = path.dirname(ancestor);
  const realRoot = await realpath(root);
  const realAncestor = await realpath(ancestor);
  const relative = path.relative(realRoot, realAncestor);
  if (relative.startsWith('..') || path.isAbsolute(relative)) throw new Error(`Path escapes repository: ${file}`);
  if (await exists(file)) {
    if ((await lstat(file)).isSymbolicLink()) throw new Error(`Symbolic links are not supported: ${file}`);
  }
}

export async function collectInventory(repositories) {
  const files = [], metadata = [];
  for (const repo of repositories) {
    const root = await realpath(repo.root);
    if (path.resolve(git(root, ['rev-parse', '--show-toplevel']).trim()).toLowerCase() !== root.toLowerCase()) throw new Error(`Not a repository root: ${root}`);
    let head = null;
    try { head = git(root, ['rev-parse', '--verify', 'HEAD']).trim(); } catch { /* Fixture/new repository without commits. */ }
    const branch = git(root, ['symbolic-ref', '--short', 'HEAD']).trim();
    metadata.push({ id: repo.id, root, head, branch, status: git(root, ['status', '--porcelain=v1', '-z']) });
    const names = new Set(git(root, ['ls-files', '-z', '--cached', '--others', '--exclude-standard']).split('\0').filter(Boolean));
    for (const relative of [...names].sort()) {
      const absolute = path.resolve(root, relative);
      if (!(await exists(absolute))) continue; // Existing deletions are part of the baseline.
      await contained(root, absolute);
      if (!(await lstat(absolute)).isFile()) continue;
      const bytes = await readFile(absolute);
      files.push({ repository: repo.id, path: relative.replaceAll('\\', '/'), absolute, hash: hash(bytes), size: bytes.length, ...(relative.endsWith('.md') ? { content: bytes.toString('utf8') } : {}) });
    }
  }
  return { repositories: metadata, files };
}

export async function assertUnchanged(before, repositories) {
  const after = await collectInventory(repositories);
  const fingerprint = inventory => JSON.stringify({
    repositories: inventory.repositories,
    files: inventory.files.map(({ repository, path, hash }) => ({ repository, path, hash }))
  });
  if (fingerprint(before) !== fingerprint(after)) throw new Error('Workspace changed during analysis. Start a new run; existing work was preserved.');
}

export function validateSchema(value, schema, location = '$') {
  const actual = value === null ? 'null' : Array.isArray(value) ? 'array' : typeof value;
  const allowed = Array.isArray(schema.type) ? schema.type : [schema.type];
  if (schema.type && !allowed.includes(actual)) throw new Error(`Invalid response type at ${location}: ${actual}`);
  if (schema.enum && !schema.enum.includes(value)) throw new Error(`Invalid response value at ${location}`);
  if (actual === 'object') {
    for (const key of schema.required ?? []) if (!Object.hasOwn(value, key)) throw new Error(`Missing response property: ${location}.${key}`);
    for (const [key, child] of Object.entries(value)) {
      const definition = schema.properties?.[key];
      if (!definition && schema.additionalProperties === false) throw new Error(`Unexpected response property: ${location}.${key}`);
      if (definition) validateSchema(child, definition, `${location}.${key}`);
    }
  }
  if (actual === 'array') value.forEach((child, i) => validateSchema(child, schema.items, `${location}[${i}]`));
}

function prose(text) {
  let fence = null;
  return text.split(/\r?\n/).map(line => {
    const marker = /^\s{0,3}(`{3,}|~{3,})/.exec(line)?.[1];
    if (marker && !fence) { fence = marker[0]; return ''; }
    if (marker && marker[0] === fence) { fence = null; return ''; }
    return fence ? '' : line;
  }).join('\n');
}

function anchors(text) {
  const found = new Set(), repeated = new Map();
  for (const line of prose(text).split('\n')) {
    const title = /^#{1,6}\s+(.+?)\s*#*\s*$/.exec(line)?.[1];
    if (!title) continue;
    const base = title.toLowerCase().replace(/<[^>]*>/g, '').replace(/[^\p{L}\p{N}_ -]/gu, '').replaceAll(' ', '-');
    const count = repeated.get(base) ?? 0;
    repeated.set(base, count + 1);
    found.add(count ? `${base}-${count}` : base);
  }
  for (const m of text.matchAll(/(?:id|name)=["']([^"']+)["']/g)) found.add(m[1]);
  return found;
}

export async function checkDocuments(documents, repositories, deleted = new Set()) {
  const issues = [], anchorIndex = new Map([...documents].map(([file, text]) => [file, anchors(text)]));
  for (const [file, text] of documents) {
    const body = prose(text), targets = [];
    for (const m of body.matchAll(/!?\[[^\]]*\]\(([^)]+)\)/g)) targets.push(m[1]);
    // Reference definitions include both explicit and shortcut reference links.
    for (const m of body.matchAll(/^\s{0,3}\[[^\]]+\]:\s*(.+)$/gm)) targets.push(m[1]);
    for (let target of targets) {
      target = target.trim().replace(/\s+["'][^"']*["']$/, '');
      if (target.startsWith('<') && target.endsWith('>')) target = target.slice(1, -1);
      if (/^[a-z][a-z0-9+.-]*:/i.test(target) || target.startsWith('//') || /[{}]/.test(target)) continue;
      const [destination, fragment] = target.split('#', 2);
      let absolute, anchor;
      try { absolute = destination ? path.resolve(path.dirname(file), decodeURIComponent(destination)) : file; anchor = fragment && decodeURIComponent(fragment); }
      catch { issues.push({ file, kind: 'invalid-link', target }); continue; }
      if (deleted.has(absolute) || (!documents.has(absolute) && !(await exists(absolute)))) issues.push({ file, kind: 'missing', target });
      else if (anchor && absolute.endsWith('.md') && !anchorIndex.get(absolute)?.has(anchor)) issues.push({ file, kind: 'anchor', target });
    }
    if (path.basename(file) !== 'AGENTS.md') continue;
    const sibling = path.join(path.dirname(file), 'CLAUDE.md');
    if (!documents.get(sibling)?.includes('@AGENTS.md')) issues.push({ file, kind: 'instructions-pair', target: sibling });
    const repo = repositories.find(r => !path.relative(r.root, file).startsWith('..'));
    if (repo) {
      const relative = path.relative(repo.root, file).replaceAll('\\', '/');
      if (relative !== 'AGENTS.md' && !documents.get(path.join(repo.root, repo.instructionMap))?.includes(relative)) issues.push({ file, kind: 'instructions-map', target: relative });
    }
  }
  return issues;
}

function allowedPath(relative) {
  if (/[\\:<>"|?*\x00-\x1f]/.test(relative) || path.isAbsolute(relative)) return false;
  const segments = relative.split('/');
  if (segments.some(x => !x || x === '.' || x === '..' || /[. ]$/.test(x) || /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(x))) return false;
  if (!relative.endsWith('.md')) return false;
  if (['README.md', 'AGENTS.md', 'CLAUDE.md'].includes(relative)) return true;
  if (relative.startsWith('docs/')) return true;
  return /^(src|tests|design-lab|\.github)\/.+\/(AGENTS|CLAUDE)\.md$/.test(relative);
}

export async function prepareChanges(changes, inventory, repositories) {
  const prepared = [], seen = new Set();
  for (const change of changes) {
    const repo = repositories.find(x => x.id === change.repository);
    if (!repo || !allowedPath(change.path)) throw new Error(`Disallowed documentation path: ${change.repository}/${change.path}`);
    const key = `${change.repository}/${change.path}`.toLowerCase();
    if (seen.has(key)) throw new Error(`Duplicate change: ${key}`);
    seen.add(key);
    if (!change.reason?.trim() || !change.evidence?.length || change.evidence.some(x => !x.trim())) throw new Error(`Change requires reason and evidence: ${key}`);
    const absolute = path.resolve(repo.root, change.path);
    await contained(repo.root, absolute);
    const previous = inventory.files.find(x => x.repository === change.repository && x.path === change.path);
    if (change.action === 'create') {
      if (previous || await exists(absolute)) throw new Error(`Create would overwrite an existing file: ${key}`);
    } else if (change.action === 'update') {
      if (!previous) throw new Error(`Update target does not exist: ${key}`);
    } else if (change.action === 'delete') {
      if (!previous || !/^docs\/(plans|specs|design)\//.test(change.path) || /\/(README|AGENTS|CLAUDE)\.md$/.test(change.path)) throw new Error(`Protected or missing deletion target: ${key}`);
      let original;
      try { original = git(repo.root, ['show', `HEAD:${change.path}`]); } catch { throw new Error(`Deletion requires a recoverable original in Git: ${key}`); }
      // Normalize line endings only: local decisions must not disappear with a deletion.
      if (original.replaceAll('\r\n', '\n') !== previous.content.replaceAll('\r\n', '\n')) throw new Error(`Deletion would lose uncommitted content not recoverable from Git: ${key}`);
      if (change.content !== null) throw new Error(`Delete must have null content: ${key}`);
    } else throw new Error(`Unknown change action: ${change.action}`);
    if (change.action !== 'delete' && (typeof change.content !== 'string' || !change.content.trim())) throw new Error(`Documentation content must not be empty: ${key}`);
    prepared.push({ ...change, absolute, beforeHash: previous?.hash ?? null });
  }
  return prepared;
}

export function plannedDocuments(inventory, plan = []) {
  const documents = new Map(inventory.files.filter(x => x.path.endsWith('.md')).map(x => [x.absolute, x.content]));
  const deleted = new Set();
  for (const change of plan) {
    if (change.action === 'delete') { documents.delete(change.absolute); deleted.add(change.absolute); }
    else documents.set(change.absolute, change.content);
  }
  return { documents, deleted };
}

export async function applyChanges(plan, inventory, repositories, backupDirectory) {
  const { documents, deleted } = plannedDocuments(inventory, plan);
  const issues = await checkDocuments(documents, repositories, deleted);
  if (issues.length) throw new Error(`Planned documentation has invalid links/instructions (${issues.length}). No changes applied.`);
  await assertUnchanged(inventory, repositories);
  await mkdir(backupDirectory, { recursive: true });
  // Recovery data records the exact local text, including pre-existing edits.
  await writeFile(path.join(backupDirectory, 'originals.json'), JSON.stringify(plan.map(x => ({ repository: x.repository, path: x.path, original: inventory.files.find(f => f.absolute === x.absolute)?.content ?? null })), null, 2));
  const applied = [];
  try {
    for (const change of plan) {
      const repo = repositories.find(x => x.id === change.repository);
      await contained(repo.root, change.absolute);
      const currentHash = await exists(change.absolute) ? hash(await readFile(change.absolute)) : null;
      if (currentHash !== change.beforeHash) throw new Error(`File changed before writing: ${change.absolute}. Recovery: ${backupDirectory}`);
      if (change.action === 'delete') await unlink(change.absolute);
      else { await mkdir(path.dirname(change.absolute), { recursive: true }); await writeFile(change.absolute, change.content, 'utf8'); }
      applied.push({ repository: change.repository, path: change.path, action: change.action });
      await writeFile(path.join(backupDirectory, 'applied.json'), JSON.stringify(applied, null, 2));
    }
  } catch (error) {
    error.applied = applied;
    throw error;
  }
  return applied;
}
