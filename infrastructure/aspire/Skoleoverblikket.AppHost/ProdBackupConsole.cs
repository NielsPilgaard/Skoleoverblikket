using System.Diagnostics;

/// <summary>
/// The prod backup console from the Aspire dashboard (run mode only): what scripts/backup-console.ps1
/// does, plus the SSH commands from docs/RESTORE.md as dashboard commands. Added only when
/// BACKUP_CONSOLE_SERVER (env var or user secret) names the VPS, e.g. ubuntu@203.0.113.10.
/// The tunnel uses local port 9091, because the dev backup agent has 9090.
/// </summary>
internal static class ProdBackupConsole
{
    private const int LocalPort = 9091;
    private const string Agent = "$(docker ps -qf label=com.docker.compose.service=backup-agent)";

    public static void Add(IDistributedApplicationBuilder builder)
    {
        var server = builder.Configuration["BACKUP_CONSOLE_SERVER"];
        if (string.IsNullOrWhiteSpace(server) || server.StartsWith('-'))
        {
            return;
        }

        // The tunnel only runs when started from the dashboard, never on a plain `aspire run`.
        builder.AddExecutable("prod-backup-console", "ssh", ".",
                              "-N", "-o", "ExitOnForwardFailure=yes", "-o", "ServerAliveInterval=30",
                              "-L", $"{LocalPort}:127.0.0.1:9090", server)
               .WithExplicitStart()
               .WithCommand("open-console", "Open console", async context =>
               {
                   var (ok, output) = await SshAsync(server, $"docker exec {Agent} cat /var/lib/backup-agent/console-link", context.CancellationToken);
                   var link = output.Trim();
                   if (!ok || !link.StartsWith("http://localhost:9090/access?key=", StringComparison.Ordinal))
                   {
                       return CommandResults.Failure($"Couldn't read the access link. Is the backup-agent container running? {output}");
                   }

                   Process.Start(new ProcessStartInfo(link.Replace("localhost:9090", $"localhost:{LocalPort}", StringComparison.Ordinal)) { UseShellExecute = true });
                   return CommandResults.Success();
               }, new CommandOptions
               {
                   Description = "Reads the access link from the agent and opens the console through the tunnel. Start the resource first.",
                   IconName = "Open",
                   IsHighlighted = true,
                   UpdateState = context => context.ResourceSnapshot.State?.Text == KnownResourceStates.Running
                       ? ResourceCommandState.Enabled
                       : ResourceCommandState.Disabled,
               })
               .WithCommand("pgbackrest-info", "pgBackRest info", context =>
                   RunAsync(server, $"docker exec {Agent} pgbackrest --stanza=main info", "pgBackRest info", context.CancellationToken),
                   new CommandOptions { Description = "Every backup in the repo and the WAL range.", IconName = "Database" })
               .WithCommand("agent-logs", "Agent logs", context =>
                   RunAsync(server, $"docker logs --tail 300 {Agent} 2>&1", "Last 300 lines", context.CancellationToken),
                   new CommandOptions { Description = "The backup agent container's last 300 log lines.", IconName = "DocumentText" })
               .WithCommand("expire", "Expire old backups", context =>
                   RunAsync(server, $"docker exec {Agent} pgbackrest --stanza=main expire", "Expired", context.CancellationToken),
                   new CommandOptions
                   {
                       Description = "Runs pgbackrest expire with the retention in pgbackrest.conf, as the agent does after each full backup (RESTORE.md §11).",
                       IconName = "Delete",
                       ConfirmationMessage = "Delete every backup older than the retention in pgbackrest.conf from the prod repo? This can't be undone.",
                   });
    }

    private static async Task<ExecuteCommandResult> RunAsync(string server, string command, string message, CancellationToken cancellationToken)
    {
        var (ok, output) = await SshAsync(server, command, cancellationToken);
        return ok
            ? CommandResults.Success(message, output, CommandResultFormat.Text, displayImmediately: true)
            : CommandResults.Failure(message, output, CommandResultFormat.Text);
    }

    /// <summary>One command on the VPS. BatchMode: fail instead of waiting for a password prompt nobody sees.</summary>
    private static async Task<(bool Ok, string Output)> SshAsync(string server, string command, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("ssh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=15", server, command })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = $"{await stdout}{await stderr}".Trim();
        return process.ExitCode == 255
            ? (false, $"Can't SSH to {server}. Check the address and your key: ssh {server}\n{output}")
            : (process.ExitCode == 0, output);
    }
}
