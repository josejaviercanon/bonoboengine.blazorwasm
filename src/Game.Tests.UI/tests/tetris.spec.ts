import { expect, test } from '@playwright/test';

test.describe('Tetris game (Game.Web static-SSR host)', () => {
  test('game select lists Tetris', async ({ page }) => {
    await page.goto('/');

    const select = page.locator('#game-select');
    await expect(select).toBeAttached();
    await expect(select.locator('option', { hasText: 'Tetris' })).toHaveCount(1);
  });

  test('tetris route ships the SSR payload in #pixi-viewport[data-message]', async ({ page }) => {
    await page.goto('/examples/games/tetris');

    const viewport = page.locator('#pixi-viewport');
    await expect(viewport).toBeAttached();

    const payload = await viewport.getAttribute('data-message');
    expect(payload).toBeTruthy();
    expect(payload).toContain('games/tetris');
    expect(payload).toContain('/api/tetris/stream');
  });

  test('PixiJS bootstraps and mounts a canvas', async ({ page }) => {
    await page.goto('/examples/games/tetris');

    await expect(page.locator('#pixi-viewport canvas').first()).toBeVisible({ timeout: 20_000 });
  });

  test('start button hides the overlay and arrow keys play without console errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') errors.push(msg.text());
    });
    page.on('pageerror', err => errors.push(String(err)));

    await page.goto('/examples/games/tetris');
    await expect(page.locator('#pixi-viewport canvas').first()).toBeVisible({ timeout: 20_000 });

    // The DOM start overlay is present before starting.
    const startButton = page.getByRole('button', { name: 'START GAME' });
    await expect(startButton).toBeVisible();

    await startButton.click();
    await expect(startButton).toBeHidden();

    // Arrow keys steer the piece (client only suggests; C# validates).
    await page.keyboard.press('ArrowLeft');
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('ArrowUp');
    await page.keyboard.press('ArrowDown');
    await page.waitForTimeout(500);

    expect(errors, errors.join('\n')).toEqual([]);
  });
});