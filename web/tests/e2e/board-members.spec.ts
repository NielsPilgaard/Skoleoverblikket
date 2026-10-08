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
  // Inviting pre-creates the Keycloak account (temporary password in the email),
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
