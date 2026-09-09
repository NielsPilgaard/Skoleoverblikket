import { test, expect, type Page } from '@playwright/test'

// The Generelt editor on WeekPlanPage renders regardless of whether the class
// has an active schema, so this flow only needs *a* class to exist.
async function gotoAnyClassUgeplan(page: Page) {
  await page.setViewportSize({ width: 1280, height: 900 })
  await page.goto('/klasser')
  await expect(page.getByRole('heading', { name: 'Klasser' })).toBeVisible({ timeout: 15_000 })

  const firstRow = page.locator('[data-testid^="class-row-"]').first()
  const emptyState = page.getByText('Ingen klasser oprettet endnu')

  // Wait for the class list to settle into a definite state — a row rendered or
  // the empty-state text — before deciding whether to create a class. Checking
  // firstRow.count() while the list is still loading would spuriously create one.
  await expect(firstRow.or(emptyState)).toBeVisible({ timeout: 15_000 })

  if ((await firstRow.count()) === 0) {
    await page.getByRole('button', { name: 'Opret klasse' }).click()
    await page.getByPlaceholder('fx 5.a').fill(`E2E-MD-${Date.now()}`)
    await page.getByRole('button', { name: 'Gem' }).click()
  }
  await expect(firstRow).toBeVisible({ timeout: 10_000 })

  const testId = await firstRow.getAttribute('data-testid')
  const classId = testId!.replace('class-row-', '')
  await page.goto(`/klasser/${classId}/ugeplan`)
  await expect(page.getByTestId('generelt-editor')).toBeVisible({ timeout: 15_000 })
}

test.describe('WeekPlanPage — MarkdownTextarea affordances', () => {
  test('bullet button prefixes every selected line with "- "', async ({ page }) => {
    await gotoAnyClassUgeplan(page)

    const editor = page.getByTestId('generelt-editor')
    await editor.fill('Tur til skoven\nHusk madpakke')
    await editor.selectText()
    await page.getByRole('button', { name: 'Punktliste' }).click()

    await expect(editor).toHaveValue('- Tur til skoven\n- Husk madpakke')
  })

  test('B button wraps the selection in **…**', async ({ page }) => {
    await gotoAnyClassUgeplan(page)

    const editor = page.getByTestId('generelt-editor')
    await editor.fill('vigtigt')
    await editor.selectText()
    await page.getByRole('button', { name: 'Fed' }).click()

    await expect(editor).toHaveValue('**vigtigt**')
  })

  test('Enter on an empty bullet removes the marker and ends the list', async ({ page }) => {
    await gotoAnyClassUgeplan(page)

    const editor = page.getByTestId('generelt-editor')
    await editor.click()
    await editor.fill('- første')
    await editor.press('End')
    await editor.press('Enter')
    // Smart continuation inserted "\n- " — caret is on an empty bullet now.
    await expect(editor).toHaveValue('- første\n- ')
    await editor.press('Enter')
    await expect(editor).toHaveValue('- første\n')
  })

  test('typing "* " at line start normalises to "- "', async ({ page }) => {
    await gotoAnyClassUgeplan(page)

    const editor = page.getByTestId('generelt-editor')
    await editor.fill('')
    await editor.pressSequentially('* mælk')

    await expect(editor).toHaveValue('- mælk')
  })

  test('preview shows by default, renders markdown, and toggle persists to localStorage', async ({
    page,
  }) => {
    await gotoAnyClassUgeplan(page)

    const editor = page.getByTestId('generelt-editor')
    const preview = page.getByTestId('generelt-editor-preview')
    const toggle = page.getByTestId('generelt-editor-preview-toggle')

    // Shown by default.
    await expect(preview).toBeVisible()

    await editor.fill('**Tur** fredag')
    await expect(preview.locator('strong')).toHaveText('Tur')

    // A bullet list must render as an actual list with visible markers, not as
    // plain lines (Tailwind Preflight strips ul styling unless we re-apply it).
    await editor.fill('Ugen:\n- mandag tur\n- fredag fri')
    await expect(preview.locator('ul li')).toHaveText(['mandag tur', 'fredag fri'])
    await expect(preview.locator('ul')).toHaveCSS('list-style-type', 'disc')

    // Hide → gone, and persisted.
    await toggle.click()
    await expect(preview).toBeHidden()
    expect(await page.evaluate(() => localStorage.getItem('markdown-preview-hidden'))).toBe('1')

    // Survives a reload.
    await page.reload()
    await expect(page.getByTestId('generelt-editor')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('generelt-editor-preview')).toBeHidden()

    // Show again → cleared.
    await page.getByTestId('generelt-editor-preview-toggle').click()
    await expect(page.getByTestId('generelt-editor-preview')).toBeVisible()
    expect(await page.evaluate(() => localStorage.getItem('markdown-preview-hidden'))).toBe('0')
  })
})
