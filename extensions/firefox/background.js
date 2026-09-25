/* global browser, WlbCollector */
let port = null;
let sequence = 0;
let generation = 0;
let inFlight = false;
let collecting = false;
let includePath = true;
let timer;
let responseTimer;
let state = { status: 'connecting', address: null };

function showStatus(value) {
  state = value;
  browser.browserAction.setBadgeText({ text: value.status === 'connected' ? '' : '!' });
  browser.browserAction.setBadgeBackgroundColor({ color: '#a15358' });
}

function connect() {
  if (port) return;
  try {
    const connection = browser.runtime.connectNative('com.worklifebalance.tracker');
    port = connection;
    connection.onMessage.addListener(message => {
      if (port !== connection) return;
      clearTimeout(responseTimer); inFlight = false;
      if (message.version !== 1) { showStatus({ status: 'incompatible', address: null }); return; }
      includePath = message.browserPath !== false;
      showStatus({ status: message.status, address: message.port ? `http://127.0.0.1:${message.port}` : null });
    });
    connection.onDisconnect.addListener(() => {
      if (port !== connection) return;
      port = null; inFlight = false; generation++;
      clearTimeout(responseTimer);
      showStatus({ status: 'bridge-unavailable', address: null });
    });
  } catch { showStatus({ status: 'bridge-unavailable', address: null }); }
}

async function collect() {
  if (collecting || inFlight) return;
  connect();
  if (!port) return;
  collecting = true;
  const revision = generation;
  try {
    const value = await WlbCollector.snapshot(browser, includePath);
    if (revision !== generation || !port) return;
    port.postMessage({ version: 1, sequence: ++sequence, observedAt: Date.now(), ...value });
    inFlight = true;
    responseTimer = setTimeout(() => { if (port) port.disconnect(); }, 6000);
  } catch { showStatus({ status: 'waiting-for-window', address: state.address }); }
  finally { collecting = false; }
}

function changed() {
  generation++;
  clearTimeout(timer);
  timer = setTimeout(() => { void collect(); }, 0);
}
browser.tabs.onActivated.addListener(changed);
browser.tabs.onRemoved.addListener(changed);
browser.tabs.onAttached.addListener(changed);
browser.tabs.onDetached.addListener(changed);
browser.tabs.onUpdated.addListener((_id, info, tab) => { if (tab.active && (info.url || info.title || info.status)) changed(); });
browser.windows.onFocusChanged.addListener(changed);
browser.windows.onRemoved.addListener(changed);
browser.runtime.onMessage.addListener(message => {
  if (message.type === 'status') return Promise.resolve(state);
  return undefined;
});
setInterval(() => { void collect(); }, 1000);
void collect();
