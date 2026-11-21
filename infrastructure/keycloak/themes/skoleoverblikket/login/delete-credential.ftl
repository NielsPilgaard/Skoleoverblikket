<#import "skoleoverblikket-layout.ftl" as layout>
<#-- Min konto only sends users here to turn off their authenticator app. -->
<@layout.page title="Slå tofaktor fra?">

  <p class="lead">Du logger derefter ind med kun din adgangskode. Dine gendannelseskoder holder også op med at virke.</p>

  <form action="${url.loginAction}" method="post">
    <button type="submit" name="accept" id="kc-accept" class="btn-danger" data-testid="kc-delete-credential-confirm">Slå tofaktor fra</button>
    <button type="submit" name="cancel-aia" value="true" class="btn-secondary" data-testid="kc-cancel">Annullér</button>
  </form>

</@layout.page>
