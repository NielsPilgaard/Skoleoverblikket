<#import "skoleoverblikket-layout.ftl" as layout>
<@layout.page title="Slå tofaktor til">

  <ol class="steps">
    <li>
      <p>Hent en godkendelses-app på din telefon, hvis du ikke har en:
        <strong>Microsoft Authenticator</strong> eller <strong>Google Authenticator</strong>. Begge er gratis.</p>
    </li>

    <#if mode?? && mode = "manual">
      <li>
        <p>Tryk på <strong>+</strong> i appen, vælg at indtaste en nøgle, og skriv denne nøgle:</p>
        <p class="secret-key" data-testid="kc-totp-secret">${totp.totpSecretEncoded}</p>
        <p class="hint">Vælg <strong>Tidsbaseret</strong>, hvis appen spørger. Kontonavn: Skoleoverblikket.</p>
        <p><a href="${totp.qrUrl}" class="inline-link" data-testid="kc-totp-qr-mode">Scan en QR-kode i stedet</a></p>
      </li>
    <#else>
      <li>
        <p>Tryk på <strong>+</strong> i appen og scan QR-koden:</p>
        <img class="qr-code" src="data:image/png;base64, ${totp.totpSecretQrCode}" alt="QR-kode til din godkendelses-app" data-testid="kc-totp-qr">
        <p><a href="${totp.manualUrl}" class="inline-link" data-testid="kc-totp-manual-mode">Kan du ikke scanne? Skriv nøglen i stedet</a></p>
      </li>
    </#if>

    <li><p>Skriv den 6-cifrede kode, som appen viser:</p></li>
  </ol>

  <form action="${url.loginAction}" id="kc-totp-settings-form" method="post">

    <div class="form-group">
      <label for="totp">Kode fra appen</label>
      <input
        type="text"
        id="totp"
        name="totp"
        class="code-input"
        autocomplete="one-time-code"
        inputmode="numeric"
        maxlength="7"
        dir="ltr"
        data-testid="kc-totp-input"
        aria-invalid="<#if messagesPerField.existsError('totp')>true</#if>"
      >
      <@layout.fieldError field="totp"/>
    </div>

    <input type="hidden" id="totpSecret" name="totpSecret" value="${totp.totpSecret}">
    <#if mode??><input type="hidden" id="mode" name="mode" value="${mode}"></#if>

    <#-- Keycloak requires a name once the user has more than one app. -->
    <#if totp.otpCredentials?size gte 1>
      <div class="form-group">
        <label for="userLabel">Navn på telefonen</label>
        <input type="text" id="userLabel" name="userLabel" autocomplete="off" placeholder="Fx Min iPhone" data-testid="kc-totp-label"
          aria-invalid="<#if messagesPerField.existsError('userLabel')>true</#if>">
        <@layout.fieldError field="userLabel"/>
      </div>
    <#else>
      <input type="hidden" id="userLabel" name="userLabel" value="Godkendelses-app">
    </#if>

    <label class="checkbox">
      <input type="checkbox" id="logout-sessions" name="logout-sessions" value="on">
      Log mig ud på mine andre enheder
    </label>

    <button type="submit" id="saveTOTPBtn" class="btn-primary" data-testid="kc-totp-submit">Slå tofaktor til</button>
    <@layout.cancelButton/>

  </form>

</@layout.page>
