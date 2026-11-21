import { test, expect, type Browser, type Page } from '@playwright/test'
import { totpCode } from './totp'

test.use({ storageState: { cookies: [], origins: [] } })

const FIRST_PASSWORD = 'TestPass123!'
const NEW_PASSWORD = 'NytKodeord456!'

/** A school of its own, so changing the password and two-factor touches no shared login. */
async function signUp(page: Page): Promise<string> {
  const email = `e2e-konto-${Date.now()}@testskole.dk`
  await page.goto('/signup')
  await page.getByTestId('signup-name').fill('Konto Testskole')
  await page.getByTestId('signup-first-name').fill('Anne')
  await page.getByTestId('signup-last-name').fill('Jensen')
  await page.getByTestId('signup-email').fill(email)
  await page.getByTestId('signup-password').fill(FIRST_PASSWORD)
  await page.getByTestId('signup-accept-dpa').check()
  await page.getByRole('button', { name: 'Opret skole' }).click()
  await expect(page).toHaveURL(/\/setup/, { timeout: 15_000 })
  return email
}

/**
 * A fresh browser with a real Keycloak login. Signup seeds tokens without an SSO session, and the
 * Min konto actions run on Keycloak's session.
 */
async function logIn(browser: Browser, email: string, password: string, otp?: string) {
  const context = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  const page = await context.newPage()
  await page.goto('/login')
  await page.waitForURL(/localhost:8080/, { timeout: 15_000 })
  await page.getByTestId('kc-username').fill(email)
  await page.getByTestId('kc-password').fill(password)
  await page.getByTestId('kc-login-submit').click()
  if (otp) {
    await page.getByTestId('kc-otp-input').fill(otp)
    await page.getByTestId('kc-otp-submit').click()
  }
  await page.waitForURL((url) => url.port !== '8080', { timeout: 30_000 })
  return page
}

async function backOnAccount(page: Page, notice: string) {
  await page.waitForURL(/\/min-konto/, { timeout: 30_000 })
  await expect(page.getByTestId('account-notice')).toContainText(notice, { timeout: 15_000 })
}

test('Min konto: change password, turn tofaktor on, log in with the code, turn it off', async ({
  page,
  browser,
}) => {
  test.setTimeout(240_000)
  const email = await signUp(page)

  // Change the password.
  let account = await logIn(browser, email, FIRST_PASSWORD)
  await account.goto('/min-konto')
  await expect(account.getByTestId('account-email')).toHaveText(email)
  await expect(account.getByTestId('account-status-off')).toBeVisible()
  await account.getByTestId('account-password').click()
  await account.getByTestId('kc-password-new').fill(NEW_PASSWORD)
  await account.getByTestId('kc-password-confirm').fill(NEW_PASSWORD)
  await account.getByTestId('kc-password-submit').click()
  await backOnAccount(account, 'Din adgangskode er skiftet.')
  await account.context().close()

  // The new password works. Turn on tofaktor with the key typed in by hand.
  account = await logIn(browser, email, NEW_PASSWORD)
  await account.goto('/min-konto')
  await account.getByTestId('account-tfa-on').click()
  await account.getByTestId('kc-totp-manual-mode').click()
  const secret = (await account.getByTestId('kc-totp-secret').textContent()) ?? ''
  expect(secret.trim()).not.toBe('')
  const setup = await totpCode(secret)
  await account.getByTestId('kc-totp-input').fill(setup.code)
  await account.getByTestId('kc-totp-submit').click()

  // Min konto goes straight on to the recovery codes.
  await expect(account.getByTestId('kc-recovery-codes')).toBeVisible({ timeout: 30_000 })
  await account.getByTestId('kc-recovery-codes-saved').check()
  await account.getByTestId('kc-recovery-codes-submit').click()
  await backOnAccount(account, 'Tofaktor er slået til.')
  await expect(account.getByTestId('account-status-on')).toBeVisible()
  await account.context().close()

  // A new login asks for the code. Keycloak refuses a code twice, so wait for the next one.
  const login = await totpCode(secret, setup.step)
  account = await logIn(browser, email, NEW_PASSWORD, login.code)
  await account.goto('/min-konto')
  await expect(account.getByTestId('account-status-on')).toBeVisible({ timeout: 15_000 })

  // Turn it off again.
  await account.getByTestId('account-tfa-off').click()
  await account.getByTestId('kc-delete-credential-confirm').click()
  await backOnAccount(account, 'Tofaktor er slået fra.')
  await expect(account.getByTestId('account-status-off')).toBeVisible()
  await account.context().close()

  // Back to password only: no code asked.
  account = await logIn(browser, email, NEW_PASSWORD)
  await expect(account.getByTestId('kc-otp-input')).toHaveCount(0)
  await account.context().close()
})
