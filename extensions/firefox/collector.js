/* Shared with dependency-free tests; no page content is read. */
(function (root) {
  async function snapshot(browser, includePath) {
    const window = await browser.windows.getLastFocused();
    if (!window.focused || window.incognito) return { focused: false, private: !!window.incognito, windowId: -1, tabId: -1 };
    const [tab] = await browser.tabs.query({ active: true, windowId: window.id });
    const confirmed = await browser.windows.get(window.id);
    if (!tab || !confirmed.focused || confirmed.incognito || tab.incognito) return { focused: false, private: true, windowId: -1, tabId: -1 };
    let url = null, title = null;
    try {
      const parsed = new URL(tab.url);
      if (parsed.protocol === 'http:' || parsed.protocol === 'https:') {
        parsed.username = ''; parsed.password = ''; parsed.search = ''; parsed.hash = '';
        if (!includePath) parsed.pathname = '/';
        url = parsed.href;
        title = (tab.title || '').slice(0, 512);
      }
    } catch { /* Restricted or transient page. */ }
    return { focused: true, private: false, windowId: window.id, tabId: tab.id, url, title };
  }
  root.WlbCollector = { snapshot };
})(globalThis);
