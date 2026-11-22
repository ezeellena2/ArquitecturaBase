import assert from 'node:assert/strict';
import { test } from 'node:test';
import { spawn } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';

const here = path.dirname(fileURLToPath(import.meta.url));
const script = path.join(here, '..', 'carril.ps1');
const work = path.join(here, 'fixtures', 'work.mjs');

// Cada test usa su propio carril (nombre y carpeta de estado), así no espera a las corridas reales de la PC.
function lane(t) {
  const dir = mkdtempSync(path.join(tmpdir(), 'carril-test-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const env = { ...process.env, AB_CARRIL_NAME: `carril-test-${randomUUID()}`, AB_CARRIL_STATE: dir };
  delete env.AB_CARRIL_OWNER;
  const log = path.join(dir, 'work.log');
  writeFileSync(log, '');
  return { dir, env, log };
}

function run(env, args) {
  const child = spawn('pwsh', ['-NoProfile', '-NonInteractive', '-File', script, ...args], { env, windowsHide: true });
  let output = '';
  child.stdout.on('data', (chunk) => { output += chunk; });
  child.stderr.on('data', (chunk) => { output += chunk; });
  const done = new Promise((resolve) => child.on('close', (code) => resolve({ code, output })));
  return { child, done, output: () => output };
}

function holderPid(dir, env) {
  return JSON.parse(readFileSync(path.join(dir, `${env.AB_CARRIL_NAME}.json`), 'utf8')).pid;
}

function job(log, label, ms, exitCode = 0) {
  return ['--', process.execPath, work, log, label, String(ms), String(exitCode)];
}

async function until(condition, ms = 15000) {
  const end = Date.now() + ms;
  while (!condition()) {
    if (Date.now() > end) throw new Error('Timed out waiting for the condition');
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
}

function events(log) {
  return readFileSync(log, 'utf8').trim().split('\n').filter(Boolean).map((line) => {
    const [kind, label, at] = line.split(' ');
    return { kind, label, at: Number(at) };
  });
}

test('two heavy runs take turns: the second starts after the first ends', async (t) => {
  const { env, log, dir } = lane(t);
  const first = run(env, job(log, 'A', 1500));
  await until(() => readFileSync(log, 'utf8').includes('start A'));
  const second = run(env, job(log, 'B', 0));

  const [a, b] = await Promise.all([first.done, second.done]);

  assert.equal(a.code, 0, a.output);
  assert.equal(b.code, 0, b.output);
  assert.deepEqual(events(log).map((e) => `${e.kind} ${e.label}`), ['start A', 'end A', 'start B', 'end B']);
  const endA = events(log).find((e) => e.kind === 'end' && e.label === 'A').at;
  const startB = events(log).find((e) => e.kind === 'start' && e.label === 'B').at;
  assert.ok(startB >= endA, `B started at ${startB}, before A ended at ${endA}`);
  assert.match(b.output, /waiting/i);
  assert.deepEqual(readdirSync(dir).filter((name) => name.endsWith('.json')), []);
});

test('the exit code of the command comes back', async (t) => {
  const { env, log } = lane(t);

  const { code, output } = await run(env, job(log, 'X', 0, 7)).done;

  assert.equal(code, 7, output);
});

test('the command gets its arguments as written, another -- included, and its output reaches the caller', async (t) => {
  const { env } = lane(t);
  const args = path.join(here, 'fixtures', 'args.mjs');

  const { code, output } = await run(env, ['--', process.execPath, args, 'test', '--', '--filter-class', 'Y Z', '-c', 'Release']).done;

  assert.equal(code, 0, output);
  assert.match(output, /^\["test","--","--filter-class","Y Z","-c","Release"\]$/m);
});

test('a heavy run started inside the lane does not wait for itself', async (t) => {
  const { env, log } = lane(t);
  const nested = ['--', 'pwsh', '-NoProfile', '-NonInteractive', '-File', script, ...job(log, 'N', 0)];

  const { code, output } = await run(env, nested).done;

  assert.equal(code, 0, output);
  assert.deepEqual(events(log).map((e) => `${e.kind} ${e.label}`), ['start N', 'end N']);
});

// Se corta el proceso anotado en el estado: el pwsh de la tienda puede arrancar detrás de un alias intermedio.
test('a holder that dies hands the lane to the run that was waiting', async (t) => {
  const { env, log, dir } = lane(t);
  const holder = run(env, job(log, 'A', 6000));
  await until(() => readFileSync(log, 'utf8').includes('start A'));
  const waiting = run(env, job(log, 'B', 0));
  await until(() => /waiting/i.test(waiting.output()));

  process.kill(holderPid(dir, env));
  const killedAt = Date.now();
  const b = await waiting.done;
  await holder.done;

  assert.equal(b.code, 0, b.output);
  assert.match(b.output, /abandoned/i);
  const startB = events(log).find((e) => e.kind === 'start' && e.label === 'B').at;
  assert.ok(startB - killedAt < 5000, `B started ${startB - killedAt} ms after the holder died`);
});

// Sin nadie esperando, Windows borra el mutex junto con su último dueño: la corrida siguiente lo crea de nuevo.
test('a holder that dies with nobody waiting leaves the lane free', async (t) => {
  const { env, log, dir } = lane(t);
  const holder = run(env, job(log, 'A', 4000));
  await until(() => readFileSync(log, 'utf8').includes('start A'));
  process.kill(holderPid(dir, env));

  const next = await run(env, job(log, 'C', 0)).done;
  await holder.done;

  assert.equal(next.code, 0, next.output);
  assert.doesNotMatch(next.output, /waiting/i);
});

test('status says the lane is free, or who has it', async (t) => {
  const { env, log } = lane(t);

  const free = await run(env, ['-Status']).done;
  assert.equal(free.code, 0, free.output);
  assert.match(free.output, /free/i);

  const holder = run(env, job(log, 'A', 6000));
  await until(() => readFileSync(log, 'utf8').includes('start A'));
  const busy = await run(env, ['-Status']).done;
  await holder.done;

  assert.equal(busy.code, 0, busy.output);
  assert.match(busy.output, /busy/i);
  assert.match(busy.output, /work\.mjs/);
});

test('without a command it explains how to call it', async (t) => {
  const { env } = lane(t);

  const { code, output } = await run(env, []).done;

  assert.equal(code, 2, output);
  assert.match(output, /usage/i);
  assert.ok(!existsSync(path.join(env.AB_CARRIL_STATE, `${env.AB_CARRIL_NAME}.json`)));
});
