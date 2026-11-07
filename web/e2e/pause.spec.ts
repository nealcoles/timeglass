import { expect, test } from '@playwright/test';

test('pauses tracking for an hour and resumes it', async ({ page, request }) => {
  try {
    await page.goto('/');

    await page.getByText('Pause', { exact: true }).click();
    await page.getByRole('button', { name: 'For an hour' }).click();

    // The clock is stopped at 14:00 in Oslo, so an hour from now is 15:00.
    await expect(page.getByText('Paused until 15:00')).toBeVisible();
    const status = await (await request.get('/api/status')).json();
    expect(status.state).toBe('paused');

    await page.getByRole('button', { name: 'Resume' }).click();
    await expect(page.getByText('Pause', { exact: true })).toBeVisible();
    await expect(page.getByText(/Paused/)).toHaveCount(0);
  } finally {
    // The dev host is shared by every test: never leave it paused.
    await request.post('/api/resume');
  }
});
