// Passkeys (WebAuthn) for the Danish login pages. Keycloak's own script asks for the name in a
// browser prompt and needs an import map, so these pages use this instead. Same form fields.

function decode(value) {
  const base64 = value.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(value.length / 4) * 4, '=')
  return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0))
}

function encode(buffer) {
  return btoa(String.fromCharCode(...new Uint8Array(buffer)))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')
}

function fail(form, error) {
  form.elements.error.value = String(error)
  form.requestSubmit()
}

/** A name the user can recognise on Min konto, before they type their own. */
export function guessDeviceName() {
  const ua = navigator.userAgent
  if (/iPhone/.test(ua)) return 'iPhone'
  if (/iPad/.test(ua)) return 'iPad'
  if (/Android/.test(ua)) return 'Android-telefon'
  if (/Macintosh/.test(ua)) return 'Mac'
  if (/Windows/.test(ua)) return 'Windows-pc'
  return 'Adgangsnøgle'
}

export async function register(input, form) {
  if (!window.PublicKeyCredential) return fail(form, 'WebAuthnUnsupportedBrowser')

  const selection = {}
  if (input.authenticatorAttachment !== 'not specified') {
    selection.authenticatorAttachment = input.authenticatorAttachment
  }
  if (input.residentKey && input.residentKey !== 'not specified') {
    selection.residentKey = input.residentKey
    selection.requireResidentKey = input.residentKey === 'required'
  }
  if (input.userVerificationRequirement !== 'not specified') {
    selection.userVerification = input.userVerificationRequirement
  }

  const algorithms = input.signatureAlgorithms.length ? input.signatureAlgorithms : [-7]
  const publicKey = {
    challenge: decode(input.challenge),
    rp: { id: input.rpId, name: input.rpEntityName },
    user: { id: decode(input.userid), name: input.username, displayName: input.username },
    pubKeyCredParams: algorithms.map((alg) => ({ type: 'public-key', alg })),
    authenticatorSelection: selection,
    excludeCredentials: input.excludeCredentialIds
      ? input.excludeCredentialIds.split(',').map((id) => ({ type: 'public-key', id: decode(id) }))
      : [],
  }
  if (input.attestationConveyancePreference !== 'not specified') {
    publicKey.attestation = input.attestationConveyancePreference
  }
  if (input.createTimeout) publicKey.timeout = input.createTimeout * 1000

  try {
    const credential = await navigator.credentials.create({ publicKey })
    const fields = form.elements
    fields.clientDataJSON.value = encode(credential.response.clientDataJSON)
    fields.attestationObject.value = encode(credential.response.attestationObject)
    fields.publicKeyCredentialId.value = encode(credential.rawId)
    fields.transports.value = credential.response.getTransports?.().join(',') ?? ''
    fields.authenticatorAttachment.value = credential.authenticatorAttachment ?? ''
    if (!fields.authenticatorLabel.value.trim()) fields.authenticatorLabel.value = guessDeviceName()
    form.requestSubmit()
  } catch (error) {
    fail(form, error)
  }
}

export async function authenticate(input, form) {
  if (!window.PublicKeyCredential) return fail(form, 'WebAuthnUnsupportedBrowser')

  const publicKey = {
    rpId: input.rpId,
    challenge: decode(input.challenge),
    allowCredentials: input.isUserIdentified
      ? input.credentialIds.map((id) => ({ type: 'public-key', id: decode(id) }))
      : [],
  }
  if (input.userVerification !== 'not specified') publicKey.userVerification = input.userVerification
  if (input.createTimeout) publicKey.timeout = input.createTimeout * 1000

  try {
    const credential = await navigator.credentials.get({ publicKey })
    const fields = form.elements
    fields.clientDataJSON.value = encode(credential.response.clientDataJSON)
    fields.authenticatorData.value = encode(credential.response.authenticatorData)
    fields.signature.value = encode(credential.response.signature)
    fields.credentialId.value = credential.id
    if (credential.response.userHandle) fields.userHandle.value = encode(credential.response.userHandle)
    form.requestSubmit()
  } catch (error) {
    fail(form, error)
  }
}
