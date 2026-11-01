import { test, expect } from '@playwright/test'

// While a deploy replaces the API, Traefik sends API calls to the maintenance container, which
// answers 503 with X-Maintenance (infrastructure/maintenance/nginx.conf). A tab that was already
// open must hold its requests behind a notice and carry on afterwards, not show errors.

test('an open tab waits out a deploy and carries on', async ({ page }) => {
  await page.goto('/lokaler')
  await expect(page.getByTestId('nav-link-dashboard')).toBeVisible({ timeout: 15_000 })

  let deploying = true
  await page.route('**/api/**', (route) =>
    deploying
      ? route.fulfill({
          status: 503,
          headers: { 'X-Maintenance': '1' },
          contentType: 'application/problem+json',
          body: '{"title":"Skoleoverblikket opdateres. Prøv igen om lidt.","status":503}',
        })
      : route.fallback()
  )

  await page.getByTestId('nav-link-dashboard').click()
  await expect(page.getByTestId('outage-notice')).toBeVisible()

  const stats = page.waitForResponse(
    (res) => res.url().includes('/api/v1/stats/dashboard') && res.status() === 200
  )
  deploying = false
  await stats

  await expect(page.getByTestId('outage-notice')).toBeHidden()
  // Only rendered when the stats query did not fail.
  await expect(page.getByTestId('admin-dashboard')).toBeVisible()
})
