import { expect, test } from "@playwright/test";
import type { DayReport, Status } from "../src/api";

test("backup, export, confirmed delete and restore on an isolated database", async ({
  page,
  request,
}, info) => {
  test.skip(
    !process.env.WLB_TEST_URL || process.env.WLB_TEST_URL.endsWith(":47831"),
    "Destructive checks require an explicit isolated test server",
  );
  const session = (await (await request.get("/api/v1/session")).json()) as {
    token: string;
  };
  const headers = { "X-WLB-Token": session.token };
  const initial = (await (
    await request.get("/api/v1/status")
  ).json()) as Status;
  const date = initial.tracker.today.date;
  if (!initial.tracker.state.paused)
    expect((await request.post("/api/v1/pause", { headers })).ok()).toBe(true);
  const original = (await (
    await request.get(`/api/v1/history?date=${date}`)
  ).json()) as DayReport;
  const copy = (await (
    await request.post("/api/v1/storage/backup", { headers })
  ).json()) as { id: string };
  try {
    expect(
      (
        await request.post("/api/v1/storage/delete", {
          headers,
          data: { all: true, confirmation: "" },
        })
      ).status(),
    ).toBe(400);
    expect(
      (
        await request.post("/api/v1/storage/delete", {
          data: { all: true, confirmation: "УДАЛИТЬ" },
        })
      ).status(),
    ).toBe(403);
    const json = await request.get(
      `/api/v1/export?from=${date}&to=${date}&format=json`,
    );
    expect(json.ok()).toBe(true);
    expect((await json.json()).days[0].workSeconds).toBeCloseTo(
      original.workSeconds,
      4,
    );
    const csv = await request.get(
      `/api/v1/export?from=${date}&to=${date}&format=csv`,
    );
    expect(csv.headers()["content-disposition"]).toContain("attachment");
    expect(await csv.text()).toContain("executable,url,domain,title,source");
    await page.goto("/");
    const downloadPromise = page.waitForEvent("download");
    await page.getByRole("button", { name: "Экспорт", exact: true }).click();
    expect((await downloadPromise).suggestedFilename()).toBe(
      `work-life-balance-${date}-${date}.csv`,
    );
    await expect(page.getByText("Экспорт подготовлен")).toBeVisible();
    await page
      .getByRole("heading", { name: "Данные и запуск" })
      .scrollIntoViewIfNeeded();
    await page
      .getByRole("button", { name: "Удалить всю историю", exact: true })
      .click();
    await expect(
      page.getByRole("button", { name: "Подтвердить", exact: true }),
    ).toBeDisabled();
    await page.getByLabel("Подтверждение операции с историей").fill("УДАЛИТЬ");
    await page.screenshot({
      path: info.outputPath("storage-desktop.png"),
      fullPage: true,
    });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.locator(".storage-confirm").scrollIntoViewIfNeeded();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    await page.screenshot({ path: info.outputPath("storage-mobile.png") });
    await page
      .getByRole("button", { name: "Подтвердить", exact: true })
      .click();
    await expect(
      page.getByText("История обновлена. Страховочная копия сохранена."),
    ).toBeVisible();
    const empty = (await (
      await request.get(`/api/v1/history?date=${date}`)
    ).json()) as DayReport;
    expect(empty.intervals).toHaveLength(0);
    await page
      .getByRole("button", {
        name: `Восстановить копию ${copy.id}`,
        exact: true,
      })
      .click();
    await page
      .getByLabel("Подтверждение операции с историей")
      .fill("ВОССТАНОВИТЬ");
    await page
      .getByRole("button", { name: "Подтвердить", exact: true })
      .click();
    await expect(page.locator(".storage-confirm")).toHaveCount(0);
    const restored = (await (
      await request.get(`/api/v1/history?date=${date}`)
    ).json()) as DayReport;
    expect(restored.intervals).toEqual(original.intervals);
  } finally {
    await request.post("/api/v1/storage/restore", {
      headers,
      data: { id: copy.id, confirmation: "ВОССТАНОВИТЬ" },
    });
    if (!initial.tracker.state.paused)
      await request.post("/api/v1/pause", { headers });
  }
});
