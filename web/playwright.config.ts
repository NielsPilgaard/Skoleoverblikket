import { defineConfig, devices } from '@playwright/test'

const WEB_URL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

// Dev default matches the Aspire AppHost user-secrets value for keycloak-admin-password.
// Override with KEYCLOAK_ADMIN_PASSWORD env var in CI.
process.env.KEYCLOAK_ADMIN_PASSWORD ??= 'your-dev-password'

export default defineConfig({
  testDir: './tests/e2e',
  globalSetup: './tests/e2e/global-setup.ts',
  fullyParallel: false,
  workers: process.env.CI ? 4 : 3,
  retries: 1,
  timeout: 60_000,
  use: {
    baseURL: WEB_URL,
    trace: 'on-first-retry',
    storageState: 'tests/e2e/.auth/admin.json',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  // Start the full Aspire stack before tests if not already running.
  // Set SKIP_ASPIRE=1 to skip (e.g. when the stack is already up in CI or locally).
  webServer: process.env.SKIP_ASPIRE
    ? undefined
    : [
        {
          command: 'aspire run --non-interactive',
          url: WEB_URL,
          timeout: 180_000,
          reuseExistingServer: true,
          stdout: 'pipe',
          stderr: 'pipe',
        },
        {
          // Wait-only entry. The command must outlive the realm import: a process that exits
          // before the URL answers fails the run with "exited early".
          command: 'node -e "setInterval(() => {}, 1 << 30)"',
          url: 'http://localhost:8080/realms/Skoleoverblikket/.well-known/openid-configuration',
          timeout: 120_000,
          reuseExistingServer: true,
        },
      ],
})
