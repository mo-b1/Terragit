import { spawn } from 'node:child_process';
import { mkdtemp, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import assert from 'node:assert/strict';

// Run against the isolated fixture server, never a user's live world.
const url = process.argv[2] || 'http://127.0.0.1:17840/';
const directory = await mkdtemp(join(tmpdir(), 'terragit-browser-'));
const browser = spawn(process.env.TERRAGIT_BROWSER || '/opt/brave-bin/brave', [
  '--headless', '--disable-gpu', '--no-first-run', '--disable-extensions',
  '--disable-background-networking', '--remote-debugging-port=0',
  '--user-data-dir=' + directory, 'about:blank'
], { stdio: ['ignore', 'ignore', 'pipe'] });
let socket;
const errors = [];
try {
  const endpoint = await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('Browser startup timed out')), 15000);
    browser.on('error', reject);
    browser.stderr.on('data', data => {
      const match = data.toString().match(/DevTools listening on (ws:\/\/[^\s]+)/);
      if (match) { clearTimeout(timeout); resolve(match[1]); }
    });
  });
  socket = new WebSocket(endpoint);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let sequence = 0, session;
  const pending = new Map();
  socket.onmessage = event => {
    const data = JSON.parse(event.data);
    if (data.id) {
      const task = pending.get(data.id);
      pending.delete(data.id);
      if (data.error) task.reject(new Error(JSON.stringify(data.error)));
      else task.resolve(data.result);
    }
    if (data.method === 'Runtime.exceptionThrown') errors.push(data.params.exceptionDetails);
  };
  function send(method, params = {}, attached = true) {
    return new Promise((resolve, reject) => {
      const id = ++sequence;
      pending.set(id, { resolve, reject });
      socket.send(JSON.stringify({ id, method, params, ...(attached && session ? { sessionId: session } : {}) }));
    });
  }
  const target = await send('Target.createTarget', { url: 'about:blank' }, false);
  session = (await send('Target.attachToTarget', { targetId: target.targetId, flatten: true }, false)).sessionId;
  await send('Runtime.enable');
  await send('Page.enable');
  async function evaluate(expression) {
    const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  }
  async function until(expression) {
    for (let attempt = 0; attempt < 100; attempt++) {
      if (await evaluate(expression)) return;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error('Timed out: ' + expression);
  }
  const click = id => evaluate('document.getElementById(' + JSON.stringify(id) + ').click()');
  async function fill(id, value) {
    await evaluate('document.getElementById(' + JSON.stringify(id) + ').value = ' + JSON.stringify(value));
  }
  async function screenshot(name) {
    const shot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
    await writeFile(join(directory, name), Buffer.from(shot.data, 'base64'));
  }
  await send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });
  await send('Page.navigate', { url });
  await until("document.getElementById('repo-name')?.textContent === 'Fernwood'");
  assert.equal(await evaluate("document.querySelectorAll('.repo-link').length"), 2);
  await screenshot('desktop.png');
  await click('tab-branches');
  await fill('branch-name', 'browser-test');
  await evaluate("document.getElementById('branch-form').requestSubmit()");
  await until("!busy && document.getElementById('branches-list').textContent.includes('browser-test')");
  assert.equal(await evaluate("document.getElementById('current-branch').textContent"), 'Main');
  await evaluate("document.querySelector('#branches-list button').click()");
  await until("document.getElementById('confirm-dialog').open");
  await evaluate("document.getElementById('confirm-dialog').close('cancel')");
  assert.ok(await evaluate("document.getElementById('branches-list').textContent.includes('browser-test')"));
  await evaluate("document.querySelector('#branches-list button').click()");
  await click('confirm-yes');
  await until("!busy && !document.getElementById('branches-list').textContent.includes('browser-test')");
  await click('tab-history');
  await click('diff');
  await until("!busy && document.getElementById('notice').textContent.includes('No differences')");
  await evaluate("document.querySelectorAll('.repo-link')[1].click()");
  assert.equal(await evaluate("document.getElementById('pull').disabled"), true);
  await click('tab-settings');
  await fill('rename', 'Copper coast <img src=x onerror=alert(1)>');
  await evaluate("document.getElementById('rename-form').requestSubmit()");
  await until("!busy && document.getElementById('repo-name').textContent.includes('<img')");
  assert.equal(await evaluate("document.querySelectorAll('#repo-name img').length"), 0);
  await fill('rename', 'Copper coast');
  await evaluate("document.getElementById('rename-form').requestSubmit()");
  await until("!busy && document.getElementById('repo-name').textContent === 'Copper coast'");
  await click('tab-history');
  await evaluate("document.querySelectorAll('.repo-link')[0].click()");
  await evaluate("document.querySelector('.row-actions button').click()");
  await fill('edit-message', 'A foundation for bigger things');
  await evaluate("document.getElementById('message-form').requestSubmit()");
  await until("!busy && document.getElementById('commits').textContent.includes('A foundation')");
  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  await screenshot('mobile.png');
  assert.equal(await evaluate("document.documentElement.scrollWidth <= window.innerWidth"), true, 'mobile layout must not overflow');
  await send('Page.reload');
  await until("document.getElementById('commits')?.textContent.includes('A foundation')");
  assert.equal(errors.length, 0, JSON.stringify(errors));
  console.log('Browser checks passed: navigation, branch create/delete/cancel, diff, inactive-world guards, rename, text escaping, message edits, persistence, mobile layout.');
  console.log('Screenshots: ' + directory);
} finally {
  socket?.close();
  browser.kill('SIGTERM');
}
