const { test } = require('node:test');
const assert = require('node:assert/strict');
require('../firefox/collector.js');

function fake(overrides = {}) {
  const window = { id: 7, focused: true, incognito: false, ...overrides.window };
  return {
    windows: { getLastFocused: async () => window, get: async () => ({ ...window, ...overrides.confirmed }) },
    tabs: { query: async options => {
      assert.deepEqual(options, { active: true, windowId: 7 });
      return [{ id: 9, url: 'https://user:secret@example.com/docs?q=secret#token', title: 'Documentation', ...overrides.tab }];
    } },
  };
}
test('only active tab of focused window and stripped credentials/query/fragment', async () => {
  const value = await globalThis.WlbCollector.snapshot(fake(), true);
  assert.equal(value.url, 'https://example.com/docs');
  assert.equal(value.tabId, 9);
});
test('domain-only preference', async () => {
  assert.equal((await globalThis.WlbCollector.snapshot(fake(), false)).url, 'https://example.com/');
});
test('private window sends no title or URL', async () => {
  const value = await globalThis.WlbCollector.snapshot(fake({ window: { incognito: true } }), true);
  assert.equal(value.focused, false); assert.equal(value.url, undefined); assert.equal(value.title, undefined);
});
test('background browser sends no page', async () => {
  assert.equal((await globalThis.WlbCollector.snapshot(fake({ window: { focused: false } }), true)).focused, false);
});
test('focus lost during async tab query discards page', async () => {
  assert.equal((await globalThis.WlbCollector.snapshot(fake({ confirmed: { focused: false } }), true)).focused, false);
});
test('internal URLs are not stored', async () => {
  const value = await globalThis.WlbCollector.snapshot(fake({ tab: { url: 'file:///C:/secret.txt', title: 'secret' } }), true);
  assert.equal(value.url, null); assert.equal(value.title, null);
});
