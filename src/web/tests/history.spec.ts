import { expect, test } from "@playwright/test";
import type { DayReport, Status } from "../src/api";

test("partial browser correction, persistence, undo and mobile editor", async ({
  page,
  request,
}, info) => {
  const status = (await (await request.get("/api/v1/status")).json()) as Status;
  const session = (await (await request.get("/api/v1/session")).json()) as {
    token: string;
  };
  const headers = { "X-WLB-Token": session.token };
  const date = status.tracker.today.date;
  const original = (await (
    await request.get(`/api/v1/history?date=${date}`)
  ).json()) as DayReport;
  const lesson = original.intervals.find(
    (s) => s.browser?.title === "Тестовый урок: геометрия",
  );
  expect(lesson, "Run --seed against a new test database first").toBeTruthy();
  let actionId: string | undefined;
  try {
    await page.goto("/");
    const row = page
      .locator(".history-row")
      .filter({ hasText: "Тестовый урок: геометрия" });
    await expect(row).toHaveCount(1);
    await row.getByRole("button", { name: /Изменить интервал/ }).click();
    const from = lesson!.start + 60000;
    const local = new Date(from - new Date(from).getTimezoneOffset() * 60000)
      .toISOString()
      .slice(0, 23)
      // datetime-local normalizes trailing fractional zeros before Playwright verifies the value.
      .replace(/(\.\d*?)0+$/, "$1")
      .replace(/\.$/, "");
    await page.getByLabel("Начало", { exact: true }).fill(local);
    await page
      .getByRole("dialog")
      .getByRole("combobox", { name: "Категория", exact: true })
      .selectOption("work");
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({
      path: info.outputPath("mobile-editor.png"),
      fullPage: true,
    });
    const responsePromise = page.waitForResponse(
      (r) =>
        r.url().endsWith("/api/v1/corrections") &&
        r.request().method() === "POST",
    );
    await page.getByRole("button", { name: "Применить к диапазону" }).click();
    actionId = ((await (await responsePromise).json()) as { actionId: string })
      .actionId;
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(row).toHaveCount(2);
    const corrected = (await (
      await request.get(`/api/v1/history?date=${date}`)
    ).json()) as DayReport;
    expect(corrected.workSeconds).toBeGreaterThanOrEqual(
      original.workSeconds + 60,
    );
    expect(
      corrected.intervals
        .filter((s) => s.browser?.title === lesson!.browser!.title)
        .reduce((sum, s) => sum + s.end - s.start, 0),
    ).toBe(120000);
    await page.reload();
    await expect(
      page.getByText("Ручное исправление", { exact: true }),
    ).toBeVisible();
    await page
      .getByRole("button", { name: "Отменить это исправление", exact: true })
      .click();
    actionId = undefined;
    await expect(row).toHaveCount(1);
    await expect(
      page.getByText("Ручное исправление", { exact: true }),
    ).toHaveCount(0);
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    await page.screenshot({
      path: info.outputPath("mobile-history.png"),
      fullPage: true,
    });
  } finally {
    if (actionId)
      await request.delete(`/api/v1/corrections/${actionId}`, { headers });
  }
});

test("batch correction and historical rules preserve manual choices", async ({
  request,
}) => {
  const state = (await (await request.get("/api/v1/status")).json()) as Status;
  const session = (await (await request.get("/api/v1/session")).json()) as {
    token: string;
  };
  const headers = { "X-WLB-Token": session.token };
  const date = state.tracker.today.date;
  const history = (await (
    await request.get(`/api/v1/history?date=${date}`)
  ).json()) as DayReport;
  const rows = history.intervals.filter(
    (s) => s.browser?.domain === "example.com",
  );
  expect(rows.length).toBe(2);
  const response = await request.post("/api/v1/corrections", {
    headers,
    data: {
      category: "work",
      ranges: rows.map((s) => ({ start: s.start, end: s.end })),
    },
  });
  expect(response.ok()).toBe(true);
  const { actionId } = (await response.json()) as { actionId: string };
  try {
    expect(
      (
        await request.put("/api/v1/settings?scope=history", {
          headers,
          data: { ...state.tracker.settings, workProcesses: [] },
        })
      ).ok(),
    ).toBe(true);
    const updated = (await (
      await request.get(`/api/v1/history?date=${date}`)
    ).json()) as DayReport;
    expect(
      updated.intervals
        .filter((s) => s.correctionId === actionId)
        .every((s) => s.category === "work"),
    ).toBe(true);
    expect(
      updated.intervals.some(
        (s) => s.executable === "blender.exe" && s.category === "rest",
      ),
    ).toBe(true);
  } finally {
    await request.delete(`/api/v1/corrections/${actionId}`, { headers });
    await request.put("/api/v1/settings?scope=history", {
      headers,
      data: state.tracker.settings,
    });
  }
});
