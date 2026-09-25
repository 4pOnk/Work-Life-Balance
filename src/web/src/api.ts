export type Presence = "active" | "automaticAfk" | "manualAfk" | "systemAfk";
export type Category = "work" | "rest" | "afk";
export interface Settings {
  port: number;
  automaticAfk: boolean;
  afkMinutes: number;
  hotkey: string;
  workProcesses: string[];
  workSites: string[];
  browserPath: boolean;
  retentionDays: number;
}
export interface Slice {
  start: number;
  end: number;
  executable: string;
  category: Category;
  browser: {
    url: string | null;
    domain: string | null;
    title: string | null;
    private: boolean;
    windowId: number;
    tabId: number;
  } | null;
  source: string;
  correctionId: string | null;
}
export interface DayReport {
  completedTodos: TodoItem[] | null;
  date: string;
  timeZone: string;
  workSeconds: number;
  restSeconds: number;
  afkSeconds: number;
  workShare: number | null;
  intervals: Slice[];
  start: number;
  end: number;
  pauses: { start: number; end: number }[] | null;
}
export interface Totals {
  workSeconds: number;
  restSeconds: number;
  afkSeconds: number;
  workShare: number | null;
}
export interface DashboardReport {
  todos: TodoDay;
  day: {
    report: DayReport;
    hours: (Omit<Totals, "workShare"> & { start: number; label: string })[];
    apps: (Omit<Totals, "workShare"> & { executable: string })[];
    pauseSeconds: number;
    unknownSeconds: number;
    futureSeconds: number;
    provisionalSeconds: number;
  };
  month: {
    month: string;
    timeZone: string;
    totals: Totals;
    days: {
      date: string;
      state: "recorded" | "empty" | "future";
      level: number;
      taskCount: number;
      taskWeight: number;
      taskLevel: number;
      hasActivity: boolean;
      totals: Totals;
    }[];
    weeks: { start: string; end: string; totals: Totals }[];
  };
}
export interface TodoItem {
  id: string;
  title: string;
  weight: number;
  date: string;
  completedAt: number | null;
  completedDate: string | null;
}
export interface TodoDay {
  date: string;
  today: string;
  items: TodoItem[];
}
export interface Status {
  tracker: {
    settings: Settings;
    state: {
      paused: boolean;
      presence: Presence;
      activity: { executable: string };
      awaySince: number | null;
    };
    today: DayReport;
    error: string | null;
    serverTime: number;
  };
  hotkeyError: string | null;
  activePort: number;
  firefox: { connected: boolean; lastSeen: number | null; state: string };
}
export interface ProcessEntry {
  executable: string;
  instances: number;
  hasWindow: boolean;
  active: boolean;
}

let token: string | undefined;
export async function api<T>(
  path: string,
  method = "GET",
  body?: unknown,
): Promise<T> {
  if (method !== "GET" && !token) {
    const session = await api<{ token: string }>("/session");
    token = session.token;
  }
  const response = await fetch(`/api/v1${path}`, {
    method,
    signal: AbortSignal.timeout(8000),
    headers:
      method === "GET"
        ? {}
        : { "Content-Type": "application/json", "X-WLB-Token": token! },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    if (response.status === 403) token = undefined;
    const error = (await response.json().catch(() => ({}))) as {
      error?: string;
    };
    throw new Error(error.error ?? `Запрос не выполнен (${response.status}).`);
  }
  const text = await response.text();
  return text ? (JSON.parse(text) as T) : (undefined as T);
}
