import { expect, test } from "@playwright/test";
import type { Status, TodoDay, DashboardReport } from "../src/api";

test.beforeEach(() => {
  test.skip(
    !process.env.WLB_TEST_URL || process.env.WLB_TEST_URL.endsWith(":47831"),
    "Use an isolated test database",
  );
});

test("tasks edit, complete, uncheck, plan and persist; calendar and narrow layout", async ({
  page,
  request,
}, info) => {
  const status = (await (await request.get("/api/v1/status")).json()) as Status;
  const today = status.tracker.today.date;
  const next = new Date(`${today}T12:00:00`);
  next.setDate(next.getDate() + 1);
  const tomorrow = `${next.getFullYear()}-${String(next.getMonth() + 1).padStart(2, "0")}-${String(next.getDate()).padStart(2, "0")}`;
  const name = `UI ${Date.now()}: Собрать сцену и проверить освещение`;
  const updated = `${name} готово`;
  const future = `План ${Date.now()}`;
  const session = (await (await request.get("/api/v1/session")).json()) as {
    token: string;
  };
  const headers = { "X-WLB-Token": session.token };
  try {
    await page.goto("/");
    const section = page.getByRole("region", { name: "Дела", exact: true });
    await section.getByLabel("Новая задача").fill(name);
    await section.getByLabel("Вес задачи").selectOption("5");
    await section.getByRole("button", { name: "Добавить задачу" }).click();
    const checkbox = section.getByRole("checkbox", {
      name: `Выполнено: ${name}`,
      exact: true,
    });
    await expect(checkbox).toBeVisible();
    await checkbox.check();
    await expect(checkbox).toBeChecked();
    await expect
      .poll(async () => {
        const data = (await (
          await request.get(
            `/api/v1/dashboard?date=${today}&month=${today.slice(0, 7)}`,
          )
        ).json()) as DashboardReport;
        return data.month.days.find((d) => d.date === today)?.taskWeight;
      })
      .toBe(5);
    await page.reload();
    await expect(checkbox).toBeChecked();
    await checkbox.uncheck();
    await expect(checkbox).not.toBeChecked();
    await section
      .getByRole("button", { name: `Изменить: ${name}`, exact: true })
      .click();
    await section.getByLabel("Название задачи").fill(updated);
    await section
      .locator(".todo-edit")
      .getByLabel("Вес задачи")
      .selectOption("4");
    await section.getByRole("button", { name: "Сохранить задачу" }).click();
    await section
      .getByRole("checkbox", { name: `Выполнено: ${updated}`, exact: true })
      .check();
    await section.getByLabel("Дата задач").fill(tomorrow);
    await expect(section.getByLabel("Новая задача")).toBeVisible();
    await section.getByLabel("Новая задача").fill(future);
    await section.getByRole("button", { name: "Добавить задачу" }).click();
    await expect(
      section.getByRole("checkbox", {
        name: `Выполнено: ${future}`,
        exact: true,
      }),
    ).toBeDisabled();
    await page.reload();
    // Date selection defaults back to today on page load; select the persisted plan.
    await section.getByLabel("Дата задач").fill(tomorrow);
    await expect(section.getByText(future, { exact: true })).toBeVisible();
    await section.getByLabel("Дата задач").fill(today);
    await expect(
      section.getByRole("checkbox", {
        name: `Выполнено: ${updated}`,
        exact: true,
      }),
    ).toBeChecked();
    const todayCell = page.locator('.calendar-day[aria-pressed="true"]');
    await expect(todayCell.locator(".task-half")).toHaveClass(/task-level-1/);
    await section.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath("todos-desktop.png") });
    await page.setViewportSize({ width: 390, height: 844 });
    await section.scrollIntoViewIfNeeded();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    await page.screenshot({ path: info.outputPath("todos-mobile.png") });
    await todayCell.scrollIntoViewIfNeeded();
    await page.screenshot({
      path: info.outputPath("todos-calendar-mobile.png"),
    });
    page.on("dialog", (dialog) => dialog.accept());
    await section
      .getByRole("button", { name: `Удалить: ${updated}`, exact: true })
      .click();
    await expect(section.getByText(updated, { exact: true })).toHaveCount(0);
  } finally {
    for (const date of [today, tomorrow]) {
      const data = (await (
        await request.get(`/api/v1/todos?date=${date}`)
      ).json()) as TodoDay;
      for (const item of data.items.filter((item) =>
        [name, updated, future].includes(item.title),
      ))
        await request.delete(`/api/v1/todos/${item.id}`, { headers });
    }
  }
});

