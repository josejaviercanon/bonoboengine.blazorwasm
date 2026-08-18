import { expect, test } from '@playwright/test';

test.describe('Snake game (Game.Web static-SSR host)', () => {
  test('game select lists Snake', async ({ page }) => {
    await page.goto('/');

    const select = page.locator('#game-select');
    await expect(select).toBeAttached();
    await expect(select.locator('option', { hasText: 'Snake' })).toHaveCount(1);
  });

  test('Snake route ships temporal sprite payload and stream URL', async ({ page }) => {
    await page.goto('/examples/games/snake');

    const viewport = page.locator('#pixi-viewport');
    await expect(viewport).toBeAttached();
    const payload = await viewport.getAttribute('data-message');
    expect(payload).toBeTruthy();
    expect(payload).toContain('games/snake');
    expect(payload).toContain('/api/snake/stream');
    expect(payload).toContain('previousX');
    expect(payload).toContain('kind');
  });

  test('Snake bootstraps, starts and accepts controls without browser errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', message => {
      if (message.type() === 'error') errors.push(message.text());
    });
    page.on('pageerror', error => errors.push(String(error)));

    await page.goto('/examples/games/snake');
    await expect(page.locator('#pixi-viewport canvas').first()).toBeVisible({ timeout: 20_000 });

    const startButton = page.getByRole('button', { name: 'START GAME' });
    await expect(startButton).toBeVisible();
    await startButton.click();
    await expect(startButton).toBeHidden();

    await page.keyboard.press('ArrowUp');
    await page.keyboard.press('ArrowRight');
    await page.waitForTimeout(1000);

    expect(errors, errors.join('\n')).toEqual([]);
  });
});
