import { expect, test } from '@playwright/test';

test.describe('Endless Race Runner (Game.Web static-SSR host)', () => {
  test('game select lists racer', async ({ page }) => {
    await page.goto('/');

    const select = page.locator('#game-select');
    await expect(select).toBeAttached();
    await expect(select.locator('option', { hasText: 'Endless Race Runner' })).toHaveCount(1);
  });

  test('route ships track, car and SSE payload', async ({ page }) => {
    await page.goto('/examples/games/racer');

    const viewport = page.locator('#pixi-viewport');
    await expect(viewport).toBeAttached();
    const payload = await viewport.getAttribute('data-message');
    expect(payload).toBeTruthy();
    expect(payload).toContain('games/racer');
    expect(payload).toContain('/api/racer/stream');
    expect(payload).toContain('segments');
    expect(payload).toContain('cars');
  });

  test('PixiJS mounts racer canvas and builds road geometry', async ({ page }) => {
    await page.goto('/examples/games/racer');

    await expect(page.locator('#pixi-viewport canvas').first()).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('#pixi-viewport')).toHaveAttribute('data-racer-bounds', /^\d+x\d+$/, {
      timeout: 20_000,
    });
    const configButton = page.locator('#racer-config-button');
    const panel = page.locator('#racer-tuning-panel');
    await expect(configButton).toBeVisible();
    await expect(panel).toBeHidden();
    await configButton.click();
    await expect(panel).toBeVisible();
    await expect(panel).toContainText('Racer tuning (paused)');
    await panel.getByRole('button', { name: 'Cancel' }).click();
    await expect(panel).toBeHidden();
  });

  test('keyboard input and tweak controls produce no client errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', message => {
      if (message.type() === 'error') errors.push(message.text());
    });
    page.on('pageerror', error => errors.push(String(error)));

    await page.goto('/examples/games/racer');
    await expect(page.locator('#pixi-viewport canvas').first()).toBeVisible({ timeout: 20_000 });
    await page.keyboard.down('ArrowUp');
    await page.keyboard.down('ArrowRight');
    await page.waitForTimeout(500);
    await page.keyboard.up('ArrowRight');
    await page.keyboard.up('ArrowUp');

    await page.locator('#racer-config-button').click();
    await expect(page.locator('#racer-tuning-panel')).toBeVisible();
    await page.locator('input[type="range"]').nth(0).fill('4');
    await page.locator('#racer-tuning-panel').getByRole('button', { name: 'Apply' }).click();
    await expect(page.locator('#racer-tuning-panel')).toBeHidden();
    await page.waitForTimeout(500);

    expect(errors, errors.join('\n')).toEqual([]);
  });
});
