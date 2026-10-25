import { test, expect, type Page } from '@playwright/test'

test.use({ storageState: { cookies: [], origins: [] } })

const MAILPIT_API = 'http://localhost:8025/api/v1'
const ADMIN_EMAIL = 'admin@debugskolen.dk'
const ADMIN_PASSWORD = 'test1234'

function uniqueStaffEmail() {
  return `staff-${Date.now()}@testskole.dk`
}

async function loginAsAdmin(page: Page) {
  await page.goto('/login')
  await page.waitForURL(/localhost:8080/, { timeout: 15_000 })
  await page.locator('#username').fill(ADMIN_EMAIL)
  await page.locator('#password').fill(ADMIN_PASSWORD)
  await page.getByRole('button', { name: /log ind|sign in/i }).click()
  await page.waitForURL((url) => url.port !== '8080', { timeout: 60_000 })
  await expect(page).toHaveURL(/\/(dashboard|backoffice)/, { timeout: 20_000 })
}

test.describe.serial('Staff invitation flow', () => {
  test('invited staff member can log in and lands on their own schedule, not dashboard', async ({ page, request }) => {
    test.setTimeout(120_000)
    const staffEmail = uniqueStaffEmail()
    const staffName = 'Test Medarbejder'

    // --- Step 1: admin logs in ---
    await loginAsAdmin(page)

    // --- Step 2: admin creates staff member via API ---
    // page.request does not carry the Keycloak bearer token. Use page.evaluate so the
    // fetch runs inside the browser where window.__keycloak (exposed in dev mode) holds the token.
    const createResult = await page.evaluate(async ({ staffName, staffEmail }: { staffName: string; staffEmail: string }) => {
      const kc = (window as unknown as { __keycloak: { token: string; updateToken: (n: number) => Promise<boolean> } }).__keycloak
      await kc.updateToken(30)
      const res = await fetch('/api/v1/staff', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${kc.token}` },
        body: JSON.stringify({ name: staffName, email: staffEmail, role: 'Teacher' }),
      })
      return { ok: res.ok, status: res.status, body: await res.json() }
    }, { staffName, staffEmail })
    expect(createResult.ok, `Create staff failed: ${createResult.status}`).toBeTruthy()
    const staff = createResult.body as { id: string }

    // --- Step 3: admin sends invitation ---
    const inviteResult = await page.evaluate(async (staffId: string) => {
      const kc = (window as unknown as { __keycloak: { token: string; updateToken: (n: number) => Promise<boolean> } }).__keycloak
      await kc.updateToken(30)
      const res = await fetch(`/api/v1/staff-invitations/invite/${staffId}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${kc.token}` },
        body: '{}',
      })
      return { ok: res.ok, status: res.status }
    }, staff.id)
    expect(inviteResult.ok, `Send invitation failed: ${inviteResult.status}`).toBeTruthy()

    // --- Step 4: extract invitation link from Mailpit ---
    // Retry a few times in case email delivery is slightly delayed
    let invitationUrl: string | undefined
    for (let i = 0; i < 10; i++) {
      const mailRes = await request.get(`${MAILPIT_API}/messages`)
      const mailBody = await mailRes.json() as { messages?: { ID: string; To: { Address: string }[]; Subject: string }[] }
      const msg = (mailBody.messages ?? []).find((m) =>
        m.To.some((to) => to.Address === staffEmail)
      )
      if (msg) {
        const detail = await request.get(`${MAILPIT_API}/message/${msg.ID}`)
        const detailBody = await detail.json() as { HTML?: string; Text?: string }
        const content = detailBody.HTML ?? detailBody.Text ?? ''
        const match = content.match(/http[^"'\s<>]+\/invitation\/[^"'\s<>]+/)
        if (match) { invitationUrl = match[0].replace(/&amp;/g, '&'); break }
      }
      await page.waitForTimeout(500)
    }
    expect(invitationUrl, 'No invitation link found in email').toBeDefined()

    // --- Step 5: open invitation page in a fresh browser context (not logged in) ---
    // A new context has no cookies or localStorage, so Keycloak starts completely fresh.
    const browser = page.context().browser()!
    const freshContext = await browser.newContext({ storageState: { cookies: [], origins: [] } })
    const freshPage = await freshContext.newPage()

    await freshPage.goto(invitationUrl!)

    // Invitation page should load with heading visible.
    // check-sso may redirect through Keycloak and back — wait until back on 5173 and heading appears.
    await freshPage.waitForURL((url) => url.hostname === 'localhost' && url.port === '5173', { timeout: 30_000 })
    await expect(freshPage.getByRole('heading', { name: /inviteret/i })).toBeVisible({ timeout: 15_000 })

    // --- Step 6: choose a password on the invitation page ---
    // No password is emailed; the invitee picks their own and is logged in right away.
    await freshPage.getByTestId('invitation-password').fill('NewPass456!')
    await freshPage.getByTestId('invitation-password-submit').click()

    // --- Step 7: the page reloads with ?accept=1 and accepts the invitation ---
    await freshPage.waitForURL(/\/invitation\//, { timeout: 20_000 })
    await expect(freshPage.getByText(/invitation accepteret/i)).toBeVisible({ timeout: 10_000 })

    // --- Step 8: click "Gå til mit skema" — must land on /mig/skema, NOT /dashboard ---
    const apiErrors: { url: string; status: number }[] = []
    freshPage.on('response', (res) => {
      if (res.url().includes('/api/v1/') && res.status() === 403) {
        apiErrors.push({ url: res.url(), status: res.status() })
      }
    })

    await freshPage.getByRole('link', { name: /gå til mit skema/i }).click()
    await expect(freshPage).toHaveURL(/\/mig\/skema/, { timeout: 15_000 })

    // Wait for all deferred queries to settle
    await freshPage.waitForTimeout(2_000)
    await freshContext.close()
    expect(
      apiErrors,
      `Got 403 errors on staff schedule page:\n${apiErrors.map((e) => e.url).join('\n')}`
    ).toHaveLength(0)
  })
})
