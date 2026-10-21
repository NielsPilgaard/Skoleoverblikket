import { test, expect, type Page } from '@playwright/test'

// One login, several roles (task 61): the seeded admin is also invited as a parent. The API links the
// invitation to the existing login, so "Vis som" appears, and switching shows the parent schedule.

const ADMIN_EMAIL = 'admin@debugskolen.dk'

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
