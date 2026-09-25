import path from 'node:path';
import webExt from 'web-ext';

// No tracker uses this directory: an offline reply still proves discovery and launch.
process.env.WLB_NATIVE_DATA_DIR = path.resolve('../.local/native-probe-offline');
const runner = await webExt.cmd.run({
  sourceDir: path.resolve('tests/native-probe'),
  firefox: 'C:\\Program Files\\Mozilla Firefox\\firefox.exe',
  noInput: true, noReload: true, args: ['-headless'], startUrl: ['about:blank'],
  pref: {
    'devtools.console.stdout.chrome': true,
    'devtools.console.stdout.content': true,
    'datareporting.healthreport.uploadEnabled': false,
    'toolkit.telemetry.enabled': false,
    'browser.newtabpage.enabled': false,
    'browser.shell.checkDefaultBrowser': false,
  },
});
const child = runner.extensionRunners[0].runningInfo.firefox;
try {
  await new Promise((resolve, reject) => {
    let buffer = '';
    const timeout = setTimeout(() => reject(new Error('Native probe timed out')), 15000);
    const consume = chunk => {
      buffer += chunk.toString();
      process.stdout.write(chunk);
      if (buffer.includes('WLB_PROBE_OK')) { clearTimeout(timeout); resolve(); }
      else if (buffer.includes('WLB_PROBE_ERROR')) { clearTimeout(timeout); reject(new Error('Firefox could not find or launch the native host')); }
    };
    child.stdout.on('data', consume);
    child.stderr.on('data', consume);
  });
} finally {
  await runner.exit();
}
