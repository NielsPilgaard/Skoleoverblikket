import { test, expect, type Page } from '@playwright/test'

// One login, several roles (task 61): the seeded admin is also invited as a parent. The API links the
// invitation to the existing login, so "Vis som" appears, and switching shows the parent schedule.

const ADMIN_EMAIL = 'admin@debugskolen.dk'
const MAILPIT_API = 'http://localhost:8025/api/v1'

type Kc = { token: string; authenticated?: boolean; updateToken: (n: number) => Promise<boolean> }

/** Calls the API as the logged-in user, without X-Acting-As, so the API acts as its default role (staff). */
async function api<T>(page: Page, method: string, path: string, body?: unknown): Promise<T> {
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

test('a teacher who is also a parent switches with "Vis som" and back', async ({ page }) => {
  test.setTimeout(90_000)
  await page.goto('/dashboard')

  const klass = await api<{ id: string }>(page, 'POST', '/classes', { name: `E2E Vis som ${Date.now()}`, gradeLevel: 2 })
  const student = await api<{ id: string }>(page, 'POST', '/students', {
    name: 'Elev Vis Som',
    classId: klass.id,
    isEnrolledInSfo: false,
  })
  const parent = await api<{ id: string }>(page, 'POST', '/parents/invite', {
    name: 'Debug Admin',
    email: ADMIN_EMAIL,
    studentIds: [student.id],
  })

  try {
    await page.reload()
    await expect(page.getByTestId('role-switcher')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('role-switcher-staff')).toHaveAttribute('aria-pressed', 'true')

    await page.getByTestId('role-switcher-parent').click()
    await page.waitForURL(/\/foraeldrevisning\/skema/, { timeout: 15_000 })
    await expect(page.getByTestId('parent-schema-page')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('role-switcher-parent')).toHaveAttribute('aria-pressed', 'true')

    await page.getByTestId('role-switcher-staff').click()
    await page.waitForURL(/\/dashboard/, { timeout: 15_000 })
    await expect(page.getByTestId('dashboard-quick-actions')).toBeVisible({ timeout: 15_000 })
  } finally {
    // The admin's login is shared with their staff row, so removing the parent keeps it.
    await api(page, 'DELETE', `/parents/${parent.id}`)
  }

  await page.reload()
  await expect(page.getByTestId('dashboard-quick-actions')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByTestId('role-switcher')).toBeHidden()
})

/** The token from the newest parent invitation emailed to `email` after `since`. */
async function parentInvitationToken(page: Page, email: string, since: number): Promise<string> {
  for (let i = 0; i < 20; i++) {
    const list = (await (await page.request.get(`${MAILPIT_API}/messages`)).json()) as {
      messages?: { ID: string; Created: string; To: { Address: string }[] }[]
    }
    const msg = (list.messages ?? []).find(
      (m) => Date.parse(m.Created) >= since && m.To.some((to) => to.Address === email)
    )
    if (msg) {
      const detail = (await (await page.request.get(`${MAILPIT_API}/message/${msg.ID}`)).json()) as {
        HTML?: string
        Text?: string
      }
      const match = (detail.Text ?? detail.HTML ?? '').match(/\/parent-invitation\/([^"'\s<>]+)/)
      if (match) return match[1]
    }
    await page.waitForTimeout(500)
  }
  throw new Error(`No parent invitation email to ${email}`)
}

test('a teacher who accepts a parent invitation lands in the parent view', async ({ page }) => {
  test.setTimeout(90_000)
  await page.goto('/dashboard')

  const klass = await api<{ id: string }>(page, 'POST', '/classes', { name: `E2E Accept ${Date.now()}`, gradeLevel: 3 })
  const student = await api<{ id: string }>(page, 'POST', '/students', {
    name: 'Elev Accept',
    classId: klass.id,
    isEnrolledInSfo: false,
  })
  const invitedAt = Date.now() - 1_000
  const parent = await api<{ id: string }>(page, 'POST', '/parents/invite', {
    name: 'Debug Admin',
    email: ADMIN_EMAIL,
    studentIds: [student.id],
  })

  try {
    const token = await parentInvitationToken(page, ADMIN_EMAIL, invitedAt)
    // ?accept=1 is where Keycloak returns after login; the admin is already logged in.
    await page.goto(`/parent-invitation/${token}?accept=1`)

    // The contact step calls a parent-only endpoint, so it fails unless the page acts as the parent.
    await page.getByTestId('invitation-contact-submit').click({ timeout: 15_000 })
    await page.waitForURL(/\/foraeldrevisning\/skema/, { timeout: 15_000 })
    await expect(page.getByTestId('parent-schema-page')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('role-switcher-parent')).toHaveAttribute('aria-pressed', 'true')
  } finally {
    await api(page, 'DELETE', `/parents/${parent.id}`)
  }
})
