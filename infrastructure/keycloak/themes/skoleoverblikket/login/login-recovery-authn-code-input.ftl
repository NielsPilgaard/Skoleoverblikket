<#import "skoleoverblikket-layout.ftl" as layout>
<@layout.page title="Brug en gendannelseskode">

  <p class="lead">Find listen med gendannelseskoder, som du gemte, da du slog tofaktor til.</p>

  <form id="kc-recovery-code-login-form" action="${url.loginAction}" method="post" onsubmit="login.disabled = true; return true;">

    <div class="form-group">
      <label for="recoveryCodeInput">Kode nr. ${recoveryAuthnCodesInputBean.codeNumber?c} på listen</label>
      <input
        type="text"
        id="recoveryCodeInput"
        name="recoveryCodeInput"
        class="code-input"
        autocomplete="one-time-code"
        autocapitalize="none"
        spellcheck="false"
        autofocus
        dir="ltr"
        data-testid="kc-recovery-code-input"
        aria-invalid="<#if messagesPerField.existsError('recoveryCodeInput')>true</#if>"
      >
      <@layout.fieldError field="recoveryCodeInput"/>
    </div>

    <button type="submit" name="login" id="kc-login" class="btn-primary" data-testid="kc-recovery-code-submit">Log ind</button>

  </form>

</@layout.page>
