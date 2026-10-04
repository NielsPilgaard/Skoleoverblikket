import { test, expect } from '@playwright/test'

test.use({ storageState: { cookies: [], origins: [] } })

function uniqueEmail() {
  return `e2e-${Date.now()}@testskole.dk`
}

test('signup creates a school and redirects to setup wizard', async ({ page }) => {
  const email = uniqueEmail()

  await page.goto('/signup')

  await page.getByTestId('signup-name').fill('E2E Testskole')
  await page.getByTestId('signup-first-name').fill('Anne')
  await page.getByTestId('signup-last-name').fill('Jensen')
  await page.getByTestId('signup-email').fill(email)
  await page.getByTestId('signup-password').fill('TestPass123!')
  await page.getByTestId('signup-accept-dpa').check()

  await page.getByRole('button', { name: 'Opret skole' }).click()

  // Should land on the setup wizard — wait up to 15 s for the redirect + Keycloak init
  await expect(page).toHaveURL(/\/setup/, { timeout: 15_000 })

  // The wizard should be visible — first step heading
  await expect(page.getByRole('heading', { name: /skoledag/i })).toBeVisible({ timeout: 10_000 })
})

test('signup shows error when submitting duplicate email', async ({ page }) => {
  // Use the seeded debug admin account from the realm config
  const existingEmail = 'admin@debugskolen.dk'

  await page.goto('/signup')

  await page.getByTestId('signup-name').fill('Duplikat Skole')
  await page.getByTestId('signup-first-name').fill('Debug')
  await page.getByTestId('signup-last-name').fill('Admin')
  await page.getByTestId('signup-email').fill(existingEmail)
  await page.getByTestId('signup-password').fill('TestPass123!')
  await page.getByTestId('signup-accept-dpa').check()

  await page.getByRole('button', { name: 'Opret skole' }).click()

  // Should stay on /signup and say the email is taken — never reuse the existing account
  await expect(page).toHaveURL('/signup', { timeout: 10_000 })
  await expect(page.getByTestId('signup-email-error')).toContainText('allerede en bruger', {
    timeout: 5_000,
  })
})

test('signup requires accepting the databehandleraftale', async ({ page }) => {
  await page.goto('/signup')

  await page.getByTestId('signup-name').fill('DPA Testskole')
  await page.getByTestId('signup-first-name').fill('Anne')
  await page.getByTestId('signup-last-name').fill('Jensen')
  await page.getByTestId('signup-email').fill(uniqueEmail())
  await page.getByTestId('signup-password').fill('TestPass123!')

  const submit = page.getByRole('button', { name: 'Opret skole' })
  await expect(submit).toBeDisabled()

  await page.getByTestId('signup-accept-dpa').check()
  await expect(submit).toBeEnabled()
})

test('databehandleraftale and sub-processor list are public', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByTestId('footer-link-databehandleraftale')).toHaveAttribute(
    'href',
    '/databehandleraftale'
  )

  await page.goto('/databehandleraftale')
  await expect(page.getByRole('heading', { name: 'Databehandleraftale', level: 1 })).toBeVisible()

  await page.goto('/underdatabehandlere')
  await expect(page.getByTestId('sub-processor-list')).toContainText('OVHcloud')
})
