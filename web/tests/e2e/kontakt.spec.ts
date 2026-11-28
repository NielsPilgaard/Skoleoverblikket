import { test, expect } from '@playwright/test'
import { createParentLogin, type ParentLogin } from './parents'

// A parent sees the co-class parents who chose to share their contact info, and nobody from other klasser.
test('Kontakt shows co-class parents who share, not other klasser', async ({ page, browser, baseURL }) => {
  test.setTimeout(180_000)
  await page.goto('/dashboard')
  const parents: ParentLogin[] = []

  try {
    const sharer = await createParentLogin(page, browser, baseURL!, { sharePhone: '12345678' })
    parents.push(sharer)
    const viewer = await createParentLogin(page, browser, baseURL!, { classId: sharer.classId })
    parents.push(viewer)
    const otherKlasse = await createParentLogin(page, browser, baseURL!, { sharePhone: '87654321' })
    parents.push(otherKlasse)

    await viewer.page.goto('/foraeldrevisning/kontakt')
    const directory = viewer.page.getByTestId('contact-directory')
    const row = directory.getByTestId('contact-directory-row').filter({ hasText: sharer.studentName })
    await expect(row).toContainText(sharer.parentName, { timeout: 15_000 })
    await row.getByTestId('person-row-toggle').click()
    await expect(row.getByRole('link', { name: '12345678' })).toHaveAttribute('href', 'tel:12345678')

    await expect(directory.getByText(otherKlasse.studentName)).toHaveCount(0)
    await expect(directory.getByText(otherKlasse.parentName)).toHaveCount(0)

    // The viewer did not tick the consent box, so the co-class sharer must not see them.
    const loaded = sharer.page.waitForResponse((res) => res.url().includes('/api/v1/contact-directory') && res.ok())
    await sharer.page.goto('/foraeldrevisning/kontakt')
    await loaded
    const sharerDirectory = sharer.page.getByTestId('contact-directory')
    await expect(sharerDirectory).toBeVisible({ timeout: 15_000 })
    await expect(sharerDirectory.getByText(viewer.parentName)).toHaveCount(0)
    await expect(sharerDirectory.getByText(viewer.studentName)).toHaveCount(0)
  } finally {
    for (const parent of parents) await parent.dispose()
  }
})
