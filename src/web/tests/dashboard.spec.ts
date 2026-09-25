import { expect, test } from "@playwright/test";
import type { DashboardReport, Status } from "../src/api";

test("calendar, hourly and application totals agree, navigation and mobile", async ({
  page,
  request,
}, info) => {
  const status = (await (await request.get("/api/v1/status")).json()) as Status;
  const date = status.tracker.today.date;
  const data = (await (
    await request.get(
      `/api/v1/dashboard?date=${date}&month=${date.slice(0, 7)}`,
    )
  ).json()) as DashboardReport;
  expect(data.day.hours.reduce((sum, x) => sum + x.workSeconds, 0)).toBeCloseTo(
    data.day.report.workSeconds,
    5,
  );
  expect(data.day.apps.reduce((sum, x) => sum + x.afkSeconds, 0)).toBeCloseTo(
    data.day.report.afkSeconds,
    5,
  );
  expect(
    data.month.days.reduce((sum, x) => sum + x.totals.workSeconds, 0),
  ).toBeCloseTo(data.month.totals.workSeconds, 5);
  expect(
    data.month.weeks.reduce((sum, x) => sum + x.totals.restSeconds, 0),
  ).toBeCloseTo(data.month.totals.restSeconds, 5);
  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Календарь активности" }),
  ).toBeVisible();
  await expect(page.locator(".calendar-day")).toHaveCount(
    data.month.days.length,
  );
  await expect(page.locator(".hour-chart svg")).toBeVisible();
  await expect(page.locator(".recharts-bar-rectangle").first()).toBeAttached();
  await page.screenshot({
    path: info.outputPath("dashboard-desktop.png"),
    fullPage: true,
  });
  await page.getByLabel("Предыдущий месяц", { exact: true }).click();
  await expect(page.getByLabel("Месяц отчёта")).not.toHaveValue(
    date.slice(0, 7),
  );
  await page.locator(".calendar-day.empty").first().click();
  await expect(
    page.getByText("За этот день нет записанной активности."),
  ).toBeVisible();
  await expect(page.getByLabel("Дата истории")).not.toHaveValue(date);
  await page.getByRole("button", { name: "Сегодня", exact: true }).click();
  await expect(page.getByLabel("Дата истории")).toHaveValue(date);
  await page.setViewportSize({ width: 390, height: 844 });
  await page
    .getByRole("heading", { name: "Календарь активности" })
    .scrollIntoViewIfNeeded();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({ path: info.outputPath("dashboard-mobile.png") });
});

test("dashboard rejects invalid and overflowing dates", async ({ request }) => {
  for (const date of ["nonsense", "9999-12-31", "2026-02-30"])
    expect(
      (
        await request.get(`/api/v1/dashboard?date=${date}&month=2026-09`)
      ).status(),
    ).toBe(400);
});
