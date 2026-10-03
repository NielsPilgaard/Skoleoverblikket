import { test, expect, type APIRequestContext, type Browser, type Page } from '@playwright/test'

// Fravær end to end: a teacher (the seeded admin, who has a staff row) notes fremmøde on a phone,
// and an invited parent sees the ulovligt fravær; the parent requests fri and the office approves.
// The parent is a real account created through the parent invitation email in Mailpit.

const MAILPIT_API = 'http://localhost:8025/api/v1'

type Kc = { token: string; authenticated?: boolean; updateToken: (n: number) => Promise<boolean> }

async function api<T>(page: Page, method: string, path: string, body?: unknown): Promise<T> {
  // Right after a navigation keycloak-js may still be initialising, with no refresh token yet.
  await page.waitForFunction(() => (window as unknown as { __keycloak?: Kc }).__keycloak?.authenticated === true)
  const result = await page.evaluate(
    async ({ method, path, body }) => {
      const kc = (window as unknown as { __keycloak: Kc }).__keycloak
      await kc.updateToken(30)
      const res = await fetch(`/api/v1${path}`, {
        method,
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${kc.token}` },
        body: body === undefined ? undefined : JSON.stringify(body),
      })
      const text = await res.text()
      return { ok: res.ok, status: res.status, json: text ? JSON.parse(text) : null }
    },
    { method, path, body }
  )
  expect(result.ok, `${method} ${path} failed with ${result.status}`).toBeTruthy()
  return result.json as T
}

/** yyyy-mm-dd in Danish time. */
function danishDate(offsetDays = 0): string {
  const d = new Date(Date.now() + offsetDays * 86_400_000)
  return new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Copenhagen' }).format(d)
}

/** The latest weekday up to today in the current quarter, or null if the quarter so far is a weekend. */
function recentSchoolDay(): string | null {
  const today = danishDate()
  const quarterStartMonth = Math.floor((Number(today.slice(5, 7)) - 1) / 3) * 3 + 1
  const quarterStart = `${today.slice(0, 4)}-${String(quarterStartMonth).padStart(2, '0')}-01`
  for (let i = 0; i < 7; i++) {
    const day = danishDate(-i)
    if (day < quarterStart) return null
    const weekday = new Date(`${day}T12:00:00`).getDay()
    if (weekday !== 0 && weekday !== 6) return day
  }
  return null
}

async function findMail(request: APIRequestContext, to: string): Promise<string> {
  for (let i = 0; i < 20; i++) {
    const res = await request.get(`${MAILPIT_API}/messages`)
    const body = (await res.json()) as { messages?: { ID: string; To: { Address: string }[] }[] }
    const msg = (body.messages ?? []).find((m) => m.To.some((t) => t.Address === to))
    if (msg) {
      const detail = (await (await request.get(`${MAILPIT_API}/message/${msg.ID}`)).json()) as {
        HTML?: string
        Text?: string
      }
      return `${detail.Text ?? ''}\n${detail.HTML ?? ''}`
    }
    await new Promise((r) => setTimeout(r, 500))
  }
  throw new Error(`No email to ${to}`)
}

/** Invites a parent for the student and logs them in through the invitation. Returns their page. */
async function parentSession(
  admin: Page,
  browser: Browser,
  request: APIRequestContext,
  studentId: string
): Promise<Page> {
  const email = `foraelder-${Date.now()}@testskole.dk`
  await api(admin, 'POST', '/parents/invite', { name: 'E2E Forælder', email, studentIds: [studentId] })

  const mail = await findMail(request, email)
  const link = mail.match(/http[^"'\s<>]+\/parent-invitation\/[^"'\s<>]+/)?.[0].replace(/&amp;/g, '&')
  const tempPassword = mail
    .match(/midlertidige adgangskode[^:]*:\s*([^\s\n<]+)/i)?.[1]
    .replace(/<[^>]+>/g, '')
    .trim()
  expect(link, 'No parent invitation link').toBeDefined()
  expect(tempPassword, 'No temporary password').toBeDefined()

  const context = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  const page = await context.newPage()
  await page.goto(link!)
  await page.waitForURL((url) => url.port === '5173', { timeout: 30_000 })
  await page.getByRole('button', { name: /opret konto|acceptér/i }).click()
  await page.waitForURL(/localhost:8080/, { timeout: 15_000 })
  await page.locator('#username').fill(email)
  await page.locator('#password').fill(tempPassword!)
  await page.getByRole('button', { name: /log ind|sign in/i }).click()

  await page.waitForURL(/localhost:8080.*password|update-password/i, { timeout: 10_000 }).catch(() => {})
  const newPass = page.locator('#password-new, input[name="password-new"]')
  if (await newPass.isVisible({ timeout: 5_000 }).catch(() => false)) {
    await newPass.fill('NewPass456!')
    await page.locator('#password-confirm, input[name="password-confirm"]').fill('NewPass456!')
    await page.getByRole('button', { name: /gem|submit|opdater|update/i }).click()
  }

  await page.waitForURL(/\/parent-invitation\//, { timeout: 20_000 })
  await page.getByRole('button', { name: /spring over/i }).click()
  await page.waitForURL(/\/foraeldrevisning\//, { timeout: 20_000 })
  return page
}

async function createClassWithStudent(admin: Page, name: string) {
  const klass = await api<{ id: string }>(admin, 'POST', '/classes', { name, gradeLevel: 3 })
  const student = await api<{ id: string }>(admin, 'POST', '/students', {
    name: `Elev ${name}`,
    classId: klass.id,
    isEnrolledInSfo: false,
  })
  return { classId: klass.id, studentId: student.id }
}

test.describe.serial('Fravær', () => {
  test('teacher notes fremmøde on a phone, parent sees ulovligt fravær', async ({ page, browser, request }) => {
    test.setTimeout(180_000)
    const day = recentSchoolDay()
    test.skip(day === null, 'The quarter so far is only a weekend — no school day to note.')

    await page.goto('/dashboard')
    const { classId, studentId } = await createClassWithStudent(page, `E2E ${Date.now()}`)

    await page.setViewportSize({ width: 390, height: 844 })
    await page.goto(`/fravaer/fremmoede/${classId}?dato=${day}`)
    await page.getByTestId(`attendance-student-${studentId}`).click()
    await expect(page.getByTestId(`attendance-category-${studentId}-Unauthorized`)).toBeVisible()
    await page.getByTestId('attendance-save').click()
    await expect(page.getByTestId('attendance-saved')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('attendance-check-status')).toContainText('Noteret')

    const parent = await parentSession(page, browser, request, studentId)
    await parent.goto('/foraeldrevisning/fravaer')
    const records = parent.locator('[data-testid^="parent-absence-record-"]')
    await expect(records).toHaveCount(1, { timeout: 15_000 })
    await expect(records.first()).toContainText('Ulovligt fravær')
    await expect(records.first()).toContainText('Noteret af skolen')
    await expect(parent.getByTestId('parent-absence-retention')).toBeVisible()
    await parent.context().close()
  })

  test('parent requests fri, the office approves, parent sees it approved', async ({ page, browser, request }) => {
    test.setTimeout(180_000)
    await page.goto('/dashboard')
    const className = `E2E fri ${Date.now()}`
    const { studentId } = await createClassWithStudent(page, className)
    const parent = await parentSession(page, browser, request, studentId)

    await parent.goto('/foraeldrevisning/fravaer')
    await parent.getByTestId('parent-absence-new').click()
    await parent.getByTestId('parent-absence-kind-ExtraordinaryLeave').click()
    await parent.getByTestId('parent-absence-child').selectOption(studentId)
    await parent.getByTestId('parent-absence-reason').fill('Familiefest')
    await parent.getByTestId('parent-absence-submit').click()
    const record = parent.locator('[data-testid^="parent-absence-record-"]').first()
    await expect(record).toContainText('afventer godkendelse', { timeout: 15_000 })

    await page.goto('/fravaer?fane=fri')
    const request_ = page.locator('[data-testid^="leave-request-"]').filter({ hasText: `Elev ${className}` })
    await expect(request_.first()).toBeVisible({ timeout: 15_000 })
    await request_.first().locator('[data-testid^="leave-approve-"]').click()
    await expect(request_).toHaveCount(0, { timeout: 15_000 })

    await parent.reload()
    await expect(parent.locator('[data-testid^="parent-absence-record-"]').first()).toContainText('godkendt', {
      timeout: 15_000,
    })
    await parent.context().close()
  })
})
