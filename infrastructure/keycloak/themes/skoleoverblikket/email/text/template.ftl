<#-- Danish name of a credential type in event emails. Same as html/template.ftl. -->
<#function credentialName type>
  <#switch type>
    <#case "password"><#return "Din adgangskode">
    <#case "otp"><#return "Tofaktor (godkendelses-app)">
    <#case "recovery-authn-codes"><#return "Dine gendannelseskoder">
    <#case "webauthn"><#case "webauthn-passwordless"><#return "En adgangsnøgle">
    <#default><#return "Dine login-oplysninger">
  </#switch>
</#function>
