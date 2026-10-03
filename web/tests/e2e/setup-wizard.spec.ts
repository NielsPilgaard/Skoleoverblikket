import { test, expect, type Page } from '@playwright/test'

// Signup creates a new account — must not carry global auth state or Keycloak will skip the flow.
test.use({ storageState: { cookies: [], origins: [] } })

function uniqueEmail() {
  return `e2e-wizard-${Date.now()}@testskole.dk`
}

// Sign up a fresh school via the UI and land on /setup with a seeded token.
// This exercises the real path: POST /api/v1/tenants → Keycloak user creation →
// password-grant token (with tenant_id + admin role) → sessionStorage seed → /setup.
async function signupAndLandOnWizard(page: Page) {
  const email = uniqueEmail()

  await page.goto('/signup')
  await page.getByTestId('signup-name').fill('E2E Wizard Skole')
  await page.getByTestId('signup-first-name').fill('Anne')
  await page.getByTestId('signup-last-name').fill('Jensen')
  await page.getByTestId('signup-email').fill(email)
  await page.getByTestId('signup-password').fill('TestPass123!')
  await page.getByTestId('signup-accept-dpa').check()
  await page.getByRole('button', { name: 'Opret skole' }).click()

  await expect(page).toHaveURL(/\/setup/, { timeout: 20_000 })
  await expect(page.getByRole('heading', { name: /skoledag/i })).toBeVisible({ timeout: 15_000 })
}

test.describe('Setup wizard — new user', () => {
  test('signup lands on wizard step 1 with a working auth token', async ({ page }) => {
    await signupAndLandOnWizard(page)
    await expect(page.getByText(/trin 1 af/i)).toBeVisible()
  })

  test('step 1: saving school day settings calls the API without 401/403', async ({ page }) => {
    await signupAndLandOnWizard(page)

    // Capture any failed API responses before clicking
    const failedRequests: { url: string; status: number }[] = []
    page.on('response', (res) => {
      if (res.url().includes('/api/') && (res.status() === 401 || res.status() === 403)) {
        failedRequests.push({ url: res.url(), status: res.status() })
      }
    })

    await page.getByRole('button', { name: /gem og fortsæt/i }).click()

    await expect(page.getByRole('heading', { name: /hvordan vil du oprette resten/i })).toBeVisible({
      timeout: 10_000,
    })
    expect(failedRequests, `Unauthorized API calls: ${JSON.stringify(failedRequests)}`).toHaveLength(0)
  })

  test('full wizard: skip all steps and reach dashboard', async ({ page }) => {
    await signupAndLandOnWizard(page)

    // Step 1: skip school day
    await expect(page.getByRole('heading', { name: /skoledag/i })).toBeVisible({ timeout: 10_000 })
    await page.getByRole('button', { name: /spring over/i }).click()

    // Step 2: choose manual creation method
    await expect(
      page.getByRole('heading', { name: /hvordan vil du oprette resten/i })
    ).toBeVisible({ timeout: 10_000 })
    await page.getByText(/jeg opretter det manuelt/i).click()

    // Steps 3–5: skip each in sequence
    const stepHeadings = [/klasser/i, /lokaler/i, /medarbejdere/i]
    for (const heading of stepHeadings) {
      await expect(page.getByRole('heading', { name: heading })).toBeVisible({ timeout: 10_000 })
      await page.getByRole('button', { name: /spring over/i }).click()
    }

    await expect(page.getByRole('heading', { name: /din skole er sat op/i })).toBeVisible({ timeout: 10_000 })
    await page.getByRole('button', { name: /gå til oversigt/i }).click()
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 })
  })

  test('full wizard: create class, then reach dashboard', async ({ page }) => {
    await signupAndLandOnWizard(page)

    // Step 1: skip school day
    await expect(page.getByRole('heading', { name: /skoledag/i })).toBeVisible({ timeout: 10_000 })
    await page.getByRole('button', { name: /spring over/i }).click()

    // Step 2: choose manual creation method
    await expect(
      page.getByRole('heading', { name: /hvordan vil du oprette resten/i })
    ).toBeVisible({ timeout: 10_000 })
    await page.getByText(/jeg opretter det manuelt/i).click()

    // Step 3: create a class
    await expect(page.getByRole('heading', { name: /klasser/i })).toBeVisible({ timeout: 10_000 })
    await page.locator('input[placeholder="fx 1.a"]').first().fill('1.a')
    await page.getByRole('button', { name: /opret og fortsæt/i }).click()

    // Step 4: skip rooms
    await expect(page.getByRole('heading', { name: /lokaler/i })).toBeVisible({ timeout: 10_000 })
    await page.getByRole('button', { name: /spring over/i }).click()

    // Step 5: skip staff
    await expect(page.getByRole('heading', { name: /medarbejdere/i })).toBeVisible({ timeout: 10_000 })
    await page.getByRole('button', { name: /spring over/i }).click()

    // Step 6: done
    await expect(page.getByRole('heading', { name: /din skole er sat op/i })).toBeVisible({ timeout: 10_000 })
    await page.getByRole('button', { name: /gå til oversigt/i }).click()
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 })
  })
})
