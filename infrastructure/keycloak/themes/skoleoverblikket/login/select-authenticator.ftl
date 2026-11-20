<#import "skoleoverblikket-layout.ftl" as layout>
<@layout.page title="Vælg hvordan du logger ind">

  <form id="kc-select-credential-form" action="${url.loginAction}" method="post">
    <#list auth.authenticationSelections as selection>
      <button type="submit" name="authenticationExecution" value="${selection.authExecId}" class="choice-button" data-testid="kc-select-${selection.displayName}">
        <span class="choice-heading">${msg(selection.displayName)}</span>
        <span class="choice-help">${msg(selection.helpText)}</span>
      </button>
    </#list>
  </form>

</@layout.page>
