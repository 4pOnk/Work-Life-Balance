/* global browser */
setTimeout(async () => {
  try {
    const response = await browser.runtime.sendNativeMessage('com.worklifebalance.tracker', { version: 0 });
    console.log('WLB_PROBE_OK ' + JSON.stringify(response));
  } catch (error) {
    console.error('WLB_PROBE_ERROR ' + error.message);
  }
}, 1000);
