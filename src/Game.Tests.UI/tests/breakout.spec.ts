import { expect, test } from '@playwright/test';

test.describe('Breakout game (Game.Web static-SSR host)', () => {
  test('game select lists Breakout', async ({ page }) => {
    await page.goto('/');

    const select = page.locator('#game-select');
    await expect(select).toBeAttached();
    await expect(select.locator('option', { hasText: 'Breakout' })).toHaveCount(1);
  });

  test('breakout route ships the SSR payload in #pixi-viewport[data-message]', async ({ page }) => {
    await page.goto('/examples/games/breakout');

    const viewport = page.locator('#pixi-viewport');
    await expect(viewport).toBeAttached();

    const payload = await viewport.getAttribute('data-message');
    expect(payload).toBeTruthy();
    expect(payload).toContain('games/breakout');
    expect(payload).toContain('/api/breakout/stream');
  });

  test('PixiJS bootstraps and mounts a canvas', async ({ page }) => {
    await page.goto('/examples/games/breakout');

    await expect(page.locator('#pixi-viewport canvas').first()).toBeVisible({ timeout: 20_000 });
  });

  test('court geometry builds from the SSR payload (board actually rendered)', async ({ page }) => {
    await page.goto('/examples/games/breakout');

    const viewport = page.locator('#pixi-viewport');
    await expect(viewport).toHaveAttribute('data-court-bounds', /^\d+x\d+$/, { timeout: 20_000 });
    const bounds = (await viewport.getAttribute('data-court-bounds')) ?? '0x0';
    const [w, h] = bounds.split('x').map(Number);
    expect(w).toBeGreaterThan(0);
    expect(h).toBeGreaterThan(0);
  });

  test('start button hides the overlay and arrow keys play without console errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') errors.push(msg.text());
    });
    page.on('pageerror', err => errors.push(String(err)));

    await page.goto('/examples/games/breakout');
    await expect(page.locator('#pixi-viewport canvas').first()).toBeVisible({ timeout: 20_000 });

    // The DOM start overlay is present before starting.
    const startButton = page.getByRole('button', { name: 'START GAME' });
    await expect(startButton).toBeVisible();

    await startButton.click();
    await expect(startButton).toBeHidden();

    // Space launches the ball; arrow keys steer the paddle (client only suggests; C# validates).
    await page.keyboard.press('ArrowLeft');
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('Space');
    await page.waitForTimeout(1000);

    expect(errors, errors.join('\n')).toEqual([]);
  });
});