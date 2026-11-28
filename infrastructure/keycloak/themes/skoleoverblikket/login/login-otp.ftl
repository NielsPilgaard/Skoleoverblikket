<#import "skoleoverblikket-layout.ftl" as layout>
<@layout.page title="Indtast kode fra din app">

  <p class="lead">Åbn din godkendelses-app (fx Microsoft Authenticator) og skriv den 6-cifrede kode til Skoleoverblikket.</p>

  <form id="kc-otp-login-form" action="${url.loginAction}" method="post" onsubmit="login.disabled = true; return true;">

    <#if otpLogin.userOtpCredentials?size gt 1>
      <fieldset class="form-group choice-list">
        <legend>Hvilken app?</legend>
        <#list otpLogin.userOtpCredentials as otpCredential>
          <label class="checkbox">
            <input type="radio" name="selectedCredentialId" value="${otpCredential.id}" <#if otpCredential.id == otpLogin.selectedCredentialId>checked</#if>>
            ${otpCredential.userLabel!"Godkendelses-app"}
          </label>
        </#list>
      </fieldset>
    </#if>

    <div class="form-group">
      <label for="otp">Kode</label>
      <input
        type="text"
        id="otp"
        name="otp"
        class="code-input"
        autocomplete="one-time-code"
        inputmode="numeric"
        maxlength="7"
        autofocus
        dir="ltr"
        data-testid="kc-otp-input"
        aria-invalid="<#if messagesPerField.existsError('totp')>true</#if>"<#if messagesPerField.existsError('totp')> aria-describedby="totp-error"</#if>
      >
      <@layout.fieldError field="totp"/>
    </div>

    <button type="submit" name="login" id="kc-login" class="btn-primary" data-testid="kc-otp-submit">Log ind</button>

  </form>

</@layout.page>
