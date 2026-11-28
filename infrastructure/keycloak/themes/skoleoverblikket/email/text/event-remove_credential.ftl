<#ftl output_format="plainText">
<#import "template.ftl" as layout>
<#assign what = layout.credentialName(event.getDetail("credential_type")!"")>
${what} blev fjernet fra din konto på Skoleoverblikket ${event.date?string["d. MMMM yyyy 'kl.' HH:mm"]}.

Var det ikke dig? Skift din adgangskode med det samme, og kontakt skolens kontor.
