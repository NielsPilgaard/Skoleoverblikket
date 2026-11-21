<#import "template.ftl" as layout>
<#assign what = layout.credentialName(event.getDetail("credential_type")!"")>
<@layout.emailLayout title="Ændring på din konto">
  <h1>${what} er fjernet</h1>
  <p>${what} blev fjernet fra din konto på Skoleoverblikket ${event.date?string["d. MMMM yyyy 'kl.' HH:mm"]}.</p>
  <div class="notice"><p style="margin-bottom:0;">Var det ikke dig? Skift din adgangskode med det samme, og kontakt skolens kontor.</p></div>
</@layout.emailLayout>
