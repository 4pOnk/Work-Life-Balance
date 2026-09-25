import { useEffect, useRef, useState } from "react";
import { ArrowLeft, ArrowRight, Check, Pencil, Undo2, X } from "lucide-react";
import { api, type Category, type DayReport, type Slice } from "./api";
import "./history.css";

const categories: Record<Category, string> = {
  work: "Работа",
  rest: "Отдых",
  afk: "AFK",
};
const sources: Record<string, string> = {
  appRule: "Правило приложения",
  browserDefault: "Браузер: отдых по умолчанию",
  siteRule: "Правило рабочего сайта",
  reclassified: "Пересчёт правил",
  manualEdit: "Ручное исправление",
  AutomaticAfk: "Автоматический AFK",
  ManualAfk: "Ручной AFK",
  SystemAfk: "Блокировка или сон",
};
const clock = (value: number) =>
  new Date(value).toLocaleTimeString("ru-RU", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
const duration = (value: number) =>
  `${Math.floor(value / 3600)} ч ${Math.floor(value / 60) % 60} мин ${Math.floor(value) % 60} с`;
const key = (slice: Slice) => `${slice.start}:${slice.executable}`;
function localInput(value: number) {
  const date = new Date(value);
  return new Date(value - date.getTimezoneOffset() * 60000)
    .toISOString()
    .slice(0, 23);
}

export function HistoryPanel({
  today,
  revision,
  changed,
  date,
  setDate,
  initialReport,
}: {
  today: string;
  revision: number;
  changed: () => Promise<void>;
  date: string;
  setDate: (date: string) => void;
  initialReport: DayReport;
}) {
  const [report, setReport] = useState<DayReport>();
  const [selected, setSelected] = useState<Slice[]>([]);
  const [editing, setEditing] = useState<Slice>();
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [category, setCategory] = useState<Category>("work");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [page, setPage] = useState(0);
  const [lastAction, setLastAction] = useState<string>();
  const [refresh, setRefresh] = useState(0);
  const dialog = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!editing) return;
    const previous = document.activeElement as HTMLElement | null;
    return () => {
      if (previous?.isConnected) previous.focus();
    };
  }, [editing]);
  useEffect(() => {
    if (!editing) return;
    const handle = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) setEditing(undefined);
      if (event.key !== "Tab") return;
      const controls = dialog.current?.querySelectorAll<HTMLElement>(
        "button:not(:disabled), input, select",
      );
      if (!controls?.length) return;
      const first = controls[0],
        last = controls[controls.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener("keydown", handle);
    return () => {
      document.removeEventListener("keydown", handle);
    };
  }, [editing, busy]);
  useEffect(() => {
    if (editing || selected.length > 0) return;
    setReport(initialReport);
  }, [date, revision, editing, selected.length, refresh, initialReport]);

  async function mutate(action: () => Promise<void>) {
    setBusy(true);
    setError("");
    try {
      await action();
      setSelected([]);
      setEditing(undefined);
      setRefresh((value) => value + 1);
      await changed();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Не удалось изменить историю.");
    } finally {
      setBusy(false);
    }
  }
  function edit(slice: Slice) {
    setEditing(slice);
    setStart(localInput(slice.start));
    setEnd(localInput(slice.end));
    setCategory(slice.category);
  }
  function apply(ranges: { start: number; end: number }[], value: Category) {
    return mutate(async () => {
      const result = await api<{ actionId: string }>("/corrections", "POST", {
        ranges,
        category: value,
      });
      setLastAction(result.actionId);
    });
  }
  function undo(id: string) {
    return mutate(async () => {
      await api(`/corrections/${id}`, "DELETE");
      if (lastAction === id) setLastAction(undefined);
    });
  }
  const rows = [...(report?.intervals ?? [])].reverse();
  const pages = Math.max(1, Math.ceil(rows.length / 50));
  const visible = rows.slice(
    Math.min(page, pages - 1) * 50,
    (Math.min(page, pages - 1) + 1) * 50,
  );

  return (
    <section className="section" id="history" aria-labelledby="history-title">
      <div className="section-heading">
        <h2 id="history-title">История активности</h2>
        <input
          type="date"
          aria-label="Дата истории"
          value={date}
          max={today}
          onChange={(event) => {
            if (!event.target.value) return;
            setDate(event.target.value);
            setPage(0);
            setSelected([]);
            setEditing(undefined);
            setReport(undefined);
          }}
        />
      </div>
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
      {report && (
        <>
          <div className="history-totals">
            <span className="work-text">
              Работа · {duration(report.workSeconds)}
            </span>
            <span>Отдых · {duration(report.restSeconds)}</span>
            <span>AFK · {duration(report.afkSeconds)}</span>
          </div>
          <svg
            className="timeline"
            viewBox="0 0 1000 24"
            role="img"
            aria-label="Лента выбранного дня"
          >
            <rect x="0" y="0" width="1000" height="24" fill="#e5eaed" />
            {revision < report.end && (
              <rect
                x={Math.max(
                  0,
                  ((revision - report.start) / (report.end - report.start)) *
                    1000,
                )}
                y="0"
                width={Math.min(
                  1000,
                  ((report.end - revision) / (report.end - report.start)) *
                    1000,
                )}
                height="24"
                fill="#fafbfc"
              >
                <title>Будущее время</title>
              </rect>
            )}
            {(report.pauses ?? []).map((pause) => (
              <rect
                key={`pause${pause.start}`}
                x={
                  ((pause.start - report.start) / (report.end - report.start)) *
                  1000
                }
                y="0"
                width={
                  ((pause.end - pause.start) / (report.end - report.start)) *
                  1000
                }
                height="24"
                fill="#bd9b58"
              >
                <title>Пауза учёта</title>
              </rect>
            ))}
            {report.intervals.map((slice) => (
              <rect
                key={key(slice)}
                x={
                  ((slice.start - report.start) / (report.end - report.start)) *
                  1000
                }
                y="0"
                width={Math.max(
                  0.4,
                  ((slice.end - slice.start) / (report.end - report.start)) *
                    1000,
                )}
                height="24"
                fill={
                  slice.category === "work"
                    ? "#29896c"
                    : slice.category === "rest"
                      ? "#4b91b4"
                      : "#9b91b5"
                }
              >
                <title>
                  {clock(slice.start)}–{clock(slice.end)} ·{" "}
                  {slice.browser?.title ?? slice.executable} ·{" "}
                  {categories[slice.category]}
                </title>
              </rect>
            ))}
          </svg>
          <p className="muted">
            Работа · Отдых · AFK · Пауза · Нет данных · Будущее время
          </p>
          <div className="history-toolbar">
            <label className="checkbox-label">
              <input
                type="checkbox"
                aria-label="Выбрать интервалы на странице"
                checked={
                  visible.length > 0 &&
                  visible.every((row) =>
                    selected.some((s) => key(s) === key(row)),
                  )
                }
                onChange={(e) => setSelected(e.target.checked ? visible : [])}
              />
              {selected.length
                ? `Выбрано: ${selected.length}`
                : "Выбрать на странице"}
            </label>
            <div className="history-actions">
              {selected.length > 0 && (
                <>
                  <select
                    aria-label="Категория выделенных интервалов"
                    value={category}
                    onChange={(e) => setCategory(e.target.value as Category)}
                  >
                    {Object.entries(categories).map(([value, label]) => (
                      <option key={value} value={value}>
                        {label}
                      </option>
                    ))}
                  </select>
                  <button
                    disabled={busy}
                    onClick={() =>
                      void apply(
                        selected.map((s) => ({ start: s.start, end: s.end })),
                        category,
                      )
                    }
                  >
                    <Check size={16} />
                    Применить
                  </button>
                  <button
                    className="icon-button"
                    title="Снять выделение"
                    aria-label="Снять выделение"
                    onClick={() => setSelected([])}
                  >
                    <X size={16} />
                  </button>
                </>
              )}
              {lastAction && (
                <button disabled={busy} onClick={() => void undo(lastAction)}>
                  <Undo2 size={16} />
                  Отменить правку
                </button>
              )}
            </div>
          </div>
          <div className="history-list">
            {visible.map((slice) => (
              <div className="history-row" key={key(slice)}>
                <input
                  type="checkbox"
                  aria-label={`Выбрать ${clock(slice.start)} ${slice.executable}`}
                  checked={selected.some((s) => key(s) === key(slice))}
                  onChange={(e) =>
                    setSelected((items) =>
                      e.target.checked
                        ? [...items, slice]
                        : items.filter((s) => key(s) !== key(slice)),
                    )
                  }
                />
                <div className="history-time">
                  <span>{clock(slice.start)}</span>
                  <small>{clock(slice.end)}</small>
                </div>
                <div className="history-activity">
                  <strong>
                    {slice.browser?.private
                      ? "Приватное окно Firefox"
                      : slice.browser?.title || slice.executable}
                  </strong>
                  <span>
                    {slice.browser?.url ||
                      (slice.executable.toLowerCase() === "firefox.exe"
                        ? "Вкладка неизвестна"
                        : slice.executable)}
                  </span>
                  <small>{sources[slice.source] ?? slice.source}</small>
                </div>
                <span
                  className={`history-category ${slice.category === "work" ? "work-text" : ""}`}
                >
                  {categories[slice.category]}
                </span>
                <div className="history-actions">
                  {slice.correctionId && (
                    <button
                      className="icon-button"
                      disabled={busy}
                      title="Отменить это исправление"
                      aria-label="Отменить это исправление"
                      onClick={() => void undo(slice.correctionId!)}
                    >
                      <Undo2 size={15} />
                    </button>
                  )}
                  <button
                    className="icon-button"
                    title="Изменить интервал"
                    aria-label={`Изменить интервал ${clock(slice.start)}`}
                    onClick={() => edit(slice)}
                  >
                    <Pencil size={15} />
                  </button>
                </div>
              </div>
            ))}
            {rows.length === 0 && (
              <p className="empty">За этот день нет данных.</p>
            )}
          </div>
          {pages > 1 && (
            <div className="pagination">
              <button
                className="icon-button"
                title="Предыдущая страница"
                disabled={page <= 0}
                onClick={() => {
                  setPage((p) => p - 1);
                  setSelected([]);
                }}
              >
                <ArrowLeft size={16} />
              </button>
              <span>
                {Math.min(page + 1, pages)} / {pages}
              </span>
              <button
                className="icon-button"
                title="Следующая страница"
                disabled={page + 1 >= pages}
                onClick={() => {
                  setPage((p) => p + 1);
                  setSelected([]);
                }}
              >
                <ArrowRight size={16} />
              </button>
            </div>
          )}
        </>
      )}
      {editing && (
        <div className="modal-backdrop">
          <div
            ref={dialog}
            className="edit-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="edit-title"
          >
            <div className="section-heading">
              <h2 id="edit-title">Изменить интервал</h2>
              <button
                className="icon-button"
                title="Закрыть"
                aria-label="Закрыть редактор"
                onClick={() => setEditing(undefined)}
              >
                <X size={17} />
              </button>
            </div>
            <p className="edit-subject">
              {editing.browser?.title ?? editing.executable}
            </p>
            <form
              onSubmit={(e) => {
                e.preventDefault();
                const from = new Date(start).getTime(),
                  to = new Date(end).getTime();
                if (
                  !Number.isFinite(from) ||
                  !Number.isFinite(to) ||
                  from < editing.start ||
                  to > editing.end ||
                  from >= to
                ) {
                  setError(
                    "Выберите часть исходного интервала: начало должно быть раньше конца.",
                  );
                  return;
                }
                void apply([{ start: from, end: to }], category);
              }}
            >
              <label>
                Начало
                <input
                  autoFocus
                  type="datetime-local"
                  step="0.001"
                  required
                  value={start}
                  onChange={(e) => setStart(e.target.value)}
                />
              </label>
              <label>
                Конец
                <input
                  type="datetime-local"
                  step="0.001"
                  required
                  value={end}
                  onChange={(e) => setEnd(e.target.value)}
                />
              </label>
              <label>
                Категория
                <select
                  value={category}
                  onChange={(e) => setCategory(e.target.value as Category)}
                >
                  {Object.entries(categories).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>
              {error && (
                <div className="alert" role="alert">
                  {error}
                </div>
              )}
              <div className="form-footer">
                <button type="button" onClick={() => setEditing(undefined)}>
                  Отмена
                </button>
                <button className="primary" disabled={busy} type="submit">
                  <Check size={16} />
                  Применить к диапазону
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </section>
  );
}
