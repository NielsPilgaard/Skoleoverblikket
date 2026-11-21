<#import "skoleoverblikket-layout.ftl" as layout>
<@layout.page title="Gem dine gendannelseskoder">

  <p class="lead">Mister du din telefon, kan du logge ind med en af disse koder i stedet. Hver kode virker én gang.
    Print dem eller gem filen et sikkert sted. Du kan ikke se dem igen.</p>

  <ol id="kc-recovery-codes-list" class="recovery-codes" data-testid="kc-recovery-codes">
    <#list recoveryAuthnCodesConfigBean.generatedRecoveryAuthnCodesList as code>
      <li>${code[0..3]}-${code[4..7]}-${code[8..]}</li>
    </#list>
  </ol>

  <div class="code-actions">
    <button type="button" id="printRecoveryCodes" class="btn-secondary">Print</button>
    <button type="button" id="downloadRecoveryCodes" class="btn-secondary">Gem som fil</button>
  </div>

  <form action="${url.loginAction}" id="kc-recovery-codes-settings-form" method="post">
    <input type="hidden" name="generatedRecoveryAuthnCodes" value="${recoveryAuthnCodesConfigBean.generatedRecoveryAuthnCodesAsString}">
    <input type="hidden" name="generatedAt" value="${recoveryAuthnCodesConfigBean.generatedAt?c}">
    <input type="hidden" id="userLabel" name="userLabel" value="Gendannelseskoder">

    <label class="checkbox">
      <input type="checkbox" id="kcRecoveryCodesConfirmationCheck" name="kcRecoveryCodesConfirmationCheck" data-testid="kc-recovery-codes-saved"
        onchange="document.getElementById('saveRecoveryAuthnCodesBtn').disabled = !this.checked;">
      Jeg har gemt koderne
    </label>

    <button type="submit" id="saveRecoveryAuthnCodesBtn" class="btn-primary" data-testid="kc-recovery-codes-submit" disabled>Færdig</button>
    <@layout.cancelButton/>
  </form>

  <script>
    <#outputformat "JavaScript">
    (function () {
      function codesText() {
        var items = document.querySelectorAll("#kc-recovery-codes-list li");
        var lines = [];
        for (var i = 0; i < items.length; i++) { lines.push((i + 1) + ". " + items[i].innerText); }
        return "Gendannelseskoder til Skoleoverblikket\r\n\r\n" + lines.join("\r\n")
          + "\r\n\r\nHver kode virker én gang. Gemt " + new Date().toLocaleDateString("da-DK") + ".\r\n";
      }
      document.getElementById("printRecoveryCodes").addEventListener("click", function () { window.print(); });
      document.getElementById("downloadRecoveryCodes").addEventListener("click", function () {
        var a = document.createElement("a");
        a.href = "data:text/plain;charset=utf-8," + encodeURIComponent(codesText());
        a.download = "skoleoverblikket-gendannelseskoder.txt";
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
      });
    })();
    </#outputformat>
  </script>

</@layout.page>
