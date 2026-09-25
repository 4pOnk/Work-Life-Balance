import { expect, test } from "@playwright/test";
import type { Settings, Status } from "../src/api";

test("real local UI: processes, settings, AFK, pause and responsive rendering", async ({
  page,
  request,
}, info) => {
  const initial = (await (
    await request.get("/api/v1/status")
  ).json()) as Status;
  const session = (await (await request.get("/api/v1/session")).json()) as {
    token: string;
  };
  const headers = { "X-WLB-Token": session.token };
  const errors: string[] = [];
  const external: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("request", (req) => {
    if (
      new URL(req.url()).origin !==
      new URL(process.env.WLB_TEST_URL ?? "http://127.0.0.1:47831").origin
    )
      external.push(req.url());
  });
  try {
    await page.goto("/");
    await expect(
      page.getByRole("heading", { name: "Сегодня", exact: true }),
    ).toBeVisible();
    await expect(page.getByText("Учёт активен", { exact: true })).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Удалить blender.exe из рабочих" }),
    ).toBeVisible();
    await page
      .getByRole("textbox", { name: "Имя рабочего процесса" })
      .fill("iteration-test.exe");
    await page.getByRole("button", { name: "Добавить", exact: true }).click();
    await expect(
      page.getByRole("button", {
        name: "Удалить iteration-test.exe из рабочих",
      }),
    ).toBeVisible();
    await page
      .getByRole("button", { name: "Удалить iteration-test.exe из рабочих" })
      .click();
    await expect(
      page.getByRole("button", {
        name: "Удалить iteration-test.exe из рабочих",
      }),
    ).toHaveCount(0);
    await page
      .getByRole("button", { name: "Включить AFK", exact: true })
      .click();
    await expect(page.getByText("Сейчас AFK", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Вернуться", exact: true }).click();
    await expect(page.getByText("Учёт активен", { exact: true })).toBeVisible();
    await page
      .getByRole("button", { name: "Приостановить учёт", exact: true })
      .click();
    await expect(
      page.getByText("Учёт приостановлен", { exact: true }),
    ).toBeVisible();
    await page
      .getByRole("button", { name: "Включить AFK", exact: true })
      .click();
    await expect(
      page.getByText("Учёт приостановлен", { exact: true }),
    ).toBeVisible();
    await page
      .getByRole("button", { name: "Возобновить учёт", exact: true })
      .click();
    await page.getByLabel("Время без ввода до AFK").fill("120");
    await page.getByRole("button", { name: "Сохранить", exact: true }).click();
    await expect(page.getByText("Настройки сохранены")).toBeVisible();
    await page.reload();
    await expect(page.getByLabel("Время без ввода до AFK")).toHaveValue("120");
    await page.screenshot({
      path: info.outputPath("desktop.png"),
      fullPage: true,
    });
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(
      page.getByRole("button", { name: "Включить AFK", exact: true }),
    ).toBeVisible();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= window.innerWidth,
      ),
    ).toBe(true);
    await page.screenshot({
      path: info.outputPath("mobile.png"),
      fullPage: true,
    });
    expect(errors).toEqual([]);
    expect(external).toEqual([]);
  } finally {
    await request.put("/api/v1/settings", {
      headers,
      data: initial.tracker.settings,
    });
    const current = (await (
      await request.get("/api/v1/status")
    ).json()) as Status;
    if (current.tracker.state.paused)
      await request.post("/api/v1/pause", { headers });
    const after = (await (
      await request.get("/api/v1/status")
    ).json()) as Status;
    if (after.tracker.state.presence !== "active")
      await request.post("/api/v1/afk", { headers });
  }
});

test("API rejects hostile origins, missing tokens and invalid configuration", async ({
  request,
}) => {
  expect((await request.post("/api/v1/afk")).status()).toBe(403);
  expect(
    (
      await request.get("/api/v1/status", {
        headers: { Origin: "https://example.com" },
      })
    ).status(),
  ).toBe(403);
  expect(
    (
      await request.get("/api/v1/status", {
        headers: { Host: "evil.example:47831" },
      })
    ).status(),
  ).toBe(403);
  const session = (await (await request.get("/api/v1/session")).json()) as {
    token: string;
  };
  const status = (await (await request.get("/api/v1/status")).json()) as Status;
  const invalid: Settings = { ...status.tracker.settings, afkMinutes: -1 };
  expect(
    (
      await request.put("/api/v1/settings", {
        headers: { "X-WLB-Token": session.token },
        data: invalid,
      })
    ).status(),
  ).toBe(400);
});
