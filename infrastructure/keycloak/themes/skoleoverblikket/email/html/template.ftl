<#-- Branded shell for Keycloak's own emails (security notices). password-reset.ftl has its own copy. -->
<#macro emailLayout title="Skoleoverblikket">
<!DOCTYPE html>
<html lang="da" dir="ltr">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${title} – Skoleoverblikket</title>
  <style>
    body { margin: 0; padding: 0; background: #f0f5f1; font-family: 'Helvetica Neue', Arial, sans-serif; color: #111827; }
    .wrapper { max-width: 520px; margin: 40px auto; padding: 0 16px; }
    .card { background: #ffffff; border-radius: 12px; border: 1px solid #e5e7eb; box-shadow: 0 1px 3px rgba(0,0,0,0.08); padding: 40px 36px; }
    .brand { display: flex; align-items: center; gap: 10px; margin-bottom: 28px; }
    .brand-name { font-size: 1.3rem; font-weight: 700; color: #113b14; letter-spacing: -0.01em; }
    h1 { font-size: 1.125rem; font-weight: 600; color: #111827; margin: 0 0 16px; }
    p { font-size: 0.9375rem; line-height: 1.6; color: #374151; margin: 0 0 20px; }
    .notice { font-size: 0.8125rem; color: #6b7280; border-top: 1px solid #e5e7eb; padding-top: 20px; margin-top: 28px; }
    .footer { text-align: center; font-size: 0.8125rem; color: #9ca3af; margin-top: 24px; }
  </style>
</head>
<body>
  <div class="wrapper">
    <div class="card">
      <div class="brand">
        <svg width="28" height="28" viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
          <polyline points="2,14 16,3 30,14" stroke="#1f6321" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
          <rect x="5" y="14" width="22" height="16" rx="1.5" stroke="#1f6321" stroke-width="2"/>
          <line x1="12" y1="14" x2="12" y2="30" stroke="#7db87d" stroke-width="1.2"/>
          <line x1="20" y1="14" x2="20" y2="30" stroke="#7db87d" stroke-width="1.2"/>
          <line x1="5" y1="19" x2="27" y2="19" stroke="#7db87d" stroke-width="1.2"/>
          <line x1="5" y1="24" x2="27" y2="24" stroke="#7db87d" stroke-width="1.2"/>
        </svg>
        <span class="brand-name">Skoleoverblikket</span>
      </div>
      <#nested>
    </div>
    <div class="footer">
      Skoleoverblikket &middot; <a href="https://skoleoverblikket.dk" style="color:#9ca3af;">skoleoverblikket.dk</a>
    </div>
  </div>
</body>
</html>
</#macro>

<#-- Danish name of a credential type in event emails. -->
<#function credentialName type>
  <#switch type>
    <#case "password"><#return "Din adgangskode">
    <#case "otp"><#return "Tofaktor (godkendelses-app)">
    <#case "recovery-authn-codes"><#return "Dine gendannelseskoder">
    <#case "webauthn"><#case "webauthn-passwordless"><#return "En adgangsnøgle">
    <#default><#return "Dine login-oplysninger">
  </#switch>
</#function>
