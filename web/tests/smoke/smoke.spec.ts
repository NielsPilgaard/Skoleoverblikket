import { expect, test } from '@playwright/test'

// Runs against production as the admin of "Smoketest Friskole" (a school with only fake
// data). Read-only: it never clicks anything that saves.
const user = process.env.SMOKE_USER
const password = process.env.SMOKE_PASSWORD

test("admin logs in, opens a klasse's ugeplan, and logs out", async ({ page }) => {
  if (!user || !password) throw new Error('SMOKE_USER and SMOKE_PASSWORD must be set')

  await page.goto('/login')
  // Keycloak's own login page, so its field ids rather than data-testid.
  await page.waitForURL(/\/realms\/Skoleoverblikket\//, { timeout: 30_000 })
  await page.locator('#username').fill(user)
  await page.locator('#password').fill(password)
  await page.getByRole('button', { name: /log ind|sign in/i }).click()

  await expect(page.getByTestId('admin-dashboard')).toBeVisible({ timeout: 30_000 })

  // Through Klasser rather than Mit skema: Mit skema is empty unless the admin teaches a lektion.
  await page.goto('/klasser')
  await page.locator('[data-testid^="class-expand-"]').first().click({ timeout: 20_000 })
  await page.locator('[data-testid^="class-ugeplan-"]').first().click({ timeout: 20_000 })
  await expect(page.getByTestId('weekplan-grid')).toBeVisible({ timeout: 20_000 })

  await page.getByTestId('logout-button').click()
  await page.waitForURL((url) => url.pathname === '/', { timeout: 30_000 })
})
