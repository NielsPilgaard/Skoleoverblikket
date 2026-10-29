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

  // Opens klasser one at a time until one has a schema, so it doesn't matter which klasse the
  // smoke school keeps its schema on.
  await page.goto('/klasser')
  const expandButtons = page.locator('[data-testid^="class-expand-"]')
  await expect(expandButtons.first()).toBeVisible({ timeout: 20_000 })
  const ugeplanButton = page.locator('[data-testid^="class-ugeplan-"]').first()
  for (const expand of await expandButtons.all()) {
    await expand.click()
    // A klasse's schemas load after it expands, so wait a little before trying the next one.
    const hasSchema = await ugeplanButton
      .waitFor({ state: 'visible', timeout: 5_000 })
      .then(() => true)
      .catch(() => false)
    if (hasSchema) break
  }
  await ugeplanButton.click()
  await expect(page.getByTestId('weekplan-grid')).toBeVisible({ timeout: 20_000 })

  await page.getByTestId('logout-button').click()
  await page.waitForURL((url) => url.pathname === '/', { timeout: 30_000 })
})
