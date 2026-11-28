<#import "skoleoverblikket-layout.ftl" as layout>
<@layout.page title="Tilføj adgangsnøgle">

  <p class="lead">Med en adgangsnøgle bekræfter du dit login med Face ID, fingeraftryk eller enhedens kode. Ingen kode at skrive af.</p>

  <form id="register" action="${url.loginAction}" method="post">
    <input type="hidden" name="clientDataJSON">
    <input type="hidden" name="attestationObject">
    <input type="hidden" name="publicKeyCredentialId">
    <input type="hidden" name="transports">
    <input type="hidden" name="authenticatorAttachment">
    <input type="hidden" name="error">

    <div class="form-group">
      <label for="authenticatorLabel">Navn på enheden</label>
      <input type="text" id="authenticatorLabel" name="authenticatorLabel" maxlength="100" autocomplete="off" data-testid="kc-passkey-label">
      <p class="hint">Så du kan kende den igen på Min konto.</p>
    </div>

    <button type="submit" id="registerWebAuthn" class="btn-primary" disabled data-testid="kc-passkey-register">Tilføj adgangsnøgle</button>
  </form>

  <#if !isSetRetry?has_content && isAppInitiatedAction?has_content>
    <form action="${url.loginAction}" method="post">
      <@layout.cancelButton/>
    </form>
  </#if>

  <script type="module">
    <#outputformat "JavaScript">
    import { register, guessDeviceName } from "${url.resourcesPath}/js/passkey.js";
    const form = document.getElementById('register');
    // Only a guess: keep a name typed before the module loaded.
    if (!form.elements.authenticatorLabel.value) form.elements.authenticatorLabel.value = guessDeviceName();
    const input = {
      challenge: ${challenge?c},
      userid: ${userid?c},
      username: ${username?c},
      signatureAlgorithms: [<#list signatureAlgorithms as alg>${alg?c},</#list>],
      rpEntityName: ${rpEntityName?c},
      rpId: ${rpId?c},
      attestationConveyancePreference: ${attestationConveyancePreference?c},
      authenticatorAttachment: ${authenticatorAttachment?c},
      residentKey: ${residentKey?c},
      userVerificationRequirement: ${userVerificationRequirement?c},
      createTimeout: ${createTimeout?c},
      excludeCredentialIds: ${excludeCredentialIds?c},
    };
    // The button starts the browser's passkey dialog; register() submits the form when it is done.
    form.addEventListener('submit', (event) => {
      if (event.submitter?.id !== 'registerWebAuthn') return;
      event.preventDefault();
      event.submitter.disabled = true;
      register(input, form);
    });
    // Disabled until here: an earlier click would post the form without a passkey.
    form.elements.registerWebAuthn.disabled = false;
    </#outputformat>
  </script>

</@layout.page>
