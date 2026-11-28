import { test, expect } from '@playwright/test'
import { createParentLogin, type ParentLogin } from './parents'

// A parent sends a 1:1 message to a staff member, who replies in the same thread.
test('parent writes to staff in Beskeder and gets a reply', async ({ page, browser, baseURL }) => {
  test.setTimeout(120_000)
  await page.goto('/dashboard')
  let parent: ParentLogin | undefined

  try {
    parent = await createParentLogin(page, browser, baseURL!)
    const subject = `E2E emne ${Date.now()}`
    const question = `E2E besked ${Date.now()}`
    const answer = `E2E svar ${Date.now()}`

    await parent.page.goto('/beskeder')
    await parent.page.getByTestId('messages-new').click({ timeout: 15_000 })
    await parent.page.getByTestId('messages-recipient-search').fill('Debug Admin')
    await parent.page
      .getByTestId('messages-recipient-option')
      .filter({ hasText: 'Debug Admin' })
      .filter({ hasText: 'Medarbejder' })
      .first()
      .click({ timeout: 15_000 })
    await parent.page.getByTestId('messages-subject').fill(subject)
    await parent.page.getByTestId('messages-body').fill(question)
    await parent.page.getByTestId('messages-send').click()
    await expect(parent.page.getByTestId('messages-send')).toBeHidden({ timeout: 15_000 })

    await page.goto('/beskeder')
    await page.getByTestId('messages-inbox-row').filter({ hasText: subject }).click({ timeout: 15_000 })
    await expect(page.getByTestId('messages-thread').getByText(question)).toBeVisible({ timeout: 15_000 })

    await page.getByTestId('messages-reply').click()
    await page.getByTestId('messages-body').fill(answer)
    await page.getByTestId('messages-send').click()
    await expect(page.getByTestId('messages-thread').getByText(answer)).toBeVisible({ timeout: 15_000 })

    await parent.page.reload()
    await parent.page.getByTestId('messages-inbox-row').filter({ hasText: subject }).click({ timeout: 15_000 })
    const thread = parent.page.getByTestId('messages-thread')
    await expect(thread.getByText(answer)).toBeVisible({ timeout: 15_000 })
    await expect(thread.getByText(question)).toBeVisible()
  } finally {
    await parent?.dispose()
  }
})
