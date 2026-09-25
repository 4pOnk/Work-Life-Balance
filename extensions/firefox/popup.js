/* global browser */
const labels = {
  connected: 'Трекер подключён. Данные остаются на компьютере.',
  'tracker-offline': 'Трекер не запущен.',
  'bridge-unavailable': 'Локальный помощник не установлен или недоступен.',
  'waiting-for-window': 'Ожидание доступного окна Firefox.',
  incompatible: 'Версии трекера и расширения несовместимы.',
  rejected: 'Трекер отклонил устаревшее сообщение.',
  connecting: 'Подключение…',
};
browser.runtime.sendMessage({ type: 'status' }).then(state => {
  document.getElementById('status').textContent = labels[state.status] || 'Нет связи с трекером.';
  const open = document.getElementById('open');
  if (state.address && /^http:\/\/127\.0\.0\.1:\d+$/.test(state.address)) {
    open.disabled = false;
    open.addEventListener('click', () => browser.tabs.create({ url: state.address }));
  }
});
