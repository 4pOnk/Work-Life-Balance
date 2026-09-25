import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import net from 'node:net';
import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { once } from 'node:events';

const root = path.resolve('../.local');
await fs.mkdir(root, { recursive: true });
const profile = await fs.mkdtemp(path.join(root, 'signed-firefox-'));
const data = path.join(profile, 'isolated-data');
const identity = createHash('sha256').update(os.userInfo().username + '|' + data.toUpperCase()).digest('hex').toUpperCase().slice(0, 24);
const sockets = new Set();
let complete;
const received = new Promise(resolve => { complete = resolve; });
const server = net.createServer(socket => {
  sockets.add(socket);
  socket.on('close', () => sockets.delete(socket));
  let buffer = Buffer.alloc(0);
  socket.on('error', error => console.error(error.message));
  socket.on('data', chunk => {
    buffer = Buffer.concat([buffer, chunk]);
    while (buffer.length >= 4) {
      const length = buffer.readUInt32LE();
      if (length > 32768) { socket.destroy(); return; }
      if (buffer.length < length + 4) return;
      const message = JSON.parse(buffer.subarray(4, length + 4));
      buffer = buffer.subarray(length + 4);
      console.log('SIGNED_MESSAGE', JSON.stringify({ version: message.version, focused: message.focused, sequence: message.sequence }));
      const reply = Buffer.from(JSON.stringify({ version: 1, ok: true, status: 'connected', port: 47831, browserPath: false }));
      const header = Buffer.alloc(4); header.writeUInt32LE(reply.length);
      socket.write(Buffer.concat([header, reply]));
      complete();
    }
  });
});
server.listen('\\\\.\\pipe\\WorkLifeBalance-browser-' + identity);
await once(server, 'listening');
await fs.mkdir(path.join(profile, 'extensions'));
await fs.copyFile(path.resolve('../.artifacts/work-life-balance-firefox-0.2.0-signed.xpi'), path.join(profile, 'extensions/work-life-balance@local.invalid.xpi'));
const prefs = {
  'extensions.autoDisableScopes': 0,
  'extensions.enabledScopes': 15,
  'devtools.console.stdout.chrome': true,
  'devtools.console.stdout.content': true,
  'datareporting.healthreport.uploadEnabled': false,
  'toolkit.telemetry.enabled': false,
  'browser.newtabpage.enabled': false,
  'browser.shell.checkDefaultBrowser': false,
};
await fs.writeFile(path.join(profile, 'user.js'), Object.entries(prefs).map(([k,v]) => `user_pref(${JSON.stringify(k)}, ${JSON.stringify(v)});`).join('\n'));
console.log('Test profile:', profile);
const child = spawn('C:\\Program Files\\Mozilla Firefox\\firefox.exe', ['-no-remote', '-headless', '-profile', profile, 'about:blank'], {
  windowsHide: true, env: { ...process.env, WLB_NATIVE_DATA_DIR: data }, stdio: ['ignore', 'pipe', 'pipe'],
});
child.stdout.on('data', chunk => process.stdout.write(chunk));
child.stderr.on('data', chunk => process.stderr.write(chunk));
let timer;
try {
  await Promise.race([received, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Signed extension did not reach native pipe')), 25000); })]);
  console.log('PASS unchanged signed XPI -> registered NativeHost -> isolated test pipe');
} finally {
  clearTimeout(timer);
  const exited = once(child, 'exit');
  child.kill();
  await exited;
  for (const socket of sockets) socket.destroy();
  server.close();
}
