import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { test, expect, type Page } from '@playwright/test'

// The lesson editor saves by itself, and "Næste" walks through the week. A silent break here means
// teachers lose what they wrote, so this covers saving, moving on, undo and a failed save.

type Kc = { token: string; updateToken: (n: number) => Promise<boolean> }

async function api<T>(page: Page, path: string): Promise<T> {
  return page.evaluate(async (path) => {
    const kc = (window as unknown as { __keycloak: Kc }).__keycloak
    await kc.updateToken(30)
    const res = await fetch(`/api/v1${path}`, { headers: { Authorization: `Bearer ${kc.token}` } })
    return res.json()
  }, path) as Promise<T>
}

/** Monday of the week the ugeplan opens on (the coming one on a weekend), as local midnight. */
function planningMonday(): Date {
  const now = new Date()
  const weekday = now.getDay()
  const offset = weekday === 0 ? 1 : weekday === 6 ? 2 : 1 - weekday
  return new Date(now.getFullYear(), now.getMonth(), now.getDate() + offset)
}

function isoWeekOf(date: Date): { isoYear: number; isoWeek: number } {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  d.setUTCDate(d.getUTCDate() + 4 - (d.getUTCDay() || 7))
  const yearStart = Date.UTC(d.getUTCFullYear(), 0, 1)
  return {
    isoYear: d.getUTCFullYear(),
    isoWeek: Math.ceil(((d.getTime() - yearStart) / 86400000 + 1) / 7),
  }
}

/** A seeded class with at least two lektioner this week. */
async function openClassWithLessons(page: Page) {
  await page.setViewportSize({ width: 1280, height: 900 })
  await page.goto('/klasser')
  await expect(page.getByRole('heading', { name: 'Klasser' })).toBeVisible({ timeout: 15_000 })

  const { isoYear, isoWeek } = isoWeekOf(planningMonday())
  const classes = await api<{ id: string }[]>(page, '/classes')
  for (const c of classes) {
    const plan = await api<{ isHolidayWeek: boolean; slots: { schemaSlotId: string }[] }>(
      page,
      `/classes/${c.id}/week-plan?isoYear=${isoYear}&isoWeek=${isoWeek}`
    )
    if (!plan.isHolidayWeek && plan.slots.length >= 2) {
      await page.goto(`/klasser/${c.id}/ugeplan`)
      await expect(page.getByTestId('weekplan-grid')).toBeVisible({ timeout: 15_000 })
      return true
    }
  }
  return false
}

test('lesson editor saves by itself and "Næste" moves on without losing text', async ({ page }) => {
  test.setTimeout(120_000)
  test.skip(!(await openClassWithLessons(page)), 'No class with lektioner this week.')

  const first = `Første ${Date.now()}`
  const second = `Anden ${Date.now()}`
  const status = page.getByTestId('lesson-save-status')

  await page.locator('[data-testid^="weekplan-slot-"]').first().click()
  const firstHeading = await page.getByRole('dialog').getByRole('heading').textContent()

  await page.getByTestId('lesson-description').fill(first)
  await expect(status).toContainText('Gemt', { timeout: 10_000 })
  await expect(page.getByTestId('lesson-undo')).toBeVisible()

  // "Næste" saves at once, also when the autosave delay hasn't passed yet.
  const nextButton = page.getByTestId('lesson-next')
  await expect(nextButton).toContainText('Næste:')
  await nextButton.click()
  await expect(page.getByTestId('lesson-description')).not.toHaveValue(first)
  await page.getByTestId('lesson-description').fill(second)
  await page.getByTestId('lesson-close').click()
  await expect(page.getByRole('dialog')).toBeHidden()

  // Everything is still there after a reload.
  await page.reload()
  await expect(page.getByTestId('weekplan-grid')).toContainText(first, { timeout: 15_000 })
  await expect(page.getByTestId('weekplan-grid')).toContainText(second)

  // "Fortryd" restores the text from when the lesson was opened, and saves it.
  await page.locator('[data-testid^="weekplan-slot-"]').first().click()
  await expect(page.getByRole('dialog').getByRole('heading')).toHaveText(firstHeading ?? '')
  await page.getByTestId('lesson-description').fill(`${first} rettet`)
  await page.getByTestId('lesson-undo').click()
  await expect(page.getByTestId('lesson-description')).toHaveValue(first)
  await expect(status).toContainText('Gemt', { timeout: 10_000 })
  await page.getByTestId('lesson-close').click()
  await page.reload()
  await expect(page.getByTestId('weekplan-grid')).toContainText(first, { timeout: 15_000 })
  await expect(page.getByTestId('weekplan-grid')).not.toContainText(`${first} rettet`)
})

