import { test, expect, type APIRequestContext, type Browser, type Page } from '@playwright/test'

// Links end to end: the admin adds a link only for one class's parents, a parent in that class
// sees it on their phone and it opens in a new tab, a parent in another class does not see it.
// Parents are real accounts created through the parent invitation email in Mailpit.

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
  const email = `links-foraelder-${Date.now()}@testskole.dk`
  await api(admin, 'POST', '/parents/invite', { name: 'E2E Forælder', email, studentIds: [studentId] })

  const mail = await findMail(request, email)
  const link = mail.match(/http[^"'\s<>]+\/parent-invitation\/[^"'\s<>]+/)?.[0].replace(/&amp;/g, '&')
  expect(link, 'No parent invitation link').toBeDefined()

  const context = await browser.newContext({
    storageState: { cookies: [], origins: [] },
    viewport: { width: 390, height: 844 },
  })
  const page = await context.newPage()
  await page.goto(link!)
  await page.waitForURL((url) => url.port === '5173', { timeout: 30_000 })
  await page.getByTestId('invitation-password').fill('NewPass456!')
  await page.getByTestId('invitation-password-submit').click()

  await page.waitForURL(/\/parent-invitation\/.*accept=1/, { timeout: 20_000 })
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

test('admin adds a class link, only parents in that class see it', async ({ page, browser, request }) => {
  test.setTimeout(180_000)
  const stamp = Date.now()
  await page.goto('/dashboard')
  const own = await createClassWithStudent(page, `E2E links ${stamp}`)
  const other = await createClassWithStudent(page, `E2E links anden ${stamp}`)
  const title = `Klassens Facebook ${stamp}`

  await page.goto('/links')
  await page.getByTestId('link-add').click()
  await page.getByTestId('link-title-input').fill(title)
  await page.getByTestId('link-url-input').fill('facebook.com/groups/e2e')
  await page.getByTestId('link-description-input').fill('Kun for klassens forældre')
  await expect(page.getByTestId('link-audience-parents')).toBeChecked()
  await page.getByTestId('link-classes-some').check()
  await page.getByTestId(`link-class-${own.classId}`).check()
  await page.getByTestId('link-save').click()

  const row = page.getByTestId('link-row').filter({ hasText: title })
  await expect(row).toBeVisible({ timeout: 15_000 })
  await expect(row).toContainText('facebook.com')
  await expect(row).toContainText(`E2E links ${stamp}`)

  const ownParent = await parentSession(page, browser, request, own.studentId)
  await ownParent.goto('/foraeldrevisning/links')
  const link = ownParent.getByTestId('external-link').filter({ hasText: title })
  await expect(link).toBeVisible({ timeout: 15_000 })
  await expect(link).toHaveAttribute('href', 'https://facebook.com/groups/e2e')
  await expect(link).toHaveAttribute('target', '_blank')
  await expect(link).toHaveAttribute('rel', /noopener/)
  await ownParent.context().close()

  const otherParent = await parentSession(page, browser, request, other.studentId)
  await otherParent.goto('/foraeldrevisning/links')
  await expect(otherParent.getByRole('heading', { name: 'Links' })).toBeVisible({ timeout: 15_000 })
  await expect(otherParent.getByTestId('links-list').or(otherParent.getByTestId('links-empty'))).toBeVisible()
  await expect(otherParent.getByTestId('external-link').filter({ hasText: title })).toHaveCount(0)
  await otherParent.context().close()
})
