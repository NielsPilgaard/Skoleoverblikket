import { expect, test } from '@playwright/test'

// Runs against production as the admin of "Smoketest Friskole" (a school with only fake
// data). Read-only: it never clicks anything that saves.
const user = process.env.SMOKE_USER
const password = process.env.SMOKE_PASSWORD

test('admin logs in, sees their schema and a ugeplan, and logs out', async ({ page }) => {
  if (!user || !password) throw new Error('SMOKE_USER and SMOKE_PASSWORD must be set')

  await page.goto('/login')
  // Keycloak's own login page, so its field ids rather than data-testid.
  await page.waitForURL(/\/realms\/Skoleoverblikket\//, { timeout: 30_000 })
  await page.locator('#username').fill(user)
  await page.locator('#password').fill(password)
  await page.getByRole('button', { name: /log ind|sign in/i }).click()

  await expect(page.getByTestId('admin-dashboard')).toBeVisible({ timeout: 30_000 })

  await page.goto('/mig/skema')
  await expect(page.getByTestId('my-schedule-grid')).toBeVisible({ timeout: 20_000 })

  // A lektion opens its klasse's ugeplan.
  await page.getByTestId('my-schedule-slot').first().click()
  await expect(page.getByTestId('weekplan-grid')).toBeVisible({ timeout: 20_000 })

  await page.getByTestId('logout-button').click()
  await page.waitForURL((url) => url.pathname === '/', { timeout: 30_000 })
})
