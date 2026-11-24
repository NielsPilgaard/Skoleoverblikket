# VPS manual steps

[harden.sh](harden.sh) does everything it can on the server itself. These are the steps it can't do: the ones in OVH, Dokploy, Grafana and on your own machine. Work through them in order.

## Before the script

1. **Emergency access.** Make sure you can log in through the OVH KVM console (OVH Control Panel → the VPS → KVM). It is the way back in if SSH breaks.
2. **SSH key** on your own machine (Windows):

   ```powershell
   ssh-keygen -t ed25519          # set a passphrase
   type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh debian@<vps> "umask 077; mkdir -p ~/.ssh; cat >> ~/.ssh/authorized_keys"
   ssh debian@<vps>               # must log in without asking for the server password
   ```

   Add a second key from another device the same way, so losing one laptop doesn't lock you out. On a new VPS, paste the public key into the OVH order form instead.
3. **Grafana Cloud token.** Grafana Cloud portal → Access Policies → a policy with only `metrics:write` and `logs:write`, scoped to the stack → create a token. From the stack's Prometheus and Loki details pages, note the push URL and user number of each. The script asks for all five values the first time.
4. **Dokploy:**
   - No database should have an **External Port**. Reach databases through an SSH tunnel instead.
   - The panel should have a domain with HTTPS and **Disable access via ip:port** turned on. Point the `DOKPLOY_URL` secret in the GitHub `production` environment at that domain first, or deploys fail.
   - 2FA on the Dokploy admin user.
   - Don't turn on Dokploy's Docker cleanup. It can remove unused volumes, which would delete the spare pgdata volume and the drill volume ([RESTORE.md](../../docs/RESTORE.md)). The script installs its own weekly prune, which never touches volumes.

## Running the script

```powershell
scp infrastructure/vps/harden.sh debian@<vps>:
ssh -t debian@<vps> sudo bash harden.sh
```

The settings are listed at the top of the script. Password login is only turned off once you have logged in with your key; otherwise the script says so and leaves it on. On a new VPS, run it before installing Dokploy, with `LOCK_SSH=yes`.

## After the script

1. **Keep the SSH session open** and run `ssh debian@<vps>` in a new terminal. If it fails, fix it from the open session, or from the KVM console.
2. **Reboot** in a quiet window if the script says one is pending: `ssh -t debian@<vps> sudo REBOOT=yes bash harden.sh`, or `sudo reboot`.
3. **Check:**
   - The app and Keycloak log in.
   - The Dokploy panel loads, and a deploy goes through.
   - From outside the server, only 22, 80 and 443 answer (`nmap <vps>`).
   - Grafana Explore shows `up{job="integrations/node_exporter", service="skoleoverblikket"}` and the server's journal in Loki.
4. **Alert rules.** Once metrics arrive, import the `skoleoverblikket-host` group from [alert-rules.json](../grafana/alert-rules.json). Don't import it earlier: "VPS down" fires when there is no data.
5. **Contact point.** The rules only notify through the `Skoleoverblikket` email contact point and its notification policy ([Grafana README](../grafana/README.md)).

## Edge firewall

Ports published by Docker bypass ufw. OVH's Edge Network Firewall (free, in front of the VPS) catches them: allow only 22, 80 and 443. It doesn't track connections, so it also needs rules that let replies to the server's own outgoing traffic back in (established TCP, DNS replies).
