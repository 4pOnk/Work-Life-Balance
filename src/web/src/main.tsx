import { StrictMode, useCallback, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  Activity,
  ArrowUpRight,
  Check,
  CirclePause,
  Coffee,
  Focus,
  Moon,
  Play,
  Plus,
  Save,
  Search,
  Settings2,
  ShieldCheck,
  Trash2,
} from "lucide-react";
import { api, type ProcessEntry, type Settings, type Status } from "./api";
import "./style.css";
import { DashboardPanel } from "./DashboardPanel";
import { FirefoxPanel } from "./FirefoxPanel";
import { StoragePanel } from "./StoragePanel";
import { WorkSitesPanel } from "./WorkSitesPanel";

function duration(seconds: number) {
  const minutes = Math.floor(seconds / 60);
  return `${Math.floor(minutes / 60)} ч ${String(minutes % 60).padStart(2, "0")} мин`;
}

function App() {
  const [status, setStatus] = useState<Status>();
  const [processes, setProcesses] = useState<ProcessEntry[]>([]);
  const [draft, setDraft] = useState<Settings>();
  const [error, setError] = useState("");
  const [connectionError, setConnectionError] = useState("");
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [search, setSearch] = useState("");
  const [allProcesses, setAllProcesses] = useState(false);
  const [newProcess, setNewProcess] = useState("");
  const [ruleScope, setRuleScope] = useState("future");

  const refresh = useCallback(async () => {
    const [next, running] = await Promise.all([
      api<Status>("/status"),
      api<ProcessEntry[]>("/processes"),
    ]);
    setStatus(next);
    setProcesses(running);
    setDraft((previous) => previous ?? next.tracker.settings);
  }, []);

  useEffect(() => {
    let alive = true;
    let timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      try {
        await refresh();
        if (alive) setConnectionError("");
      } catch (e) {
        if (alive)
          setConnectionError(
            e instanceof Error ? e.message : "Нет связи с трекером.",
          );
      }
      if (alive)
        timer = setTimeout(() => {
          void poll();
        }, 2000);
    };
    void poll();
    return () => {
      alive = false;
      clearTimeout(timer);
    };
  }, [refresh]);

  async function action(operation: () => Promise<unknown>) {
    setBusy(true);
    setError("");
    setSaved(false);
    try {
      await operation();
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Операция не выполнена.");
    } finally {
      setBusy(false);
    }
  }

  async function setRules(workProcesses: string[]) {
    if (!status) return;
    const updated = { ...status.tracker.settings, workProcesses };
    await api(`/settings?scope=${ruleScope}`, "PUT", updated);
    setDraft((previous) =>
      previous ? { ...previous, workProcesses } : updated,
    );
  }

  function toggleProcess(executable: string) {
    if (!status) return;
    const names = status.tracker.settings.workProcesses;
    const exists = names.some(
      (name) => name.toLowerCase() === executable.toLowerCase(),
    );
    void action(() =>
      setRules(
        exists
          ? names.filter(
              (name) => name.toLowerCase() !== executable.toLowerCase(),
            )
          : [...names, executable],
      ),
    );
  }

  const state = status?.tracker.state;
  const today = status?.tracker.today;
  const away = state?.presence !== "active";
  const label = state?.paused
    ? "Учёт приостановлен"
    : away
      ? "Сейчас AFK"
      : "Учёт активен";
  const workNames = status?.tracker.settings.workProcesses ?? [];
  const isWork = (name: string) =>
    workNames.some((n) => n.toLowerCase() === name.toLowerCase());
  const filtered = processes.filter(
    (p) =>
      (allProcesses || p.hasWindow || p.active) &&
      p.executable.toLowerCase().includes(search.toLowerCase()),
  );
  const total = today
    ? today.workSeconds + today.restSeconds + today.afkSeconds
    : 0;

  return (
    <div className="app">
      <header className="topbar">
        <a className="brand" href="#">
          <span className="brand-icon">
            <Activity size={22} />
          </span>
          Work Life Balance
        </a>
        <span className="local">
          <ShieldCheck size={15} /> На этом компьютере
        </span>
      </header>
      <main>
        <div className="page-heading">
          <div>
            <div className="eyebrow">ЛИЧНОЕ ВРЕМЯ</div>
            <h1>Сегодня</h1>
            <p className="date">
              {new Date().toLocaleDateString("ru-RU", {
                weekday: "long",
                day: "numeric",
                month: "long",
              })}
            </p>
          </div>
          <a className="settings-link" href="#settings">
            <Settings2 size={17} /> Настройки
          </a>
        </div>
        {connectionError && (
          <div className="alert" role="alert">
            {connectionError}
          </div>
        )}
        {error && (
          <div className="alert" role="alert">
            {error}
          </div>
        )}
        {status?.tracker.error && (
          <div className="alert" role="alert">
            {status.tracker.error}
          </div>
        )}
        {!status ? (
          <div className="loading" role="status">
            Подключение к локальному трекеру…
          </div>
        ) : (
          <>
            <section className="live-row" aria-label="Текущая активность">
              <div
                className={`live-dot ${state?.paused ? "paused" : away ? "away" : ""}`}
              />
              <div className="live-text">
                <strong>{label}</strong>
                <span>
                  {state?.activity.executable === "unknown"
                    ? "Активное окно недоступно"
                    : state?.activity.executable}
                </span>
              </div>
              <div className="live-actions">
                <button
                  disabled={busy}
                  onClick={() => void action(() => api("/afk", "POST"))}
                >
                  <Moon size={16} />
                  {away && !state?.paused ? "Вернуться" : "Включить AFK"}
                </button>
                <button
                  className="icon-button"
                  disabled={busy}
                  title={
                    state?.paused ? "Возобновить учёт" : "Приостановить учёт"
                  }
                  aria-label={
                    state?.paused ? "Возобновить учёт" : "Приостановить учёт"
                  }
                  onClick={() => void action(() => api("/pause", "POST"))}
                >
                  {state?.paused ? (
                    <Play size={18} />
                  ) : (
                    <CirclePause size={18} />
                  )}
                </button>
              </div>
            </section>
            <section className="totals" aria-label="Итоги дня">
              <div className="metric">
                <span>
                  <Focus size={17} /> Работа
                </span>
                <strong>{duration(today!.workSeconds)}</strong>
                <small className="work-text">
                  {today!.workShare === null
                    ? "Нет активного времени"
                    : `${Math.round(today!.workShare * 100)}% активного времени`}
                </small>
              </div>
              <div className="metric">
                <span>
                  <Coffee size={17} /> Отдых
                </span>
                <strong>{duration(today!.restSeconds)}</strong>
                <small>Вне рабочих приложений</small>
              </div>
              <div className="metric">
                <span>
                  <Moon size={17} /> AFK
                </span>
                <strong>{duration(today!.afkSeconds)}</strong>
                <small>Отсутствие за компьютером</small>
              </div>
            </section>
            <div
              className="day-distribution"
              aria-label="Распределение времени за день"
            >
              {total > 0 ? (
                <>
                  <progress
                    className="work"
                    max={total}
                    value={today!.workSeconds}
                    aria-label="Работа"
                  />
                  <progress
                    className="rest"
                    max={total}
                    value={today!.restSeconds}
                    aria-label="Отдых"
                  />
                  <progress
                    className="afk"
                    max={total}
                    value={today!.afkSeconds}
                    aria-label="AFK"
                  />
                </>
              ) : (
                <span className="muted">Пока нет записанных интервалов</span>
              )}
            </div>
            <DashboardPanel
              today={today!.date}
              revision={status.tracker.serverTime}
              changed={refresh}
            />
            <section className="section" aria-labelledby="process-title">
              <div className="section-heading">
                <div>
                  <h2 id="process-title">Рабочие приложения</h2>
                  <p>{workNames.length} в списке работы</p>
                </div>
              </div>
              <label className="rule-scope">
                Изменения правил
                <select
                  value={ruleScope}
                  onChange={(e) => setRuleScope(e.target.value)}
                  aria-label="Область применения правил"
                >
                  <option value="future">Только с этого момента</option>
                  <option value="history">Также пересчитать историю</option>
                </select>
              </label>
              <div className="rules">
                {workNames.map((name) => (
                  <div className="rule" key={name}>
                    <span className="app-symbol">
                      <Focus size={17} />
                    </span>
                    <span>{name}</span>
                    <button
                      disabled={busy}
                      className="icon-button"
                      title={`Удалить ${name} из рабочих`}
                      aria-label={`Удалить ${name} из рабочих`}
                      onClick={() => toggleProcess(name)}
                    >
                      <Trash2 size={15} />
                    </button>
                  </div>
                ))}
                {workNames.length === 0 && (
                  <p className="muted">Рабочих приложений пока нет</p>
                )}
              </div>
              <form
                className="add-process"
                onSubmit={(event) => {
                  event.preventDefault();
                  void action(async () => {
                    await setRules([...workNames, newProcess.trim()]);
                    setNewProcess("");
                  });
                }}
              >
                <input
                  aria-label="Имя рабочего процесса"
                  placeholder="приложение.exe"
                  value={newProcess}
                  onChange={(e) => setNewProcess(e.target.value)}
                  maxLength={128}
                  required
                />
                <button disabled={busy || !newProcess.trim()} type="submit">
                  <Plus size={16} />
                  Добавить
                </button>
              </form>
              <div className="process-toolbar">
                <h3>
                  Сейчас запущены <span>{filtered.length}</span>
                </h3>
                <div className="filters">
                  <label className="checkbox-label">
                    <input
                      type="checkbox"
                      checked={allProcesses}
                      onChange={(e) => setAllProcesses(e.target.checked)}
                    />
                    Все процессы
                  </label>
                  <div className="search">
                    <Search size={16} />
                    <input
                      aria-label="Поиск процессов"
                      placeholder="Найти приложение"
                      value={search}
                      onChange={(e) => setSearch(e.target.value)}
                    />
                  </div>
                </div>
              </div>
              <div className="process-list">
                {filtered.map((process) => (
                  <div className="process-row" key={process.executable}>
                    <span
                      className={`process-icon ${process.active ? "current" : ""}`}
                    >
                      <Activity size={17} />
                    </span>
                    <div className="process-name">
                      <strong>{process.executable}</strong>
                      <span>
                        {process.active
                          ? "Активное окно"
                          : `${process.instances} экз.`}
                      </span>
                    </div>
                    <span
                      className={
                        isWork(process.executable) &&
                        process.executable.toLowerCase() !== "firefox.exe"
                          ? "category work-text"
                          : "category"
                      }
                    >
                      {isWork(process.executable) &&
                      process.executable.toLowerCase() !== "firefox.exe"
                        ? "Работа"
                        : process.executable.toLowerCase() === "firefox.exe"
                          ? "По сайту"
                          : "Отдых"}
                    </span>
                    <button
                      className={`icon-button ${isWork(process.executable) ? "selected" : ""}`}
                      disabled={
                        busy ||
                        process.executable.toLowerCase() === "firefox.exe"
                      }
                      title={
                        process.executable.toLowerCase() === "firefox.exe"
                          ? "Для Firefox используются правила сайтов"
                          : isWork(process.executable)
                            ? "Удалить из рабочих"
                            : "Добавить в рабочие"
                      }
                      aria-label={`${isWork(process.executable) ? "Удалить" : "Добавить"} ${process.executable}`}
                      onClick={() => toggleProcess(process.executable)}
                    >
                      {isWork(process.executable) ? (
                        <Check size={17} />
                      ) : (
                        <Plus size={17} />
                      )}
                    </button>
                  </div>
                ))}
                {filtered.length === 0 && (
                  <div className="empty">Приложения не найдены</div>
                )}
              </div>
            </section>
            <section
              className="section"
              id="settings"
              aria-labelledby="settings-title"
            >
              <div className="section-heading">
                <h2 id="settings-title">Настройки учёта</h2>
                <Settings2 size={19} />
              </div>
              {draft && (
                <form
                  onSubmit={(event) => {
                    event.preventDefault();
                    void action(async () => {
                      await api("/settings", "PUT", {
                        ...draft,
                        workProcesses: workNames,
                        workSites: status.tracker.settings.workSites,
                        retentionDays: status.tracker.settings.retentionDays,
                      });
                      setSaved(true);
                    });
                  }}
                >
                  <div className="setting-row">
                    <label htmlFor="auto-afk">Автоматический AFK</label>
                    <input
                      className="switch"
                      id="auto-afk"
                      type="checkbox"
                      checked={draft.automaticAfk}
                      onChange={(e) =>
                        setDraft({ ...draft, automaticAfk: e.target.checked })
                      }
                    />
                  </div>
                  <div className="setting-row">
                    <label htmlFor="afk-minutes">Время без ввода до AFK</label>
                    <div className="with-unit">
                      <input
                        id="afk-minutes"
                        type="number"
                        min="0.05"
                        max="10080"
                        step="any"
                        required
                        value={draft.afkMinutes}
                        onChange={(e) =>
                          setDraft({
                            ...draft,
                            afkMinutes: Number(e.target.value),
                          })
                        }
                      />
                      <span>мин</span>
                    </div>
                  </div>
                  <div className="setting-row">
                    <label htmlFor="hotkey">Переключение AFK</label>
                    <input
                      id="hotkey"
                      value={draft.hotkey}
                      onChange={(e) =>
                        setDraft({ ...draft, hotkey: e.target.value })
                      }
                      required
                      maxLength={80}
                    />
                  </div>
                  {status.hotkeyError && (
                    <div className="alert" role="alert">
                      {status.hotkeyError}
                    </div>
                  )}
                  <div className="setting-row">
                    <label htmlFor="port">Порт интерфейса</label>
                    <input
                      id="port"
                      type="number"
                      min="1024"
                      max="65535"
                      step="1"
                      value={draft.port}
                      onChange={(e) =>
                        setDraft({ ...draft, port: Number(e.target.value) })
                      }
                      required
                    />
                  </div>
                  <div className="setting-row">
                    <label htmlFor="browser-path">Адреса страниц Firefox</label>
                    <select
                      id="browser-path"
                      value={draft.browserPath ? "path" : "domain"}
                      onChange={(e) =>
                        setDraft({
                          ...draft,
                          browserPath: e.target.value === "path",
                        })
                      }
                    >
                      <option value="path">Домен и путь</option>
                      <option value="domain">Только домен</option>
                    </select>
                  </div>
                  {status.activePort !== status.tracker.settings.port && (
                    <p className="notice">
                      Новый порт применится после перезапуска приложения.
                    </p>
                  )}
                  <div className="form-footer">
                    <span role="status">
                      {saved ? "Настройки сохранены" : ""}
                    </span>
                    <button className="primary" disabled={busy} type="submit">
                      <Save size={16} />
                      Сохранить
                    </button>
                  </div>
                </form>
              )}
            </section>
            <FirefoxPanel status={status.firefox} />
            <WorkSitesPanel
              settings={status.tracker.settings}
              changed={refresh}
            />
            <StoragePanel status={status} changed={refresh} />
          </>
        )}
        <footer>
          <span>
            <ShieldCheck size={14} /> Данные хранятся локально
          </span>
          <span>
            v0.4 <ArrowUpRight size={14} />
          </span>
        </footer>
      </main>
    </div>
  );
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
