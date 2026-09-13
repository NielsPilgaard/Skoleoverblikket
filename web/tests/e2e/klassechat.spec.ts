import { test, expect } from '@playwright/test'

// The stored auth state is an admin, who sees every klasse thread in the tenant.
test.describe('Klassechat', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/klassechat')
    await expect(page.getByRole('heading', { name: 'Klassechat' })).toBeVisible({ timeout: 15_000 })
  })

  test('thread list renders one row per klasse', async ({ page }) => {
    await expect(page.getByTestId('klassechat-thread-list')).toBeVisible()

    const rows = page.getByTestId('klassechat-thread-row')
    await expect(rows.first()).toBeVisible({ timeout: 15_000 })
    expect(await rows.count()).toBeGreaterThan(0)
  })

  test('sending a message shows it in the thread', async ({ page }) => {
    const body = `E2E besked ${Date.now()}`

    await page.getByTestId('klassechat-thread-row').first().click()
    await expect(page.getByTestId('klassechat-composer')).toBeVisible()

    await page.getByTestId('klassechat-composer').fill(body)
    await page.getByTestId('klassechat-send').click()

    await expect(page.getByText(body)).toBeVisible({ timeout: 15_000 })
  })

  test('a URL in a message renders as a link', async ({ page }) => {
    const marker = `E2E link ${Date.now()}`

    await page.getByTestId('klassechat-thread-row').first().click()
    await expect(page.getByTestId('klassechat-composer')).toBeVisible()

    await page.getByTestId('klassechat-composer').fill(`${marker} https://skoleoverblikket.dk`)
    await page.getByTestId('klassechat-send').click()

    const link = page.getByRole('link', { name: 'https://skoleoverblikket.dk' }).last()
    await expect(link).toBeVisible({ timeout: 15_000 })
    await expect(link).toHaveAttribute('href', 'https://skoleoverblikket.dk')
  })

  test('a javascript: scheme is not rendered as a link', async ({ page }) => {
    const marker = `E2E unsafe ${Date.now()}`

    await page.getByTestId('klassechat-thread-row').first().click()
    await expect(page.getByTestId('klassechat-composer')).toBeVisible()

    // eslint-disable-next-line no-script-url
    await page.getByTestId('klassechat-composer').fill(`${marker} javascript:alert(1)`)
    await page.getByTestId('klassechat-send').click()

    await expect(page.getByText(marker)).toBeVisible({ timeout: 15_000 })
    await expect(page.locator('a[href^="javascript:"]')).toHaveCount(0)
  })

  test('admin can delete a message (moderation)', async ({ page }) => {
    const body = `E2E slet ${Date.now()}`

    await page.getByTestId('klassechat-thread-row').first().click()
    await expect(page.getByTestId('klassechat-composer')).toBeVisible()

    await page.getByTestId('klassechat-composer').fill(body)
    await page.getByTestId('klassechat-send').click()
    await expect(page.getByText(body)).toBeVisible({ timeout: 15_000 })

    page.once('dialog', (dialog) => dialog.accept())

    const message = page.getByTestId('klassechat-message').filter({ hasText: body })
    await message.getByTestId('klassechat-delete-message').click()

    await expect(page.getByText(body)).not.toBeVisible({ timeout: 15_000 })
  })

  test('sending a message with an attachment', async ({ page }) => {
    const body = `E2E vedhæft ${Date.now()}`

    await page.getByTestId('klassechat-thread-row').first().click()
    await expect(page.getByTestId('klassechat-composer')).toBeVisible()

    await page.getByTestId('klassechat-file-input').setInputFiles({
      name: 'klassechat-e2e.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('Vedhæftet testfil'),
    })

    // The chip appears only once presign → PUT → confirm has completed.
    await expect(page.getByText('klassechat-e2e.txt')).toBeVisible({ timeout: 30_000 })

    await page.getByTestId('klassechat-composer').fill(body)
    await page.getByTestId('klassechat-send').click()

    await expect(page.getByText(body)).toBeVisible({ timeout: 15_000 })

    const message = page.getByTestId('klassechat-message').filter({ hasText: body })
    await expect(message.getByRole('link', { name: /klassechat-e2e\.txt/ })).toBeVisible({ timeout: 15_000 })
  })
})
