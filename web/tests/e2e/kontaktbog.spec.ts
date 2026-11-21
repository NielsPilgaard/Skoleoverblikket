import { test, expect } from '@playwright/test'
import { createParentLogin, type ParentLogin } from './parents'

// A parent writes to the school about their child, staff answer, and the parent sees the answer.
test('parent and staff talk in the kontaktbog', async ({ page, browser, baseURL }) => {
  test.setTimeout(120_000)
  await page.goto('/dashboard')
  let parent: ParentLogin | undefined

  try {
    parent = await createParentLogin(page, browser, baseURL!)
    const question = `E2E spørgsmål ${Date.now()}`
    const answer = `E2E svar ${Date.now()}`

    await parent.page.goto('/foraeldrevisning/kontaktbog')
    await parent.page.getByTestId('parent-contact-new-body').fill(question, { timeout: 15_000 })
    await parent.page.getByTestId('parent-contact-start').click()
    await expect(parent.page.getByTestId('parent-contact-conversation').getByText(question)).toBeVisible({
      timeout: 15_000,
    })

    await page.goto('/kontaktbog')
    const row = page.getByTestId('contact-thread-row').filter({ hasText: parent.studentName })
    await expect(row.getByTestId('contact-thread-unread')).toHaveText('1', { timeout: 15_000 })
    await row.click()
    await expect(page.getByTestId('contact-conversation').getByText(question)).toBeVisible({ timeout: 15_000 })
    await expect(row.getByTestId('contact-thread-unread')).toBeHidden({ timeout: 15_000 })

    await page.getByTestId('contact-composer').fill(answer)
    await page.getByTestId('contact-send').click()
    await expect(page.getByTestId('contact-conversation').getByText(answer)).toBeVisible({ timeout: 15_000 })

    await parent.page.reload()
    await parent.page.getByTestId('parent-contact-thread-row').filter({ hasText: parent.studentName }).click()
    await expect(parent.page.getByTestId('parent-contact-conversation').getByText(answer)).toBeVisible({
      timeout: 15_000,
    })
  } finally {
    await parent?.dispose()
  }
})
