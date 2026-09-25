import { useState, type FormEvent } from "react";
import { ListTodo, Plus, Pencil, Trash2, Check, X } from "lucide-react";
import { api, type TodoDay, type TodoItem } from "./api";
import "./todo.css";

export function TodoPanel({
  date,
  today,
  data,
  select,
  changed,
}: {
  date: string;
  today: string;
  data?: TodoDay;
  select: (date: string) => void;
  changed: () => Promise<void>;
}) {
  const [title, setTitle] = useState("");
  const [weight, setWeight] = useState(1);
  const [editing, setEditing] = useState<TodoItem>();
  const [busy, setBusy] = useState(false);
  const [completion, setCompletion] = useState<{
    id: string;
    completed: boolean;
  }>();
  const [error, setError] = useState("");
  const actualToday = data?.today ?? today;
  const past = date < actualToday;
  const done = data?.items.filter((item) => item.completedAt !== null) ?? [];
  async function mutate(
    path: string,
    method: string,
    body?: unknown,
    saved?: () => void,
  ) {
    if (busy) return;
    setBusy(true);
    setError("");
    try {
      await api(path, method, body);
      saved?.();
      await changed();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Не удалось сохранить задачу.");
    } finally {
      setCompletion(undefined);
      setBusy(false);
    }
  }
  function add(event: FormEvent) {
    event.preventDefault();
    void mutate("/todos", "POST", { title, weight, date }, () => {
      setTitle("");
      setWeight(1);
    });
  }
  return (
    <section
      className="section todos"
      aria-labelledby="todos-title"
      aria-busy={busy}
    >
      <div className="section-heading">
        <h2 id="todos-title">
          <ListTodo size={18} /> Дела
        </h2>
        <input
          type="date"
          aria-label="Дата задач"
          value={date}
          min="1970-01-01"
          max="9998-12-31"
          disabled={busy}
          onChange={(e) => {
            if (e.target.value) select(e.target.value);
          }}
        />
      </div>
      <p className="muted">
        {past ? "Выполнено" : date > actualToday ? "Запланировано" : "Сегодня"}
        {data &&
          ` · ${done.length} выполнено · вес ${done.reduce((sum, item) => sum + item.weight, 0)}`}
      </p>
      {error && (
        <p className="alert" role="alert">
          {error}
        </p>
      )}
      {!data ? (
        <p role="status">Загрузка задач…</p>
      ) : (
        <>
          {data.items.length === 0 && (
            <p className="muted">
              {past ? "Нет выполненных задач." : "Задач пока нет."}
            </p>
          )}
          <ul className="todo-list">
            {data.items.map((item) => (
              <li key={item.id} className={`todo-row effort-${item.weight}`}>
                {editing?.id === item.id && !past ? (
                  <form
                    className="todo-edit"
                    onSubmit={(e) => {
                      e.preventDefault();
                      void mutate(
                        `/todos/${item.id}`,
                        "PUT",
                        {
                          title: editing.title,
                          weight: editing.weight,
                          date: editing.date,
                        },
                        () => setEditing(undefined),
                      );
                    }}
                  >
                    <input
                      aria-label="Название задачи"
                      required
                      maxLength={240}
                      value={editing.title}
                      onChange={(e) =>
                        setEditing({ ...editing, title: e.target.value })
                      }
                      disabled={busy}
                    />
                    <Weight
                      value={editing.weight}
                      disabled={busy}
                      change={(value) =>
                        setEditing({ ...editing, weight: value })
                      }
                    />
                    <input
                      type="date"
                      aria-label="Перенести задачу на"
                      min={actualToday}
                      max="9998-12-31"
                      value={editing.date}
                      disabled={busy || item.completedAt !== null}
                      onChange={(e) =>
                        setEditing({ ...editing, date: e.target.value })
                      }
                      required
                    />
                    <button
                      className="icon-button"
                      title="Сохранить задачу"
                      aria-label="Сохранить задачу"
                      disabled={busy || !editing.title.trim()}
                    >
                      <Check size={18} />
                    </button>
                    <button
                      className="icon-button"
                      type="button"
                      title="Отменить редактирование"
                      aria-label="Отменить редактирование"
                      disabled={busy}
                      onClick={() => setEditing(undefined)}
                    >
                      <X size={18} />
                    </button>
                  </form>
                ) : (
                  <>
                    <input
                      type="checkbox"
                      aria-label={`Выполнено: ${item.title}`}
                      checked={
                        completion?.id === item.id
                          ? completion.completed
                          : item.completedAt !== null
                      }
                      disabled={busy || past || date > actualToday}
                      onChange={(e) => {
                        setCompletion({
                          id: item.id,
                          completed: e.target.checked,
                        });
                        void mutate(`/todos/${item.id}/completion`, "POST", {
                          completed: e.target.checked,
                        });
                      }}
                    />
                    <span
                      className={`todo-title ${item.completedAt !== null ? "done" : ""}`}
                    >
                      {item.title}
                    </span>
                    <span
                      className="todo-weight"
                      title={`Трудозатратность: ${item.weight} из 5`}
                      aria-label={`Вес ${item.weight}`}
                    >
                      {item.weight}
                    </span>
                    {!past && (
                      <div className="todo-actions">
                        <button
                          className="icon-button"
                          title="Изменить задачу"
                          aria-label={`Изменить: ${item.title}`}
                          disabled={busy}
                          onClick={() => setEditing({ ...item })}
                        >
                          <Pencil size={16} />
                        </button>
                        <button
                          className="icon-button"
                          title="Удалить задачу"
                          aria-label={`Удалить: ${item.title}`}
                          disabled={busy}
                          onClick={() => {
                            if (
                              window.confirm(`Удалить задачу «${item.title}»?`)
                            )
                              void mutate(`/todos/${item.id}`, "DELETE");
                          }}
                        >
                          <Trash2 size={16} />
                        </button>
                      </div>
                    )}
                  </>
                )}
              </li>
            ))}
          </ul>
          {!past && (
            <form className="todo-add" onSubmit={add}>
              <input
                aria-label="Новая задача"
                placeholder="Новая задача"
                required
                maxLength={240}
                value={title}
                disabled={busy}
                onChange={(e) => setTitle(e.target.value)}
              />
              <Weight value={weight} disabled={busy} change={setWeight} />
              <button
                className="icon-button"
                aria-label="Добавить задачу"
                title="Добавить задачу"
                disabled={busy || !title.trim()}
              >
                <Plus size={20} />
              </button>
            </form>
          )}
        </>
      )}
    </section>
  );
}

function Weight({
  value,
  change,
  disabled,
}: {
  value: number;
  change: (value: number) => void;
  disabled: boolean;
}) {
  return (
    <label className="todo-weight-input">
      Вес
      <select
        aria-label="Вес задачи"
        value={value}
        disabled={disabled}
        onChange={(e) => change(Number(e.target.value))}
      >
        {[1, 2, 3, 4, 5].map((n) => (
          <option key={n} value={n}>
            {n}
          </option>
        ))}
      </select>
    </label>
  );
}
