import { expect, type Browser, type BrowserContext, type Page } from '@playwright/test'

const MAILPIT_API = 'http://localhost:8025/api/v1'

type Kc = { token: string; authenticated?: boolean; updateToken: (n: number) => Promise<boolean> }

/** Calls the API as the logged-in user, without X-Acting-As, so the API acts as its default role (staff). */
export async function api<T>(page: Page, method: string, path: string, body?: unknown): Promise<T> {
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

/** The token from the newest parent invitation emailed to `email` after `since`. */
export async function parentInvitationToken(page: Page, email: string, since: number): Promise<string> {
  for (let i = 0; i < 20; i++) {
    const list = (await (await page.request.get(`${MAILPIT_API}/messages`)).json()) as {
      messages?: { ID: string; Created: string; To: { Address: string }[] }[]
    }
    // The mailbox is shared by all workers, so other mail to the same address can arrive after `since`.
    const candidates = (list.messages ?? []).filter(
      (m) => Date.parse(m.Created) >= since && m.To.some((to) => to.Address === email)
    )
    for (const msg of candidates) {
      const detail = (await (await page.request.get(`${MAILPIT_API}/message/${msg.ID}`)).json()) as {
        HTML?: string
        Text?: string
      }
      const match = `${detail.Text ?? ''}\n${detail.HTML ?? ''}`.match(/\/parent-invitation\/([^"'\s<>]+)/)
      if (match) return match[1]
    }
    await page.waitForTimeout(500)
  }
  throw new Error(`No parent invitation email to ${email}`)
}

export type ParentLogin = {
  parentId: string
  parentName: string
  studentName: string
  context: BrowserContext
  page: Page
  /** Closes the parent's browser and deletes the parent. */
  dispose: () => Promise<void>
}

/**
 * A new klasse, student and parent, with the parent logged in through the real invitation flow
 * in their own browser. `adminPage` must be logged in as the seeded admin.
 */
export async function createParentLogin(adminPage: Page, browser: Browser, baseURL: string): Promise<ParentLogin> {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1000)}`
  const studentName = `Elev ${stamp}`
  const parentName = `Forælder ${stamp}`
  const email = `parent-${stamp}@testskole.dk`

  const klass = await api<{ id: string }>(adminPage, 'POST', '/classes', { name: `E2E ${stamp}`, gradeLevel: 4 })
  const student = await api<{ id: string }>(adminPage, 'POST', '/students', {
    name: studentName,
    classId: klass.id,
    isEnrolledInSfo: false,
  })
  const invitedAt = Date.now() - 1_000
  const parent = await api<{ id: string }>(adminPage, 'POST', '/parents/invite', {
    name: parentName,
    email,
    studentIds: [student.id],
  })
  const token = await parentInvitationToken(adminPage, email, invitedAt)

  const context = await browser.newContext({ baseURL, storageState: { cookies: [], origins: [] } })
  const page = await context.newPage()
  await page.goto(`/parent-invitation/${token}`)
  await page.getByTestId('invitation-password').fill('ForaelderPass123!', { timeout: 30_000 })
  await page.getByTestId('invitation-password-submit').click()
  await page.getByTestId('invitation-contact-submit').click({ timeout: 30_000 })
  await page.waitForURL(/\/foraeldrevisning\/skema/, { timeout: 15_000 })

  return {
    parentId: parent.id,
    parentName,
    studentName,
    context,
    page,
    dispose: async () => {
      await context.close()
      await api(adminPage, 'DELETE', `/parents/${parent.id}`)
    },
  }
}
