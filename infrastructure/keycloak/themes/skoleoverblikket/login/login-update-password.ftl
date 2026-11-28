<!DOCTYPE html>
<html lang="da">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Vælg ny adgangskode – Skoleoverblikket</title>
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

    <h1 class="login-title">Vælg ny adgangskode</h1>

    <#if message?has_content>
      <div class="alert alert-${message.type}">
        ${message.summary}
      </div>
    </#if>

    <form action="${url.loginAction}" method="post">

      <input type="hidden" id="username" name="username" value="${username!''}">
      <input type="hidden" name="stateChecker" value="${stateChecker!''}">

      <div class="form-group">
        <label for="password-new">Ny adgangskode</label>
        <input
          type="password"
          id="password-new"
          name="password-new"
          data-testid="kc-password-new"
          autocomplete="new-password"
          autofocus
        >
      </div>

      <div class="form-group">
        <label for="password-confirm">Gentag adgangskode</label>
        <input
          type="password"
          id="password-confirm"
          name="password-confirm"
          data-testid="kc-password-confirm"
          autocomplete="new-password"
        >
      </div>

      <button type="submit" class="btn-primary" data-testid="kc-password-submit">Gem adgangskode</button>

    </form>

  </div>

</body>
</html>
