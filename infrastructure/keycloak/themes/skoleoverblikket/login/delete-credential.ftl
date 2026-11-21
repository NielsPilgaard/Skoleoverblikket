<#import "skoleoverblikket-layout.ftl" as layout>
<#-- Min konto only sends users here to turn off their authenticator app. -->
<@layout.page title="Slå tofaktor fra?">

  <#-- One app per confirm: with a second app left, two-factor stays on until that one is removed too. -->
  <p class="lead">Appen på din telefon kan ikke længere bruges til at logge ind. Har du ikke en anden app, logger du derefter ind med kun din adgangskode, og dine gendannelseskoder holder op med at virke.</p>

  <form action="${url.loginAction}" method="post">
    <button type="submit" name="accept" id="kc-accept" class="btn-danger" data-testid="kc-delete-credential-confirm">Slå tofaktor fra</button>
    <button type="submit" name="cancel-aia" value="true" class="btn-secondary" data-testid="kc-cancel">Annullér</button>
  </form>

</@layout.page>
