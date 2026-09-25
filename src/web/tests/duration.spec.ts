import { expect, test } from "@playwright/test";
import {
  calendarDuration,
  compactCalendarDuration,
  duration,
} from "../src/duration";

test("positive short durations never appear as zero", () => {
  for (const [seconds, label] of [
    [0.001, "<1 с"],
    [0.999, "<1 с"],
    [1, "1 с"],
    [9.002, "9 с"],
    [59.999, "59 с"],
  ] as const) {
    expect(calendarDuration(seconds)).toBe(label);
    expect(duration(seconds)).toBe(label);
  }
  expect(calendarDuration(0)).toBe("0");
  expect(duration(0)).toBe("0 ч 0 мин");
  expect(calendarDuration(60)).toBe("1 мин");
  expect(calendarDuration(3599)).toBe("59 мин");
  expect(calendarDuration(3600)).toBe("1.0 ч");
  expect(duration(3660)).toBe("1 ч 1 мин");
});

test("split calendar uses compact labels on narrow screens", () => {
  expect(compactCalendarDuration(0)).toBe("0");
  expect(compactCalendarDuration(0.001)).toBe("<1с");
  expect(compactCalendarDuration(59)).toBe("59с");
  expect(compactCalendarDuration(3599)).toBe("59м");
  expect(compactCalendarDuration(25 * 3600)).toBe("25ч");
});
