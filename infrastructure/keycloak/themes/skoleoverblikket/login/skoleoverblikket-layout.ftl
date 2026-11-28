<#-- Shared page shell for the Danish login pages added in task 64. The older pages (login, reset and
     update password) still carry their own copy of this markup. -->
<#macro page title>
<!DOCTYPE html>
<html lang="da">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${title} – Skoleoverblikket</title>
  <link rel="stylesheet" href="${url.resourcesPath}/css/styles.css">
</head>
<body>

  <div class="card-pf">

    <div class="brand-header">
      <svg width="32" height="32" viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
        <polyline points="2,14 16,3 30,14" stroke="#1f6321" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
        <rect x="5" y="14" width="22" height="16" rx="1.5" stroke="#1f6321" stroke-width="2"/>
        <line x1="12" y1="14" x2="12" y2="30" stroke="#7db87d" stroke-width="1.2"/>
        <line x1="20" y1="14" x2="20" y2="30" stroke="#7db87d" stroke-width="1.2"/>
        <line x1="5" y1="19" x2="27" y2="19" stroke="#7db87d" stroke-width="1.2"/>
        <line x1="5" y1="24" x2="27" y2="24" stroke="#7db87d" stroke-width="1.2"/>
      </svg>
      <span class="brand-name">Skoleoverblikket</span>
    </div>

    <h1 class="login-title">${title}</h1>

    <#-- From Min konto the page itself says what to do; Keycloak's "you need to set up X" warning only adds noise. -->
    <#if message?has_content && !(message.type == 'warning' && isAppInitiatedAction??)>
      <div class="alert alert-${message.type}" data-testid="kc-message">
        ${kcSanitize(message.summary)?no_esc}
      </div>
    </#if>

    <#nested>

    <#if auth?has_content && auth.showTryAnotherWayLink()>
      <form id="kc-select-try-another-way-form" action="${url.loginAction}" method="post">
        <input type="hidden" name="tryAnotherWay" value="on">
        <button type="submit" class="link-button" data-testid="kc-try-another-way">Brug en anden metode</button>
      </form>
    </#if>

  </div>

</body>
</html>
</#macro>

<#-- The "cancel" button on pages the user reached from Min konto. -->
<#macro cancelButton>
  <#if isAppInitiatedAction??>
    <button type="submit" name="cancel-aia" value="true" class="btn-secondary" data-testid="kc-cancel">Annullér</button>
  </#if>
</#macro>

<#macro fieldError field>
  <#if messagesPerField.existsError(field)>
    <span id="${field}-error" class="field-error" aria-live="polite">${kcSanitize(messagesPerField.get(field))?no_esc}</span>
  </#if>
</#macro>
