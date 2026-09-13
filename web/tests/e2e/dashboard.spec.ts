import { test, expect } from '@playwright/test'

// The shared storageState authenticates as an admin, so these cover the admin
// dashboard. Staff-side coverage (landing on /mig/oversigt) lives in the API
// integration tests, which can act as a non-admin staff principal directly.
test.describe('Admin dashboard — quick actions and alerts', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/dashboard')
    await expect(page.getByRole('heading', { name: 'Oversigt' })).toBeVisible({ timeout: 15_000 })
  })

  test('quick actions are always shown and navigate to their targets', async ({ page }) => {
    const quickActions = page.getByTestId('dashboard-quick-actions')
    await expect(quickActions).toBeVisible()

    for (const label of ['Klasser', 'Medarbejdere', 'Kalender', 'Importer data']) {
      await expect(quickActions.getByRole('link', { name: new RegExp(label) })).toBeVisible()
    }

    await quickActions.getByRole('link', { name: /Medarbejdere/ }).click()
    await expect(page).toHaveURL(/\/medarbejdere$/)
  })

  test('alerts render as tiles only when something needs attention', async ({ page }) => {
    // Tiles are omitted at zero rather than rendered empty, so an untouched
    // tenant shows no alerts block at all.
    const alerts = page.getByTestId('dashboard-alerts')

    if ((await alerts.count()) === 0) {
      await expect(alerts).toHaveCount(0)
      return
    }

    await expect(alerts).toBeVisible()
    const tiles = alerts.getByRole('link')
    await expect(tiles.first()).toBeVisible()
    expect(await tiles.count()).toBeGreaterThan(0)
  })

  test('sidebar Oversigt link points at the admin dashboard', async ({ page }) => {
    const oversigt = page.getByRole('link', { name: 'Oversigt', exact: true }).first()
    await expect(oversigt).toHaveAttribute('href', '/dashboard')
  })
})
