import { test, expect, type Page } from '@playwright/test'

// Vikardækning: the office reports a teacher absent on their behalf, opens the absence and covers
// a lektion with a suggested vikar. Uses the seeded school, where staff have schema lektioner.

type Kc = { token: string; updateToken: (n: number) => Promise<boolean> }

async function api<T>(page: Page, path: string): Promise<T> {
  return page.evaluate(async (path) => {
    const kc = (window as unknown as { __keycloak: Kc }).__keycloak
    await kc.updateToken(30)
    const res = await fetch(`/api/v1${path}`, { headers: { Authorization: `Bearer ${kc.token}` } })
    return res.json()
  }, path) as Promise<T>
}

test('office reports a teacher absent and assigns a vikar to a lektion', async ({ page }) => {
  test.setTimeout(120_000)
  await page.goto('/vikardaekning')
  await expect(page.getByRole('heading', { name: 'Vikardækning' })).toBeVisible({ timeout: 15_000 })

  // Pick a teacher who has lektioner (the seed gives most teachers a full week) and isn't already
  // absent today: a second absence for the same day is rejected, so reruns need a fresh teacher.
  const staff = await api<{ id: string; name: string; role: string }[]>(page, '/staff')
  const today = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Copenhagen' }).format(new Date())
  const absences = await api<{ staffId: string }[]>(
    page,
    `/staff-absences?from=${today}&to=${today}`
  )
  const absentIds = new Set(absences.map((a) => a.staffId))
  const teacher = staff.find(
    (s) => s.role === 'Teacher' && s.name !== 'Debug Admin' && !absentIds.has(s.id)
  )
  test.skip(!teacher, 'The seeded school has no teacher who is free today.')

  await page.getByTestId('cover-report-for-staff').click()
  await page.getByTestId('cover-report-staff').selectOption(teacher!.id)
  await page.getByTestId('cover-report-submit').click()

  const absence = page.locator('[data-testid^="cover-absence-"]').filter({ hasText: teacher!.name }).first()
  await expect(absence).toBeVisible({ timeout: 15_000 })
  await absence.click()
  await expect(page.getByRole('heading', { name: teacher!.name })).toBeVisible({ timeout: 15_000 })

  const pickers = page.locator('[data-testid^="cover-candidates-"]')
  test.skip((await pickers.count()) === 0, 'No uncovered lektioner today for this teacher.')

  const picker = pickers.first()
  const lessonTestId = (await picker.getAttribute('data-testid'))!.replace('cover-candidates-', 'cover-lesson-')
  const option = picker.locator('option').nth(1)
  const vikarName = ((await option.textContent()) ?? '').replace(' (vikar)', '').trim()
  await picker.selectOption({ index: 1 })

  await expect(page.getByTestId(lessonTestId)).toContainText(vikarName, { timeout: 15_000 })
  await expect(page.getByTestId(lessonTestId.replace('cover-lesson-', 'cover-unassign-'))).toBeVisible()
})
