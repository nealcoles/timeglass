import { expect, test } from '@playwright/test';
import { daySummary, TODAY } from './demo.ts';

test("today's view shows each app's share of the active time", async ({ page, request }) => {
  const summary = await daySummary(request, TODAY);
  expect(summary.apps.length).toBeGreaterThan(1);

  await page.goto('/');

  await expect(page.getByRole('status').filter({ hasText: 'Tracker:' })).toHaveText(
    'Tracker: online',
  );
  await expect(page.getByRole('heading', { level: 2 })).toHaveText(
    'Today, Thursday, 5 November 2026',
  );

  // The demo day has five apps, so the list names every one, longest first.
  const rows = page
    .getByRole('table', { name: 'Active time per app, longest first' })
    .locator('tbody tr');
  const expected = [...summary.apps].sort((a, b) => b.durationMs - a.durationMs);
  await expect(rows).toHaveCount(expected.length);
  for (const [i, app] of expected.entries()) {
    const row = rows.nth(i);
    await expect(row.getByRole('rowheader')).toContainText(app.app.name);
    await expect(row.getByRole('cell').last()).toHaveText(`${app.percent}%`);
  }

  // The percentages on screen add up to 100.
  const shown = await rows.locator('.app-shares__percent').allTextContents();
  expect(shown.reduce((sum, p) => sum + Number.parseInt(p, 10), 0)).toBe(100);

  // The timeline is there, and the day ends at the stopped clock: 14:00 in Oslo.
  await expect(page.getByRole('heading', { name: 'When' })).toBeVisible();
  await expect(page.getByRole('definition').filter({ hasText: /^14:00$/ })).toBeVisible();
});
