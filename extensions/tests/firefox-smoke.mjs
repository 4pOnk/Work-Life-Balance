import path from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import webExt from 'web-ext';

if (!process.argv[2]) throw new Error('Pass the isolated tracker data directory. Register the native host first.');
process.env.WLB_NATIVE_DATA_DIR = path.resolve(process.argv[2]);
const runner = await webExt.cmd.run({
  sourceDir: path.resolve('firefox'),
  artifactsDir: path.resolve('../.local/firefox-webext'),
  firefox: 'C:\\Program Files\\Mozilla Firefox\\firefox.exe',
  noInput: true, noReload: true,
  args: ['-headless'],
  startUrl: ['http://127.0.0.1:47831'],
  pref: { 'datareporting.healthreport.uploadEnabled': false, 'toolkit.telemetry.enabled': false,
    'browser.newtabpage.enabled': false, 'browser.shell.checkDefaultBrowser': false },
});
try {
  let connected = false;
  for (let i = 0; i < 50; i++) {
    const response = await fetch('http://127.0.0.1:47831/api/v1/status');
    const status = await response.json();
    if (status.firefox.connected) { connected = true; break; }
    await sleep(200);
  }
  if (!connected) throw new Error('Real Firefox did not connect through native messaging.');
  console.log('PASS real Firefox temporary-profile extension -> registered native host -> tracker');
} finally { await runner.exit(); }
