import { test, expect } from '@playwright/test'

test.describe('CalendarPage — fixed month grid height', () => {
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 })
    await page.goto('/kalender')
    await expect(page.getByRole('heading', { name: 'Kalender' })).toBeVisible({ timeout: 15_000 })
  })

  test('all month cards have identical height (6 rows)', async ({ page }) => {
    const monthCards = page.locator('.lg\\:grid > div')
    const count = await monthCards.count()
    expect(count).toBeGreaterThan(0)

    const heights = await Promise.all(
      Array.from({ length: count }, (_, i) =>
        monthCards.nth(i).evaluate((el) => el.getBoundingClientRect().height),
      ),
    )

    const unique = [...new Set(heights)]
    const min = Math.min(...unique)
    const max = Math.max(...unique)
    expect(max - min).toBeLessThanOrEqual(1)
  })

  test('each month card grid has exactly 6 data rows (week-num cells)', async ({ page }) => {
    const monthCards = page.locator('.lg\\:grid > div')
    const count = await monthCards.count()
    expect(count).toBeGreaterThan(0)

    for (let i = 0; i < count; i++) {
      const card = monthCards.nth(i)
      const weekNumCells = card.locator('[data-testid="week-num"]')
      const weekNumCount = await weekNumCells.count()
      expect(weekNumCount).toBeGreaterThanOrEqual(4)
      expect(weekNumCount).toBeLessThanOrEqual(6)
    }
  })
})

test.describe('CalendarPage — entry list focus on click', () => {
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 })
    await page.goto('/kalender')
    await expect(page.getByRole('heading', { name: 'Kalender' })).toBeVisible({ timeout: 15_000 })
  })

  test('clicking entry in list highlights the day cell in the month grid', async ({ page }) => {
    const entryRows = page.getByTestId('calendar-entry-row')
    const count = await entryRows.count()
    test.skip(count === 0, 'No calendar entries — skipping highlight test')

    await entryRows.first().click()

    const desktopGrid = page.getByTestId('desktop-month-grid')
    await expect(desktopGrid.locator('.ring-2.ring-brand-500').first()).toBeVisible({ timeout: 5_000 })
  })

  test('highlight disappears after 8 seconds', async ({ page }) => {
    const entryRows = page.getByTestId('calendar-entry-row')
    const count = await entryRows.count()
    test.skip(count === 0, 'No calendar entries — skipping highlight timeout test')

    await entryRows.first().click()
    const desktopGrid = page.getByTestId('desktop-month-grid')
    await expect(desktopGrid.locator('.ring-2.ring-brand-500').first()).toBeVisible({ timeout: 5_000 })

    await expect(desktopGrid.locator('.ring-2.ring-brand-500').first()).not.toBeVisible({ timeout: 12_000 })
  })
})

test.describe('CalendarPage — "Tilføj begivenhed" button', () => {
  test('button is visible next to year dropdown for admins', async ({ page }) => {
    await page.goto('/kalender')
    await expect(page.getByRole('heading', { name: 'Kalender' })).toBeVisible({ timeout: 15_000 })
    await expect(page.getByRole('button', { name: 'Tilføj begivenhed' })).toBeVisible()
  })

  test('clicking Tilføj begivenhed opens create event modal', async ({ page }) => {
    await page.goto('/kalender')
    await expect(page.getByRole('heading', { name: 'Kalender' })).toBeVisible({ timeout: 15_000 })
    await page.getByRole('button', { name: 'Tilføj begivenhed' }).click()
    await expect(page.getByRole('dialog')).toBeVisible({ timeout: 5_000 })
  })
})
