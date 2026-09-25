import { useEffect, useState } from "react";
import {
  DatabaseBackup,
  Download,
  RotateCcw,
  Save,
  Trash2,
} from "lucide-react";
import { api, type Status } from "./api";
import "./storage.css";

interface Backup {
  id: string;
  created: number;
  bytes: number;
}
interface Storage {
  databaseBytes: number;
  firstRecorded: number | null;
  backups: Backup[];
}
export function StoragePanel({
  status,
  changed,
}: {
  status: Status;
  changed: () => Promise<void>;
}) {
  const [storage, setStorage] = useState<Storage>();
  const [startup, setStartup] = useState<boolean>();
  const [days, setDays] = useState(status.tracker.settings.retentionDays ?? 0);
  const [from, setFrom] = useState(status.tracker.today.date);
  const [to, setTo] = useState(status.tracker.today.date);
  const [format, setFormat] = useState("csv");
  const [pending, setPending] = useState<{
    operation: "delete" | "restore";
    id?: string;
    all?: boolean;
    from?: string;
    to?: string;
  }>();
  const [confirmation, setConfirmation] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  useEffect(() => {
    void api<Storage>("/storage")
      .then(setStorage)
      .catch((e) => setError(String(e)));
    void api<{ enabled: boolean }>("/startup")
      .then((x) => setStartup(x.enabled))
      .catch((e) => setError(String(e)));
  }, []);
  async function run(action: () => Promise<unknown>, success: string) {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await action();
      setStorage(await api<Storage>("/storage"));
      await changed();
      setMessage(success);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Операция не выполнена.");
    } finally {
      setBusy(false);
    }
  }
  function confirm(value: NonNullable<typeof pending>) {
    setPending(value);
    setConfirmation("");
  }
  async function download() {
    const response = await fetch(
      `/api/v1/export?${new URLSearchParams({ from, to, format })}`,
      { signal: AbortSignal.timeout(60000) },
    );
    if (!response.ok) {
      const value = (await response.json()) as { error?: string };
      throw new Error(value.error ?? "Экспорт не выполнен.");
    }
    const url = URL.createObjectURL(await response.blob());
    const link = document.createElement("a");
    link.href = url;
    link.download = `work-life-balance-${from}-${to}.${format}`;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  const word = pending?.operation === "restore" ? "ВОССТАНОВИТЬ" : "УДАЛИТЬ";
  const paused = status.tracker.state.paused;
  const cutoff = new Date(`${status.tracker.today.date}T12:00:00`);
  cutoff.setDate(cutoff.getDate() - days);
  const cutoffDate = `${cutoff.getFullYear()}-${String(cutoff.getMonth() + 1).padStart(2, "0")}-${String(cutoff.getDate()).padStart(2, "0")}`;
  return (
    <section className="section" aria-labelledby="storage-title">
      <div className="section-heading">
        <h2 id="storage-title">Данные и запуск</h2>
        <DatabaseBackup size={19} />
      </div>
      {error && (
        <div role="alert" className="alert">
          {error}
        </div>
      )}
      <div className="setting-row">
        <label htmlFor="startup">Запускать при входе в Windows</label>
        <input
          id="startup"
          type="checkbox"
          className="switch"
          disabled={busy || startup === undefined}
          checked={startup ?? false}
          onChange={(e) => {
            const enabled = e.target.checked;
            void run(async () => {
              const result = await api<{ enabled: boolean }>(
                "/startup",
                "PUT",
                { enabled },
              );
              setStartup(result.enabled);
            }, "Автозапуск обновлён");
          }}
        />
      </div>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          void run(
            () =>
              api("/settings", "PUT", {
                ...status.tracker.settings,
                retentionDays: days,
              }),
            "Срок хранения сохранён. Автоматического удаления нет.",
          );
        }}
      >
        <div className="setting-row">
          <label htmlFor="retention">
            Срок хранения, дней (0 — без ограничения)
          </label>
          <input
            id="retention"
            type="number"
            min="0"
            max="36500"
            step="1"
            required
            value={days}
            onChange={(e) => setDays(Number(e.target.value))}
          />
        </div>
        <div className="storage-actions">
          <button disabled={busy} type="submit">
            <Save size={16} />
            Сохранить срок
          </button>
          <button
            type="button"
            disabled={busy || days <= 0 || !paused}
            onClick={() =>
              confirm({
                operation: "delete",
                from: "1970-01-01",
                to: cutoffDate,
              })
            }
          >
            <Trash2 size={16} />
            Очистить по сроку
          </button>
        </div>
      </form>
      <h3>Экспорт и диапазон истории</h3>
      <div className="storage-range">
        <label>
          С
          <input
            aria-label="Начало диапазона данных"
            type="date"
            min="1970-01-01"
            max={to}
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
        </label>
        <label>
          По
          <input
            aria-label="Конец диапазона данных"
            type="date"
            min={from}
            max={status.tracker.today.date}
            value={to}
            onChange={(e) => setTo(e.target.value)}
          />
        </label>
        <label>
          Формат
          <select
            aria-label="Формат экспорта"
            value={format}
            onChange={(e) => setFormat(e.target.value)}
          >
            <option value="csv">CSV</option>
            <option value="json">JSON</option>
          </select>
        </label>
      </div>
      <div className="storage-actions">
        <button
          disabled={busy || !from || !to}
          onClick={() => void run(download, "Экспорт подготовлен")}
        >
          <Download size={16} />
          Экспорт
        </button>
        <button
          disabled={busy || !paused || !from || !to}
          onClick={() => confirm({ operation: "delete", from, to })}
        >
          <Trash2 size={16} />
          Удалить диапазон
        </button>
        <button
          disabled={busy || !paused}
          onClick={() => confirm({ operation: "delete", all: true })}
        >
          <Trash2 size={16} />
          Удалить всю историю
        </button>
      </div>
      <h3>Резервные копии</h3>
      <div className="storage-actions">
        <span>
          База:{" "}
          {storage ? `${(storage.databaseBytes / 1048576).toFixed(2)} МБ` : "…"}
        </span>
        <button
          disabled={busy}
          onClick={() =>
            void run(
              () => api("/storage/backup", "POST"),
              "Копия создана на этом компьютере",
            )
          }
        >
          <DatabaseBackup size={16} />
          Создать копию
        </button>
      </div>
      {!paused && (
        <p className="muted">
          Удаление и восстановление доступны при паузе учёта.
        </p>
      )}
      <div className="backup-list">
        {storage?.backups.map((copy) => (
          <div className="backup-row" key={copy.id}>
            <span>
              {new Date(copy.created).toLocaleString("ru-RU")}
              <small>
                {(copy.bytes / 1048576).toFixed(2)} МБ · {copy.id.slice(0, 8)}
              </small>
            </span>
            <button
              className="icon-button"
              title="Восстановить историю из копии"
              aria-label={`Восстановить копию ${copy.id}`}
              disabled={busy || !paused}
              onClick={() => confirm({ operation: "restore", id: copy.id })}
            >
              <RotateCcw size={16} />
            </button>
          </div>
        ))}
      </div>
      {storage?.backups.length === 0 && <p className="muted">Копий пока нет</p>}
      {pending && (
        <form
          className="storage-confirm"
          onSubmit={(e) => {
            e.preventDefault();
            void run(async () => {
              await api(`/storage/${pending.operation}`, "POST", {
                ...pending,
                confirmation,
              });
              setPending(undefined);
            }, "История обновлена. Страховочная копия сохранена.");
          }}
        >
          <h3>
            {pending.operation === "restore"
              ? "Заменить историю и задачи выбранной копией?"
              : pending.all
                ? "Удалить всю историю и выполненные задачи?"
                : `Удалить историю и выполненные задачи с ${pending.from} по ${pending.to}?`}
          </h3>
          <p>
            Текущие настройки сохранятся. Перед операцией создаётся страховочная
            копия. Удалённая история останется в копиях на диске.
          </p>
          <label>
            Для подтверждения введите {word}
            <input
              aria-label="Подтверждение операции с историей"
              autoFocus
              value={confirmation}
              onChange={(e) => setConfirmation(e.target.value)}
              autoComplete="off"
            />
          </label>
          <div className="storage-actions">
            <button
              type="submit"
              disabled={busy || !paused || confirmation !== word}
            >
              {pending.operation === "restore" ? (
                <RotateCcw size={16} />
              ) : (
                <Trash2 size={16} />
              )}
              Подтвердить
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => setPending(undefined)}
            >
              Отмена
            </button>
          </div>
        </form>
      )}
      <p role="status">{busy ? "Операция выполняется…" : message}</p>
    </section>
  );
}
