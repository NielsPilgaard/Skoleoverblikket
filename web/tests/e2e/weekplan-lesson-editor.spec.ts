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

test('a file that uploads but does not attach says so, and "Prøv igen" does not upload it twice', async ({
  page,
}) => {
  test.setTimeout(120_000)
  test.skip(!(await openClassWithLessons(page)), 'No class with lektioner this week.')

  let uploads = 0
  page.on('request', (r) => {
    if (r.method() === 'PUT' && !r.url().includes('/api/')) uploads++
  })
  await page.route('**/week-plan/slots/*/files', (route) =>
    route.request().method() === 'POST' ? route.abort() : route.continue()
  )

  await page.locator('[data-testid^="weekplan-slot-"]').first().click()
  await page.getByTestId('lesson-files').click()
  await page.getByTestId('file-picker-input').setInputFiles({
    name: `e2e-attach-${Date.now()}.txt`,
    mimeType: 'text/plain',
    buffer: Buffer.from('hej'),
  })
  await expect(page.getByTestId('file-upload-failed')).toContainText('kom ikke med på lektionen', {
    timeout: 30_000,
  })
  await expect(page.getByTestId('file-upload-done')).toBeHidden()

  await page.unroute('**/week-plan/slots/*/files')
  await page.getByTestId('file-upload-retry').click()
  await expect(page.getByTestId('file-upload-done')).toBeVisible({ timeout: 15_000 })
  expect(uploads).toBe(1)

  await page.getByTestId('file-picker-done').click()
  await page.getByTestId('lesson-close').click()
})

test('the lesson dialog works with only the keyboard', async ({ page }) => {
  test.setTimeout(120_000)
  test.skip(!(await openClassWithLessons(page)), 'No class with lektioner this week.')
  const slot = page.locator('[data-testid^="weekplan-slot-"]').first()
  const dialog = page.getByRole('dialog')
  const description = page.getByTestId('lesson-description')

  await slot.focus()
  await page.keyboard.press('Enter')
  await expect(description).toBeFocused()
  await expect(dialog).toHaveAttribute('aria-labelledby', /.+/)

  // Tab goes round inside the dialog, and never on to the page behind it.
  for (let i = 0; i < 25; i++) {
    await page.keyboard.press('Tab')
    expect(await page.evaluate(() => !!document.activeElement?.closest('[role=dialog]'))).toBe(true)
  }

  // The file picker is a step in the dialog: focus moves in, and Escape goes one step back.
  await page.getByTestId('lesson-files').focus()
  await page.keyboard.press('Enter')
  await expect(page.getByTestId('file-picker')).toBeVisible()
  expect(await page.evaluate(() => !!document.activeElement?.closest('[role=dialog]'))).toBe(true)
  await page.keyboard.press('Escape')
  await expect(page.getByTestId('file-picker')).toBeHidden()
  await expect(description).toBeFocused()

  // "Næste" replaces the dialog. Focus must land in the next lesson, not on the page behind it.
  await page.getByTestId('lesson-next').focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('dialog').getByTestId('lesson-save-status')).toBeVisible()
  await page.waitForTimeout(100)
  await expect(description).toBeFocused()

  // Escape from the text field saves what was typed, and focus returns to the lesson it opened from.
  const text = `Tastatur ${Date.now()}`
  await description.fill(text)
  await page.keyboard.press('Escape')
  await expect(dialog).toBeHidden({ timeout: 10_000 })
  await expect(slot).toBeFocused()
  await expect(page.getByTestId('weekplan-grid')).toContainText(text)
})

test('the ugeplan week dropdown opens the chosen week, and the list stays put', async ({ page }) => {
  test.setTimeout(60_000)
  test.skip(!(await openClassWithLessons(page)), 'No class with lektioner this week.')
  const week = page.getByTestId('week-jump')

  const first = await week.locator('option').first().getAttribute('value')
  const count = await week.locator('option').count()
  expect(count).toBeGreaterThanOrEqual(52)

  const [isoYear, isoWeek] = first!.split('-')
  const loaded = page.waitForResponse(
    (r) => r.url().includes(`isoYear=${isoYear}&isoWeek=${isoWeek}`) && r.status() === 200
  )
  await week.selectOption(first!)
  await loaded
  await expect(week).toHaveValue(first!)
  // The first week of the school year can start in July. It must not flip the list to last year.
  await expect(week.locator('option').first()).toHaveAttribute('value', first!)
  await expect(week.locator('option')).toHaveCount(count)
})

test('week and year dropdowns jump without stepping', async ({ page }) => {
  test.setTimeout(60_000)
  await page.goto('/sfo')
  const week = page.getByTestId('week-jump')
  await expect(week).toBeVisible({ timeout: 15_000 })

  const lastWeek = await week.locator('option').last().getAttribute('value')
  await week.selectOption(lastWeek!)
  await expect(week).toHaveValue(lastWeek!)

  const year = page.getByTestId('year-jump')
  const before = await year.inputValue()
  const next = String(Number(before) - 1)
  await year.selectOption(next)
  await expect(year).toHaveValue(next)
  await expect(week).toHaveValue(new RegExp(`^${next}-`))
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
    await expect(tab).toHaveAccessibleName(/^I dag, Tirsdag \d+/)
  })

  test('from 15:00 it is the next school day', async ({ page }) => {
    await openPreview(page, tuesdayAt(16))
    const tab = page.getByTestId('weekplan-day-toggle-Wednesday')
    test.skip(!(await tab.isVisible()), 'This week is a holiday week.')
    await expect(tab).toHaveAttribute('aria-selected', 'true')
    await expect(tab).toContainText('I morgen')
    await expect(tab).toHaveAccessibleName(/^I morgen, Onsdag \d+/)
    await expect(page.getByTestId('weekplan-day-toggle-Tuesday')).toHaveAttribute(
      'aria-selected',
      'false'
    )
  })
})
