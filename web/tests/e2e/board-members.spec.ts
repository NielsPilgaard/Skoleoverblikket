import { test, expect } from '@playwright/test'

// Board members are managed under Stamdata → Bestyrelse, not in Skoleindstillinger.
// The shared storageState authenticates as an admin.

test('admin invites, updates and removes a board member from Stamdata', async ({ page }) => {
  const email = `bestyrelse-${Date.now()}@example.dk`
  const name = `Bestyrelse E2E ${Date.now()}`

  await page.goto('/dashboard')
  const stamdata = page.getByTestId('nav-group-Stamdata')
  await expect(stamdata).toBeVisible({ timeout: 15_000 })
  if ((await stamdata.getAttribute('aria-expanded')) !== 'true') await stamdata.click()
  await page.getByTestId('nav-link-bestyrelsesmedlemmer').click()
  await expect(page.getByTestId('board-members-page')).toBeVisible({ timeout: 15_000 })

  await page.getByTestId('board-invite-button').first().click()
  await page.getByTestId('board-invite-name').fill(name)
  await page.getByTestId('board-invite-email').fill(email)
  await page.getByTestId('board-invite-submit').click()

  const row = page.getByTestId('board-member-row').filter({ hasText: email })
  await expect(row).toBeVisible({ timeout: 15_000 })
  // Inviting pre-creates the Keycloak account (the invitee chooses its password later),
  // so a fresh invite already shows as having an account.
  await expect(row).toContainText('Konto oprettet')

  const teacherAccess = row.getByTestId('board-member-teacher-access')
  await expect(teacherAccess).not.toBeChecked()
  await teacherAccess.click()
  await expect(teacherAccess).toBeChecked({ timeout: 15_000 })

  page.once('dialog', (dialog) => void dialog.accept())
  await row.getByTestId('board-member-remove').click()
  await expect(row).toHaveCount(0, { timeout: 15_000 })

  await page.goto('/indstillinger')
  await expect(page.getByTestId('logo-upload')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByTestId('board-invite-button')).toHaveCount(0)
  await expect(page.getByTestId('board-member-row')).toHaveCount(0)
})

// The invitation link is opened before the board member has logged in, so the page must work
// anonymously. The generated client only redirects to Keycloak for operations that need a token.
test('board invitation link works without logging in first', async ({ page, browser, request }) => {
  const email = `bestyrelse-link-${Date.now()}@example.dk`

  await page.goto('/bestyrelsesmedlemmer')
  await expect(page.getByTestId('board-members-page')).toBeVisible({ timeout: 15_000 })
  await page.getByTestId('board-invite-button').first().click()
  await page.getByTestId('board-invite-name').fill(`Bestyrelse Link ${Date.now()}`)
  await page.getByTestId('board-invite-email').fill(email)
  await page.getByTestId('board-invite-submit').click()
  const row = page.getByTestId('board-member-row').filter({ hasText: email })
  await expect(row).toBeVisible({ timeout: 15_000 })

  let link: string | undefined
  for (let i = 0; i < 20 && !link; i++) {
    const list = (await (await request.get('http://localhost:8025/api/v1/messages')).json()) as {
      messages?: { ID: string; To: { Address: string }[] }[]
    }
    const msg = (list.messages ?? []).find((m) => m.To.some((t) => t.Address === email))
    if (msg) {
      const detail = (await (await request.get(`http://localhost:8025/api/v1/message/${msg.ID}`)).json()) as {
        Text?: string
      }
      link = detail.Text?.match(/http\S+\/board-invitation\/\S+/)?.[0]
    }
    if (!link) await page.waitForTimeout(500)
  }
  expect(link, 'No board invitation link in email').toBeDefined()

  const visitorContext = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  const visitor = await visitorContext.newPage()
  await visitor.goto(link!)
  await expect(visitor.getByTestId('board-invitation-preview')).toBeVisible({ timeout: 15_000 })
  expect(new URL(visitor.url()).port).toBe('5173')
  // A new board member chooses their password here and lands logged in on the board pages.
  await visitor.getByTestId('board-invitation-password').fill('NewPass456!')
  await visitor.getByTestId('board-invitation-accept').click()
  await visitor.waitForURL(/\/bestyrelse\/oversigt/, { timeout: 15_000 })
  await visitorContext.close()

  page.once('dialog', (dialog) => void dialog.accept())
  await row.getByTestId('board-member-remove').click()
  await expect(row).toHaveCount(0, { timeout: 15_000 })
})
