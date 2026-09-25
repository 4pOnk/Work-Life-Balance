import { useCallback, useEffect, useState } from "react";
import { ChevronLeft, ChevronRight, CalendarDays } from "lucide-react";
import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { api, type DashboardReport, type Totals } from "./api";
import { HistoryPanel } from "./HistoryPanel";
import { TodoPanel } from "./TodoPanel";
import {
  calendarDuration,
  compactCalendarDuration,
  duration,
} from "./duration";
import "./dashboard.css";

const label = (date: string) =>
  new Date(`${date}T12:00:00`).toLocaleDateString("ru-RU", {
    day: "numeric",
    month: "long",
    year: "numeric",
  });
function monthShift(month: string, amount: number) {
  const [year, number] = month.split("-").map(Number);
  const date = new Date(year, number - 1 + amount, 1, 12);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`;
}
function Summary({ value }: { value: Totals }) {
  return (
    <div className="report-totals">
      <span>
        <small>Работа</small>
        <strong>{duration(value.workSeconds)}</strong>
      </span>
      <span>
        <small>Отдых</small>
        <strong>{duration(value.restSeconds)}</strong>
      </span>
      <span>
        <small>AFK</small>
        <strong>{duration(value.afkSeconds)}</strong>
      </span>
      <span>
        <small>Доля работы</small>
        <strong>
          {value.workShare === null
            ? "Нет данных"
            : `${Math.round(value.workShare * 100)}%`}
        </strong>
      </span>
    </div>
  );
}
export function DashboardPanel({
  today,
  revision,
  changed,
}: {
  today: string;
  revision: number;
  changed: () => Promise<void>;
}) {
  const [picked, setPicked] = useState<string>();
  const date = picked ?? today;
  const [month, setMonth] = useState(today.slice(0, 7));
  const [data, setData] = useState<DashboardReport>();
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  const [hovered, setHovered] = useState<string>();
  const load = useCallback(
    () => api<DashboardReport>(`/dashboard?date=${date}&month=${month}`),
    [date, month],
  );
  useEffect(() => {
    let alive = true;
    load()
      .then((value) => {
        if (alive) {
          setData(value);
          setError("");
        }
      })
      .catch((e) => {
        if (alive)
          setError(e instanceof Error ? e.message : "Отчёт недоступен");
      });
    return () => {
      alive = false;
    };
  }, [load, revision, retry]);
  async function edited() {
    setData(await load());
    await changed();
  }
  function select(value: string) {
    setPicked(value);
    setMonth(value.slice(0, 7));
  }
  const current =
    data?.day.report.date === date && data.month.month === month
      ? data
      : undefined;
  const detail = current?.day;
  const preview = current?.month.days.find(
    (day) => day.date === (hovered ?? date),
  );
  const offset = (new Date(`${month}-01T12:00:00`).getDay() + 6) % 7;
  return (
    <>
      <TodoPanel
        key={date}
        date={date}
        today={today}
        data={current?.todos}
        select={select}
        changed={edited}
      />
      <section className="section" aria-labelledby="calendar-title">
        <div className="section-heading">
          <h2 id="calendar-title">
            <CalendarDays size={18} /> Календарь активности
          </h2>
          <div className="month-controls">
            <button
              className="icon-button"
              aria-label="Предыдущий месяц"
              title="Предыдущий месяц"
              disabled={month <= "1970-01"}
              onClick={() => setMonth(monthShift(month, -1))}
            >
              <ChevronLeft size={18} />
            </button>
            <input
              type="month"
              aria-label="Месяц отчёта"
              min="1970-01"
              max="9998-12"
              value={month}
              onChange={(e) => {
                if (e.target.value) setMonth(e.target.value);
              }}
            />
            <button
              className="icon-button"
              aria-label="Следующий месяц"
              title="Следующий месяц"
              disabled={month >= "9998-12"}
              onClick={() => setMonth(monthShift(month, 1))}
            >
              <ChevronRight size={18} />
            </button>
          </div>
        </div>
        {error && (
          <div role="alert" className="alert">
            {error}{" "}
            <button onClick={() => setRetry((x) => x + 1)}>Повторить</button>
          </div>
        )}
        {!current ? (
          <p role="status">Загрузка отчёта…</p>
        ) : (
          <>
            <Summary value={current.month.totals} />
            <div className="calendar-layout">
              <div>
                <div className="calendar-grid" aria-label="Дни месяца">
                  {["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"].map((day) => (
                    <span className="weekday" key={day}>
                      {day}
                    </span>
                  ))}
                  {Array.from({ length: offset }, (_, i) => (
                    <span key={`blank${i}`} />
                  ))}
                  {current.month.days.map((day) => {
                    const description =
                      day.state === "future"
                        ? "Будущий день"
                        : day.state === "empty"
                          ? "Нет данных"
                          : `Работа ${duration(day.totals.workSeconds)}, отдых ${duration(day.totals.restSeconds)}, AFK ${duration(day.totals.afkSeconds)}`;
                    return (
                      <button
                        key={day.date}
                        className={`calendar-day ${day.state}`}
                        aria-label={`${label(day.date)}: ${description}; выполнено ${day.taskCount}, вес ${day.taskWeight}`}
                        title={`${label(day.date)}: ${description}; выполнено ${day.taskCount}, вес ${day.taskWeight}`}
                        aria-pressed={day.date === date}
                        onFocus={() => setHovered(day.date)}
                        onMouseEnter={() => setHovered(day.date)}
                        onMouseLeave={() => setHovered(undefined)}
                        onClick={() => {
                          select(day.date);
                        }}
                      >
                        <i
                          aria-hidden="true"
                          className={`calendar-half time-half ${day.hasActivity ? `level-${day.level}` : ""}`}
                        />
                        <i
                          aria-hidden="true"
                          className={`calendar-half task-half task-level-${day.taskLevel}`}
                        />
                        <span className="calendar-number">
                          {Number(day.date.slice(-2))}
                        </span>
                        <small className="calendar-values">
                          <span
                            className={`calendar-value ${day.hasActivity && day.level >= 3 ? "light" : ""}`}
                          >
                            <span className="calendar-duration-full">
                              {day.state === "future"
                                ? "·"
                                : day.hasActivity
                                  ? calendarDuration(day.totals.workSeconds)
                                  : "—"}
                            </span>
                            <span className="calendar-duration-compact">
                              {day.state === "future"
                                ? "·"
                                : day.hasActivity
                                  ? compactCalendarDuration(
                                      day.totals.workSeconds,
                                    )
                                  : "—"}
                            </span>
                          </span>
                          <span
                            className={`calendar-value ${day.taskLevel >= 3 ? "light" : ""}`}
                          >
                            {day.taskWeight}
                          </span>
                        </small>
                      </button>
                    );
                  })}
                </div>
                <div className="calendar-legend">
                  <strong>Часы работы</strong>
                  {["0 ч", "<1 ч", "1–4 ч", "4–8 ч", "8+ ч"].map((item, i) => (
                    <span key={item}>
                      <i className={`level-${i}`} />
                      {item}
                    </span>
                  ))}
                  <span>— Нет данных</span>
                  <span>· Будущее</span>
                </div>
                <div className="calendar-legend">
                  <strong>Вес выполненных задач</strong>
                  {["0", "1–4", "5–9", "10–14", "15+"].map((item, i) => (
                    <span key={item}>
                      <i className={`task-level-${i}`} />
                      {item}
                    </span>
                  ))}
                </div>
                <div className="calendar-preview" aria-live="polite">
                  {preview
                    ? `${label(preview.date)} · ${preview.state === "future" ? "Будущий день" : preview.state === "empty" ? "Нет данных" : `Работа ${duration(preview.totals.workSeconds)} · Отдых ${duration(preview.totals.restSeconds)} · AFK ${duration(preview.totals.afkSeconds)}`}`
                    : " "}
                </div>
                {preview && (
                  <p className="muted">
                    Выполнено задач: {preview.taskCount} · Суммарный вес:{" "}
                    {preview.taskWeight}
                  </p>
                )}
              </div>
              <div className="weeks">
                <h3>Недели в этом месяце</h3>
                {current.month.weeks.map((week) => (
                  <div className="week" key={week.start}>
                    <strong>
                      {week.start.slice(8)}–{week.end.slice(8)}
                    </strong>
                    <span>
                      Работа {duration(week.totals.workSeconds)}
                      <small>
                        Отдых {duration(week.totals.restSeconds)} · AFK{" "}
                        {duration(week.totals.afkSeconds)}
                      </small>
                    </span>
                  </div>
                ))}
              </div>
            </div>
          </>
        )}
      </section>
      {detail && (
        <>
          <section className="section" aria-labelledby="day-title">
            <div className="section-heading">
              <div>
                <h2 id="day-title">{label(date)}</h2>
                <p>
                  {date === today ? "Текущий неполный день · " : ""}
                  {detail.report.timeZone}
                </p>
              </div>
              <button
                onClick={() => {
                  setPicked(undefined);
                  setMonth(today.slice(0, 7));
                }}
              >
                Сегодня
              </button>
            </div>
            <Summary value={detail.report} />
            <div className="coverage">
              <span>Пауза {duration(detail.pauseSeconds)}</span>
              <span>Нет данных {duration(detail.unknownSeconds)}</span>
              {date === today && (
                <span>Остаток дня {duration(detail.futureSeconds)}</span>
              )}
              {detail.provisionalSeconds > 0 && (
                <span>
                  Предварительно до AFK: {duration(detail.provisionalSeconds)}
                </span>
              )}
            </div>
            <h3>По часам</h3>
            <div className="chart-legend">
              <span>Работа</span>
              <span>Отдых</span>
              <span>AFK</span>
            </div>
            <div className="hour-chart" aria-label="Почасовая активность">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart
                  data={detail.hours}
                  accessibilityLayer
                  margin={{ left: 0, right: 8, top: 8, bottom: 0 }}
                >
                  <CartesianGrid vertical={false} stroke="#e0e5e8" />
                  <XAxis
                    dataKey="label"
                    tickFormatter={(value) => String(value).slice(0, 5)}
                    minTickGap={22}
                    tick={{ fontSize: 11 }}
                  />
                  <YAxis
                    width={35}
                    tickFormatter={(value) => `${Number(value) / 60}`}
                    domain={[0, 3600]}
                    tick={{ fontSize: 11 }}
                  />
                  <Tooltip formatter={(value) => duration(Number(value))} />
                  <Bar
                    dataKey="workSeconds"
                    name="Работа"
                    stackId="time"
                    fill="#23816b"
                    isAnimationActive={false}
                  />
                  <Bar
                    dataKey="restSeconds"
                    name="Отдых"
                    stackId="time"
                    fill="#4786a6"
                    isAnimationActive={false}
                  />
                  <Bar
                    dataKey="afkSeconds"
                    name="AFK"
                    stackId="time"
                    fill="#8b789a"
                    isAnimationActive={false}
                  />
                </BarChart>
              </ResponsiveContainer>
            </div>
            <details>
              <summary>Почасовые значения, минуты</summary>
              <div className="report-table">
                <table>
                  <thead>
                    <tr>
                      <th>Час (UTC±)</th>
                      <th>Работа</th>
                      <th>Отдых</th>
                      <th>AFK</th>
                    </tr>
                  </thead>
                  <tbody>
                    {detail.hours.map((hour) => (
                      <tr key={hour.start}>
                        <th>{hour.label}</th>
                        <td>{(hour.workSeconds / 60).toFixed(1)}</td>
                        <td>{(hour.restSeconds / 60).toFixed(1)}</td>
                        <td>{(hour.afkSeconds / 60).toFixed(1)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </details>
            <h3>Приложения</h3>
            {detail.apps.length === 0 ? (
              <p className="muted">За этот день нет записанной активности.</p>
            ) : (
              <div className="report-table">
                <table>
                  <thead>
                    <tr>
                      <th>Приложение</th>
                      <th>Работа</th>
                      <th>Отдых</th>
                      <th>AFK</th>
                    </tr>
                  </thead>
                  <tbody>
                    {detail.apps.map((app) => (
                      <tr key={app.executable}>
                        <th>
                          {app.executable === "unknown"
                            ? "Неизвестное окно"
                            : app.executable}
                        </th>
                        <td>{duration(app.workSeconds)}</td>
                        <td>{duration(app.restSeconds)}</td>
                        <td>{duration(app.afkSeconds)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
          <HistoryPanel
            key={date}
            today={today}
            date={date}
            setDate={select}
            initialReport={detail.report}
            revision={revision}
            changed={edited}
          />
        </>
      )}
    </>
  );
}
