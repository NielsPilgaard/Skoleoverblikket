import { createHmac } from 'node:crypto'

// The realm's OTP policy: HmacSHA1, 6 digits, 30 seconds.
const PERIOD_SECONDS = 30

function base32Decode(input: string): Buffer {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  const clean = input.replace(/[\s=]/g, '').toUpperCase()
  let bits = 0
  let value = 0
  const bytes: number[] = []
  for (const char of clean) {
    value = (value << 5) | alphabet.indexOf(char)
    bits += 5
    if (bits >= 8) {
      bytes.push((value >>> (bits - 8)) & 0xff)
      bits -= 8
    }
  }
  return Buffer.from(bytes)
}

function currentStep(): number {
  return Math.floor(Date.now() / 1000 / PERIOD_SECONDS)
}

function codeFor(secret: string, step: number): string {
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(step))
  const hmac = createHmac('sha1', base32Decode(secret)).update(counter).digest()
  const offset = hmac[hmac.length - 1] & 0x0f
  const binary = hmac.readUInt32BE(offset) & 0x7fffffff
  return String(binary % 1_000_000).padStart(6, '0')
}

/**
 * A TOTP code for the secret Keycloak shows in manual mode, plus its time step. Keycloak refuses a
 * code twice, so pass the step of the last code used and this waits for the next one.
 */
export async function totpCode(secret: string, after?: number): Promise<{ code: string; step: number }> {
  while (after !== undefined && currentStep() <= after) {
    await new Promise((resolve) => setTimeout(resolve, 1000))
  }
  const step = currentStep()
  return { code: codeFor(secret, step), step }
}
