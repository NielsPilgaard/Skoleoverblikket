<#import "skoleoverblikket-layout.ftl" as layout>
<#-- Min konto sends users here to remove an authenticator app or a passkey, one per confirm. Two-factor
     stays on while another app or passkey is left. -->
<#-- Name it when the user named it; Keycloak falls back to the type name, which tells nothing. -->
<#assign typeNames = ["otp-display-name", msg("otp-display-name"), "webauthn-display-name", msg("webauthn-display-name")]>
<#assign named = (credentialLabel!"")?has_content && !typeNames?seq_contains(credentialLabel)>
<@layout.page title="Fjern login-metode?">

  <p class="lead"><#if named><strong data-testid="kc-delete-credential-label">${credentialLabel}</strong><#else>Den</#if> kan ikke længere bruges til at logge ind. Har du ingen anden app eller adgangsnøgle, logger du derefter ind med kun din adgangskode, og dine gendannelseskoder holder op med at virke.</p>

  <form action="${url.loginAction}" method="post">
    <button type="submit" name="accept" id="kc-accept" class="btn-danger" data-testid="kc-delete-credential-confirm">Fjern</button>
    <button type="submit" name="cancel-aia" value="true" class="btn-secondary" data-testid="kc-cancel">Annullér</button>
  </form>

</@layout.page>
