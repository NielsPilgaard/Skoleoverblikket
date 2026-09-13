import { test, expect, type Page } from '@playwright/test'

// The shared storageState authenticates as an admin, so these cover the admin
// dashboard. Staff-side coverage (landing on /mig/oversigt) lives in the API
// integration tests, which can act as a non-admin staff principal directly.

// page.request does not carry the Keycloak bearer token. Use page.evaluate so the
// fetch runs inside the browser where window.__keycloak (exposed in dev mode) holds it.
// Same pattern as invitation.spec.ts.
async function apiFetch(
  page: Page,
  input: { url: string; method: string; body?: unknown },
): Promise<{ ok: boolean; status: number; body: unknown }> {
  return page.evaluate(async ({ url, method, body }) => {
    const kc = (window as unknown as { __keycloak: { token: string; updateToken: (n: number) => Promise<boolean> } })
      .__keycloak
    await kc.updateToken(30)
    const res = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${kc.token}` },
      body: body ? JSON.stringify(body) : undefined,
    })
    const text = await res.text()
    return { ok: res.ok, status: res.status, body: text ? JSON.parse(text) : null }
  }, input)
}

test.describe('Admin dashboard — quick actions and alerts', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/dashboard')
    await expect(page.getByRole('heading', { name: 'Oversigt' })).toBeVisible({ timeout: 15_000 })
  })

  test('quick actions are always shown and navigate to their targets', async ({ page }) => {
    const quickActions = page.getByTestId('dashboard-quick-actions')
    await expect(quickActions).toBeVisible()

    for (const label of ['Klasser', 'Medarbejdere', 'Kalender', 'Importer data']) {
      await expect(quickActions.getByRole('link', { name: new RegExp(label) })).toBeVisible()
    }

    await quickActions.getByRole('link', { name: /Medarbejdere/ }).click()
    await expect(page).toHaveURL(/\/medarbejdere$/)
  })

  test('an open vacation window renders as an alert tile', async ({ page }) => {
    const title = `E2E ferieindmelding ${Date.now()}`
    const today = new Date()
    const iso = (d: Date) => d.toISOString().slice(0, 10)
    const deadline = new Date(today)
    deadline.setDate(deadline.getDate() + 7)
    const start = new Date(today)
    start.setDate(start.getDate() + 30)
    const end = new Date(today)
    end.setDate(end.getDate() + 37)

    const created = await apiFetch(page, {
      url: '/api/v1/vacation-registration',
      method: 'POST',
      body: {
        title,
        registrationDeadline: iso(deadline),
        careStartDate: iso(start),
        careEndDate: iso(end),
        granularity: 'Weeks',
        isOpen: true,
      },
    })
    expect(created.ok, `Create vacation window failed: ${created.status}`).toBeTruthy()

    try {
      await page.goto('/dashboard')
      const tile = page.getByTestId('alert-vacation-window')
      await expect(tile).toBeVisible({ timeout: 15_000 })
      await expect(tile).toContainText(title)
    } finally {
      const windows = await apiFetch(page, { url: '/api/v1/vacation-registration', method: 'GET' })
      const match = (windows.body as { id: string; title: string }[] | null)?.find((w) => w.title === title)
      if (match) {
        await apiFetch(page, { url: `/api/v1/vacation-registration/${match.id}`, method: 'DELETE' })
      }
    }
  })

  test('vacation window alert tile is absent when no window is open', async ({ page }) => {
    const windows = await apiFetch(page, { url: '/api/v1/vacation-registration', method: 'GET' })
    const openWindows = (windows.body as { id: string; isOpen: boolean }[] | null)?.filter((w) => w.isOpen) ?? []
    test.skip(openWindows.length > 0, 'Tenant currently has an open vacation window — not a clean baseline')

    await expect(page.getByTestId('alert-vacation-window')).toHaveCount(0)
  })

  test('sidebar Oversigt link points at the admin dashboard', async ({ page }) => {
    const oversigt = page.getByRole('link', { name: 'Oversigt', exact: true }).first()
    await expect(oversigt).toHaveAttribute('href', '/dashboard')
  })
})