test('a failed save keeps the text and the dialog, and "Prøv igen" saves it', async ({ page }) => {
  test.setTimeout(120_000)
  test.skip(!(await openClassWithLessons(page)), 'No class with lektioner this week.')

  const text = `Må ikke forsvinde ${Date.now()}`
  await page.route('**/week-plan/slots**', (route) =>
    route.request().method() === 'PUT' ? route.abort() : route.continue()
  )

  await page.locator('[data-testid^="weekplan-slot-"]').first().click()
  await page.getByTestId('lesson-description').fill(text)
  await expect(page.getByTestId('lesson-save-error')).toBeVisible({ timeout: 10_000 })

  // "Luk" tries once more. That fails too, so the dialog and the text stay.
  const closeAttempt = page.waitForEvent('requestfailed')
  await page.getByTestId('lesson-close').click()
  await closeAttempt
  await expect(page.getByTestId('lesson-save-error')).toBeVisible()
  await expect(page.getByTestId('lesson-description')).toHaveValue(text)

  await page.unroute('**/week-plan/slots**')
  await page.getByTestId('lesson-save-retry').click()
  await expect(page.getByTestId('lesson-save-status')).toContainText('Gemt', { timeout: 10_000 })
  await page.getByTestId('lesson-close').click()
  await expect(page.getByTestId('weekplan-grid')).toContainText(text, { timeout: 15_000 })
})

test('file picker uploads and ticks a file, and a too big file only stops itself', async ({
  page,
}) => {
  test.setTimeout(120_000)
  test.skip(!(await openClassWithLessons(page)), 'No class with lektioner this week.')

  // Sparse, so nothing is really written. The limit is checked before any upload starts.
  const tooBig = path.join(os.tmpdir(), `for-stor-${Date.now()}.pdf`)
  fs.closeSync(fs.openSync(tooBig, 'w'))
  fs.truncateSync(tooBig, 501 * 1024 * 1024)

  try {
    await page.locator('[data-testid^="weekplan-slot-"]').first().click()
    await page.getByTestId('lesson-files').click()
    await expect(page.getByTestId('file-picker')).toBeVisible()

    const name = `e2e-picker-${Date.now()}.txt`
    await page.getByTestId('file-picker-input').setInputFiles(tooBig)
    await expect(page.getByTestId('file-upload-tooBig')).toContainText('Den må højst være 500 MB')

    await page
      .getByTestId('file-picker-input')
      .setInputFiles({ name, mimeType: 'text/plain', buffer: Buffer.from('hej') })
    await expect(page.getByTestId('file-upload-done')).toContainText(name, { timeout: 30_000 })
    await expect(page.getByTestId('file-picker-count')).not.toHaveText('Ingen filer valgt')

    await page.getByTestId('file-picker-done').click()
    await expect(page.getByTestId('lesson-files')).toContainText('vedhæftet')
  } finally {
    fs.rmSync(tooBig, { force: true })
  }
})

test.describe('parent view opens on the right day', () => {
  async function openPreview(page: Page, at: Date) {
    await page.clock.setFixedTime(at)
    await page.setViewportSize({ width: 375, height: 800 })
    await page.goto('/klasser')
    await expect(page.getByRole('heading', { name: 'Klasser' })).toBeVisible({ timeout: 15_000 })
    const classes = await api<{ id: string }[]>(page, '/classes')
    await page.goto(`/klasser/${classes[0].id}/ugeplan`)
    await page.getByTestId('parent-preview-toggle').click({ timeout: 15_000 })
    await expect(page.getByTestId('parent-preview')).toBeVisible()
  }

  const tuesdayAt = (hour: number) => {
    const monday = planningMonday()
    return new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + 1, hour)
  }

  test('before 15:00 it is today', async ({ page }) => {
    await openPreview(page, tuesdayAt(14))
    const tab = page.getByTestId('weekplan-day-toggle-Tuesday')
    test.skip(!(await tab.isVisible()), 'This week is a holiday week.')
    await expect(tab).toHaveAttribute('aria-selected', 'true')
    await expect(tab).toContainText('I dag')
  })

  test('from 15:00 it is the next school day', async ({ page }) => {
    await openPreview(page, tuesdayAt(16))
    const tab = page.getByTestId('weekplan-day-toggle-Wednesday')
    test.skip(!(await tab.isVisible()), 'This week is a holiday week.')
    await expect(tab).toHaveAttribute('aria-selected', 'true')
    await expect(tab).toContainText('I morgen')
    await expect(page.getByTestId('weekplan-day-toggle-Tuesday')).toHaveAttribute(
      'aria-selected',
      'false'
    )
  })
})
