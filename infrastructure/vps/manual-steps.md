# VPS manual steps

[harden.sh](harden.sh) does everything it can on the server itself. These are the steps it can't do: the ones in OVH, Dokploy, Grafana and on your own machine. Work through them in order.

## Before the script

1. **Emergency access.** Log in through the OVH KVM console (OVH Control Panel → the VPS → KVM) with the `debian` user's password. If there is no password, set one with `sudo passwd debian` first. That password keeps working on the console after SSH password login is turned off, so it's the way back in if SSH breaks.
2. **SSH key** on your own machine (Windows):

   ```powershell
   ssh-keygen -t ed25519          # set a passphrase
   type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh debian@<vps> "umask 077; mkdir -p ~/.ssh; cat >> ~/.ssh/authorized_keys"
   ssh debian@<vps>               # must log in without asking for the server password
   ```

   Add a second key from another device the same way, so losing one laptop doesn't lock you out. On a new VPS, paste the public key into the OVH order form instead.
3. **Grafana Cloud token.** Grafana Cloud portal → Access Policies → a policy with only `metrics:write` and `logs:write`, scoped to the stack → create a token. From the stack's Prometheus and Loki details pages, note the push URL and user number of each. The script asks for all five values the first time.
4. **Dokploy:**
   - Clear **External Port** on the Keycloak database. Check first that Keycloak connects to its database inside Docker (`KEYCLOAK_DB_URL` uses the service name, not the public IP).
   - Leave the app database's port 5435 open for now. The `migrate` job in `ci.yml` runs `psql` against it from GitHub Actions, so closing it breaks every deploy. It closes at the [task 54](../../tasks/54-move-vps.md) cutover.
   - Give the panel a domain with HTTPS. Update the `DOKPLOY_URL` secret in the GitHub `production` environment to that domain. Only then turn on **Disable access via ip:port**, or deploys and rollbacks fail.
   - Turn on 2FA for the Dokploy admin user.
   - Don't turn on Dokploy's Docker cleanup. It can remove unused volumes, which would delete the spare pgdata volume and the drill volume. The script installs its own weekly prune, which never touches volumes.

## Running the script

```powershell
scp infrastructure/vps/harden.sh debian@<vps>:
ssh -t debian@<vps> sudo bash harden.sh
```

The settings are listed at the top of the script. Password login is only turned off once you have logged in with your key; otherwise the script says so and leaves it on. On a new VPS, run it before installing Dokploy, with `LOCK_SSH=yes`.

## After the script

1. **Keep the SSH session open** and run `ssh debian@<vps>` in a new terminal. If it fails, fix it from the open session, or from the KVM console if that is gone.
2. **Reboot** in a quiet window: `ssh -t debian@<vps> sudo REBOOT=yes bash harden.sh`, or `sudo reboot`. This loads the new kernel and the Docker log limits.
3. **Check:**
   - The app and Keycloak log in.
   - The Dokploy panel loads, and a deploy goes through.
   - From outside the server: `nc -zv <vps> 5436` and `nc -zv <vps> 3000` fail.
   - Grafana Explore shows `up{job="integrations/node_exporter", service="skoleoverblikket"}` and the server's journal in Loki.
4. **Alert rules.** Once metrics arrive, import the `skoleoverblikket-host` group from [alert-rules.json](../grafana/alert-rules.json). Don't import it earlier: "VPS down" fires when there is no data.
5. **Contact point.** If not done yet, create the `Skoleoverblikket` email contact point and a notification policy for `service=skoleoverblikket` ([Grafana README](../grafana/README.md)). Until then, no alert is sent.

## Later, at the task 54 cutover

- Close the app database's port 5435.
- Add OVH's Edge Network Firewall (free, in front of the VPS), allowing only 22, 80 and 443. It is the only thing that blocks ports Docker publishes, because those bypass ufw. It doesn't track connections, so it also needs rules that let replies to the server's own outgoing traffic back in (established TCP, DNS replies).
