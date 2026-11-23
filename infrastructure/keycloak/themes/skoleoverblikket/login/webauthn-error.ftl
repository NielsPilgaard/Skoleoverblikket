<#import "skoleoverblikket-layout.ftl" as layout>
<#-- Shown when the passkey dialog was closed or failed, both when adding one and at login. -->
<@layout.page title="Adgangsnøglen virkede ikke">

  <p class="lead">Blev Face ID eller fingeraftryk afbrudt? Prøv igen. Virker det stadig ikke, så brug en anden metode.</p>

  <form action="${url.loginAction}" method="post">
    <input type="hidden" name="authenticationExecution" value="${execution!}">
    <input type="hidden" name="isSetRetry" value="retry">
    <button type="submit" class="btn-primary" data-testid="kc-passkey-retry">Prøv igen</button>
  </form>

  <#if isAppInitiatedAction??>
    <form action="${url.loginAction}" method="post">
      <@layout.cancelButton/>
    </form>
  </#if>

</@layout.page>