test("past recap is read-only and overdue tasks are only on today", async ({
  page,
  request,
}) => {
  const { tracker } = (await (
    await request.get("/api/v1/status")
  ).json()) as Status;
  const date = new Date(`${tracker.today.date}T12:00:00`);
  date.setDate(date.getDate() - 1);
  const yesterday = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
  await page.goto("/");
  const section = page.getByRole("region", { name: "Дела", exact: true });
  await expect(
    section.getByText("Тест: перенесённая задача", { exact: true }),
  ).toBeVisible();
  await section.getByLabel("Дата задач").fill(yesterday);
  const done = section.getByRole("checkbox", {
    name: "Выполнено: Тест: вчерашняя задача",
    exact: true,
  });
  await expect(done).toBeChecked();
  await expect(done).toBeDisabled();
  await expect(
    section.getByText("Тест: перенесённая задача", { exact: true }),
  ).toHaveCount(0);
  await expect(section.getByLabel("Новая задача")).toHaveCount(0);
  await expect(section.getByRole("button")).toHaveCount(0);
  await expect(
    page.locator('.calendar-day[aria-pressed="true"] .task-half'),
  ).toHaveClass(/task-level-1/);
});

test("all effort colors and saturated split calendar fit a 320px screen", async ({
  page,
  request,
}, info) => {
  const { tracker } = (await (
    await request.get("/api/v1/status")
  ).json()) as Status;
  const date = tracker.today.date;
  const { token } = (await (await request.get("/api/v1/session")).json()) as {
    token: string;
  };
  const headers = { "X-WLB-Token": token };
  const prefix = `Weights-${Date.now()}-`;
  const ids: string[] = [];
  try {
    for (let weight = 1; weight <= 5; weight++) {
      expect(
        (
          await request.post("/api/v1/todos", {
            headers,
            data: {
              title: `${prefix}${weight} ПроверитьМатериалСОченьДлиннымНазванием`,
              weight,
              date,
            },
          })
        ).ok(),
      ).toBe(true);
    }
    const tasks = (await (
      await request.get(`/api/v1/todos?date=${date}`)
    ).json()) as TodoDay;
    for (const item of tasks.items.filter((item) =>
      item.title.startsWith(prefix),
    )) {
      ids.push(item.id);
      expect(
        (
          await request.post(`/api/v1/todos/${item.id}/completion`, {
            headers,
            data: { completed: true },
          })
        ).ok(),
      ).toBe(true);
    }
    await page.setViewportSize({ width: 320, height: 900 });
    await page.goto("/");
    const section = page.getByRole("region", { name: "Дела", exact: true });
    for (let weight = 1; weight <= 5; weight++)
      await expect(
        section.locator(`.effort-${weight}`).filter({ hasText: prefix }),
      ).toBeVisible();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    const fits = await section
      .locator(".todo-title")
      .evaluateAll((nodes) =>
        nodes.every((node) => node.scrollWidth <= node.clientWidth + 1),
      );
    expect(fits).toBe(true);
    await section.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath("todos-320.png") });
    const cell = page.locator('.calendar-day[aria-pressed="true"]');
    await expect(cell.locator(".task-half")).toHaveClass(/task-level-4/);
    await cell.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath("calendar-320.png") });
  } finally {
    for (const id of ids)
      await request.delete(`/api/v1/todos/${id}`, { headers });
  }
});

test("todo API validates input, forbids early completion and requires token", async ({
  request,
}) => {
  const { tracker } = (await (
    await request.get("/api/v1/status")
  ).json()) as Status;
  const { token } = (await (await request.get("/api/v1/session")).json()) as {
    token: string;
  };
  const headers = { "X-WLB-Token": token };
  const draft = { title: "API test", weight: 1, date: tracker.today.date };
  expect((await request.post("/api/v1/todos", { data: draft })).status()).toBe(
    403,
  );
  for (const invalid of [
    { ...draft, weight: 6 },
    { ...draft, title: " " },
    { ...draft, date: "1970-01-01" },
  ])
    expect(
      (
        await request.post("/api/v1/todos", { headers, data: invalid })
      ).status(),
    ).toBe(400);
  expect(
    (
      await request.post("/api/v1/todos", {
        headers,
        data: { ...draft, date: "9998-12-31" },
      })
    ).ok(),
  ).toBe(true);
  const future = (await (
    await request.get("/api/v1/todos?date=9998-12-31")
  ).json()) as TodoDay;
  const item = future.items.find((x) => x.title === draft.title)!;
  try {
    expect(
      (
        await request.post(`/api/v1/todos/${item.id}/completion`, {
          headers,
          data: { completed: true },
        })
      ).status(),
    ).toBe(400);
  } finally {
    await request.delete(`/api/v1/todos/${item.id}`, { headers });
  }
});
