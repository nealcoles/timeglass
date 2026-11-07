import { expect, test } from '@playwright/test';

test('a changed setting is saved and still there after a reload', async ({ page, request }) => {
  const before = await (await request.get('/api/settings')).json();

  try {
    await page.goto('/');
    await page.getByRole('link', { name: 'Settings' }).click();
    await expect(page).toHaveURL('/settings');

    const minutes = page.getByRole('spinbutton');
    await expect(minutes).toBeVisible();
    await minutes.fill('7.5');
    await page.getByRole('button', { name: 'Save settings' }).click();
    await expect(page.getByText('Saved. The new settings apply right away.')).toBeVisible();

    await page.reload();
    await expect(page.getByRole('spinbutton')).toHaveValue('7.5');
    const after = await (await request.get('/api/settings')).json();
    expect(after.idleThresholdSeconds).toBe(450);

    // A value the app can't use is caught before it is sent.
    await page.getByRole('spinbutton').fill('0');
    await page.getByRole('button', { name: 'Save settings' }).click();
    await expect(page.getByText('Some settings need fixing; see above.')).toBeVisible();
  } finally {
    // The dev host is shared by every test: put the settings back.
    const restored = await request.put('/api/settings', { data: before });
    expect(restored.ok()).toBe(true);
  }
});
