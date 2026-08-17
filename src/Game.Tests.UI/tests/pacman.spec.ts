import { expect, test } from '@playwright/test';

test.describe('Pac-Man game (Game.Web static-SSR host)', () => {
  test('game select lists Pac-Man', async ({ page }) => {
    await page.goto('/');

    const select = page.locator('#game-select');
    await expect(select).toBeAttached();
    await expect(select.locator('option', { hasText: 'Pac-Man' })).toHaveCount(1);
  });

  test('Pac-Man route ships maze payload and stream URL', async ({ page }) => {
    await page.goto('/examples/games/pacman');

    const viewport = page.locator('#pixi-viewport');
    await expect(viewport).toBeAttached();
    const payload = await viewport.getAttribute('data-message');
    expect(payload).toBeTruthy();
    expect(payload).toContain('games/pacman');
    expect(payload).toContain('/api/pacman/stream');
    expect(payload).toContain('mazeRows');
  });

  test('Pac-Man mounts canvas, starts, accepts input without browser errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', message => {
      if (message.type() === 'error') errors.push(message.text());
    });
    page.on('pageerror', error => errors.push(String(error)));

    await page.goto('/examples/games/pacman');
    await expect(page.locator('#pixi-viewport canvas').first()).toBeVisible({ timeout: 20_000 });

    const startButton = page.getByRole('button', { name: 'START GAME' });
    await expect(startButton).toBeVisible();
    await startButton.click();
    await expect(startButton).toBeHidden();

    await page.keyboard.press('ArrowLeft');
    await page.keyboard.press('ArrowUp');
    await page.waitForTimeout(750);

    expect(errors, errors.join('\n')).toEqual([]);
  });
});
