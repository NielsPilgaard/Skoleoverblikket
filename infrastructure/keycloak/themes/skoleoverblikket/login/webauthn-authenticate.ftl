<#import "skoleoverblikket-layout.ftl" as layout>
<@layout.page title="Log ind med adgangsnøgle">

  <p class="lead">Bekræft med Face ID, fingeraftryk eller enhedens kode.</p>

  <form id="webauth" action="${url.loginAction}" method="post">
    <input type="hidden" name="clientDataJSON">
    <input type="hidden" name="authenticatorData">
    <input type="hidden" name="signature">
    <input type="hidden" name="credentialId">
    <input type="hidden" name="userHandle">
    <input type="hidden" name="error">
  </form>

  <button type="button" id="authenticateWebAuthnButton" class="btn-primary" autofocus data-testid="kc-passkey-login">Brug adgangsnøgle</button>

  <script type="module">
    <#outputformat "JavaScript">
    import { authenticate } from "${url.resourcesPath}/js/passkey.js";
    const input = {
      isUserIdentified: "${isUserIdentified!''}" === "true",
      challenge: ${challenge?c},
      userVerification: ${userVerification?c},
      rpId: ${rpId?c},
      createTimeout: ${createTimeout?c},
      credentialIds: [<#if authenticators??><#list authenticators.authenticators as authenticator>${authenticator.credentialId?c},</#list></#if>],
    };
    const button = document.getElementById('authenticateWebAuthnButton');
    button.addEventListener('click', () => {
      button.disabled = true;
      authenticate(input, document.getElementById('webauth'));
    });
    </#outputformat>
  </script>

</@layout.page>
