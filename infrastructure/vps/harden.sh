#!/usr/bin/env bash
# Hardens a Debian VPS that runs Dokploy. Idempotent: run it again after any change.
# Run it on a new VPS before installing Dokploy, and on existing ones as they are.
# The steps it can't do (OVH, Dokploy, Grafana, your SSH key): manual-steps.md
#
#   scp infrastructure/vps/harden.sh debian@<vps>:
#   ssh -t debian@<vps> sudo bash harden.sh
#
# Settings (environment variables, all optional; pass them after sudo):
#   ADMIN_USER   The only user allowed to SSH in. Default: the user who ran sudo.
#   SSH_PUBKEY   A public key to add to ADMIN_USER's authorized_keys.
#   LOCK_SSH     auto (default): turn off password login only once ADMIN_USER has
#                logged in with a key in the last 7 days. yes: skip that check.
#                no: leave sshd alone.
#   TIMEZONE     Default Europe/Copenhagen.
#   SWAP_SIZE    Default 2G. 0 skips.
#   REBOOT_TIME  When unattended-upgrades reboots after a kernel update. Default
#                05:30, after the backup agent's 02:00 full, 03:00 verify, 04:00 drill.
#   GCLOUD_ENV   File with the Grafana Cloud settings (see install_alloy). Without
#                it the script asks, or skips Alloy when there is no terminal.
#   REBOOT       yes: reboot at the end if one is pending. Default no.
#   SERVICE      service label on this host's metrics and logs. Default skoleoverblikket.
#                The host alerts in infrastructure/grafana/alert-rules.json watch
#                service="skoleoverblikket", which only the production VPS may carry:
#                "VPS down" adds up every host with it, so a second one would hide an
#                outage. Any other host (test box, the other VPS during a move) uses
#                another value, e.g. SERVICE=skoleoverblikket-new.
#
# Docker-published ports bypass ufw. The script lists them at the end; close them
# in Dokploy, not here.

set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

ADMIN_USER=${ADMIN_USER:-${SUDO_USER:-}}
LOCK_SSH=${LOCK_SSH:-auto}
TIMEZONE=${TIMEZONE:-Europe/Copenhagen}
SWAP_SIZE=${SWAP_SIZE:-2G}
REBOOT_TIME=${REBOOT_TIME:-05:30}
GCLOUD_ENV=${GCLOUD_ENV:-}
REBOOT=${REBOOT:-no}
SERVICE=${SERVICE:-skoleoverblikket}

TEXTFILE_DIR=/var/lib/node_exporter/textfile
NOTES=()

step() { printf '\n\033[1m== %s\033[0m\n' "$*"; }
log() { printf '   %s\n' "$*"; }
note() { NOTES+=("$*"); printf '   \033[33m! %s\033[0m\n' "$*"; }
die() { printf '\033[31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }

# put PATH [MODE] < content: writes only when the content differs. Sets CHANGED=1/0.
put() {
	local path=$1 mode=${2:-644} tmp
	tmp=$(mktemp)
	cat >"$tmp"
	if [[ -f $path ]] && cmp -s "$tmp" "$path"; then
		CHANGED=0
		rm -f "$tmp"
		return
	fi
	install -D -m "$mode" "$tmp" "$path"
	rm -f "$tmp"
	CHANGED=1
	log "wrote $path"
}

preflight() {
	[[ $EUID -eq 0 ]] || die "Run as root: sudo bash $0"
	# shellcheck source=/dev/null
	. /etc/os-release
	[[ $ID == debian ]] || die "Written for Debian, found $PRETTY_NAME"
	[[ -n $ADMIN_USER && $ADMIN_USER != root ]] || die "Set ADMIN_USER, or run through sudo as that user"
	id "$ADMIN_USER" >/dev/null 2>&1 || die "User $ADMIN_USER does not exist"
	log "$PRETTY_NAME, admin user $ADMIN_USER"
}

packages() {
	step "Packages"
	apt-get update -q
	apt-get install -y -q ufw unattended-upgrades jq curl gpg ca-certificates >/dev/null
}

timezone() {
	step "Timezone"
	if [[ $(timedatectl show -p Timezone --value) != "$TIMEZONE" ]]; then
		timedatectl set-timezone "$TIMEZONE"
		log "set to $TIMEZONE"
	fi
}

swap() {
	step "Swap"
	if [[ $SWAP_SIZE == 0 ]]; then
		log "skipped"
	elif swapon --show --noheadings | grep -q .; then
		log "already on"
	else
		# Recreate a swapfile without a swap header, e.g. from a run stopped before mkswap.
		if [[ $(blkid -p -s TYPE -o value /swapfile 2>/dev/null) != swap ]]; then
			rm -f /swapfile
			fallocate -l "$SWAP_SIZE" /swapfile
			chmod 600 /swapfile
			mkswap /swapfile >/dev/null
		fi
		swapon /swapfile
		grep -q '^/swapfile ' /etc/fstab || echo '/swapfile none swap sw 0 0' >>/etc/fstab
		log "$SWAP_SIZE swapfile on"
	fi
	# Swap is a safety net against OOM kills during builds, not extra RAM.
	put /etc/sysctl.d/90-swappiness.conf <<<'vm.swappiness = 10'
	((CHANGED)) && sysctl -q -p /etc/sysctl.d/90-swappiness.conf
	return 0
}

journald() {
	step "Journal size"
	put /etc/systemd/journald.conf.d/00-size.conf <<-'EOF'
		[Journal]
		SystemMaxUse=500M
	EOF
	if ((CHANGED)); then
		systemctl restart systemd-journald
		journalctl --vacuum-size=500M -q
	fi
}

no_llmnr() {
	step "LLMNR/mDNS off"
	if ! systemctl is-active --quiet systemd-resolved; then
		log "systemd-resolved not running, nothing to do"
		return
	fi
	put /etc/systemd/resolved.conf.d/00-no-llmnr.conf <<-'EOF'
		[Resolve]
		LLMNR=no
		MulticastDNS=no
	EOF
	((CHANGED)) && systemctl restart systemd-resolved
	return 0
}

auto_upgrades() {
	step "Unattended upgrades + nightly reboot"
	put /etc/apt/apt.conf.d/20auto-upgrades <<-'EOF'
		APT::Periodic::Update-Package-Lists "1";
		APT::Periodic::Unattended-Upgrade "1";
	EOF
	# Kernel fixes only take effect after a reboot. Debian's default origins are
	# security updates only, so Docker and Alloy are never upgraded unattended.
	put /etc/apt/apt.conf.d/52reboot <<-EOF
		Unattended-Upgrade::Automatic-Reboot "true";
		Unattended-Upgrade::Automatic-Reboot-WithUsers "true";
		Unattended-Upgrade::Automatic-Reboot-Time "$REBOOT_TIME";
		Unattended-Upgrade::Remove-Unused-Kernel-Packages "true";
	EOF
	systemctl enable --now unattended-upgrades >/dev/null 2>&1
}

docker_logs() {
	step "Docker log rotation"
	local want='{"log-driver":"json-file","log-opts":{"max-size":"20m","max-file":"3"}}' merged
	if [[ -s /etc/docker/daemon.json ]]; then
		# Another driver's log-opts (syslog-address, ...) would stop Docker from starting
		# under json-file, so drop them when the driver changes.
		merged=$(jq --argjson w "$want" 'if has("log-driver") and .["log-driver"] != $w["log-driver"] then del(.["log-opts"]) else . end | . * $w' /etc/docker/daemon.json) ||
			die "/etc/docker/daemon.json is not valid JSON"
	else
		merged=$(jq . <<<"$want")
	fi
	# No live-restore: it conflicts with Swarm, which Dokploy uses.
	put /etc/docker/daemon.json <<<"$merged"
	if ((CHANGED)) && docker_restart_pending; then
		note "Docker log limits apply after Docker restarts (the reboot does it) and only to containers created after that"
	fi
}

# Docker reads daemon.json when it starts, so a change is pending until Docker has
# started after the file's last write. Checked each run, so a rerun still knows.
docker_restart_pending() {
	[[ -f /etc/docker/daemon.json ]] && systemctl is-active --quiet docker || return 1
	local started
	started=$(systemctl show docker.service -p ActiveEnterTimestamp --value --timestamp=unix 2>/dev/null || true)
	started=${started#@}
	[[ $started =~ ^[0-9]+$ ]] || return 1
	(($(stat -c %Y /etc/docker/daemon.json) > started))
}

docker_prune() {
	step "Weekly Docker image prune"
	# Images and build cache only. Never volumes: the spare pgdata volume and the
	# drill volume are unused on purpose (docs/RESTORE.md).
	put /etc/systemd/system/docker-prune.service <<-'EOF'
		[Unit]
		Description=Remove Docker images and build cache unused for 7 days (never volumes)
		ConditionPathExists=/usr/bin/docker

		[Service]
		Type=oneshot
		ExecStart=/usr/bin/docker image prune --all --force --filter until=168h
		ExecStart=/usr/bin/docker builder prune --all --force --filter until=168h
	EOF
	put /etc/systemd/system/docker-prune.timer <<-'EOF'
		[Unit]
		Description=Weekly Docker image prune

		[Timer]
		OnCalendar=Sun 06:30
		Persistent=true

		[Install]
		WantedBy=timers.target
	EOF
	systemctl daemon-reload
	systemctl enable --now docker-prune.timer >/dev/null 2>&1
}

firewall() {
	step "ufw"
	# Allow the port sshd really listens on before turning ufw on, or a VPS with
	# SSH on another port locks us out.
	local ssh_ports
	ssh_ports=$(sshd -T 2>/dev/null | awk '$1 == "port" {print $2}' | sort -un | tr '\n' ' ' || true)
	[[ -n $ssh_ports ]] || die "Could not read the SSH port from 'sshd -T'. ufw left as it was."
	for port in $ssh_ports; do
		ufw limit "$port/tcp" comment ssh >/dev/null
	done
	ufw default deny incoming >/dev/null
	ufw default allow outgoing >/dev/null
	ufw allow 80/tcp comment http >/dev/null
	ufw allow 443/tcp comment https >/dev/null
	ufw allow 443/udp comment http3 >/dev/null
	ufw --force enable >/dev/null
	log "deny incoming except SSH ${ssh_ports% } (rate-limited), 80, 443"
}

ssh_hardening() {
	step "SSH"
	local home ak keys
	home=$(getent passwd "$ADMIN_USER" | cut -d: -f6)
	ak=$home/.ssh/authorized_keys

	if [[ -n ${SSH_PUBKEY:-} ]]; then
		ssh-keygen -lf - <<<"$SSH_PUBKEY" >/dev/null 2>&1 || die "SSH_PUBKEY is not a valid public key"
		install -d -m 700 -o "$ADMIN_USER" -g "$(id -gn "$ADMIN_USER")" "$home/.ssh"
		touch "$ak"
		grep -qxF "$SSH_PUBKEY" "$ak" || { echo "$SSH_PUBKEY" >>"$ak" && log "added key to $ak"; }
		chown "$ADMIN_USER:" "$ak"
		chmod 600 "$ak"
	fi

	keys=$(ssh-keygen -lf "$ak" 2>/dev/null | wc -l || true)
	case $LOCK_SSH in
	no)
		log "LOCK_SSH=no, sshd left alone"
		return
		;;
	yes)
		((keys > 0)) || die "$ak has no valid key. Refusing to turn off password login."
		;;
	auto)
		if ((keys == 0)); then
			note "SSH password login still ON: $ADMIN_USER has no key in $ak"
			return
		fi
		# grep without -q: an early exit would SIGPIPE journalctl and fail the pipeline.
		if ! journalctl -u ssh.service --since -7d -q 2>/dev/null | grep -F "Accepted publickey for $ADMIN_USER " >/dev/null; then
			note "SSH password login still ON: no key login by $ADMIN_USER in the last 7 days. Log in with your key, then run again."
			return
		fi
		;;
	*) die "LOCK_SSH must be auto, yes or no" ;;
	esac

	# 00- so it wins: sshd keeps the first value it reads, and 50-cloud-init.conf
	# turns PasswordAuthentication on. TCP forwarding stays on for the SSH tunnels
	# (backup console, Keycloak admin, Dokploy panel).
	put /etc/ssh/sshd_config.d/00-hardening.conf <<-EOF
		# Managed by infrastructure/vps/harden.sh
		PasswordAuthentication no
		KbdInteractiveAuthentication no
		PubkeyAuthentication yes
		PermitRootLogin no
		AllowUsers $ADMIN_USER
		MaxAuthTries 3
		LoginGraceTime 30
		X11Forwarding no
	EOF
	if ! sshd -t; then
		rm -f /etc/ssh/sshd_config.d/00-hardening.conf
		die "sshd config invalid, drop-in removed, nothing changed"
	fi
	sshd -T | grep -qx 'passwordauthentication no' || die "Another sshd setting overrides the drop-in"
	if ((CHANGED)); then
		systemctl reload ssh
		note "Password login is OFF. Keep this session open and test 'ssh $ADMIN_USER@<vps>' in a new terminal."
	fi
}

# Host metrics and the journal to Grafana Cloud. App logs and traces already go
# there over OTLP, so container logs are not shipped. GCLOUD_ENV holds:
#   GCLOUD_PROM_URL=https://prometheus-....grafana.net/api/prom/push
#   GCLOUD_PROM_USER=<number>
#   GCLOUD_LOKI_URL=https://logs-....grafana.net/loki/api/v1/push
#   GCLOUD_LOKI_USER=<number>
#   GCLOUD_TOKEN=<access policy token with metrics:write and logs:write only>
install_alloy() {
	step "Alloy -> Grafana Cloud"
	local env=/etc/alloy/grafana-cloud.env env_changed=0
	if [[ -n $GCLOUD_ENV ]]; then
		[[ -f $GCLOUD_ENV ]] || die "GCLOUD_ENV file $GCLOUD_ENV not found"
		# Alloy reads it only at start, so a rotated token needs a restart (below).
		put "$env" 600 <"$GCLOUD_ENV"
		env_changed=$CHANGED
	elif [[ ! -f $env ]]; then
		if [[ ! -t 0 ]]; then
			note "Alloy skipped: no $env and no terminal to ask"
			return
		fi
		local p_url p_user l_url l_user token
		read -rp "   Prometheus remote_write URL: " p_url
		read -rp "   Prometheus user (number): " p_user
		read -rp "   Loki push URL: " l_url
		read -rp "   Loki user (number): " l_user
		read -rsp "   Access policy token (hidden): " token
		echo
		printf 'GCLOUD_PROM_URL=%s\nGCLOUD_PROM_USER=%s\nGCLOUD_LOKI_URL=%s\nGCLOUD_LOKI_USER=%s\nGCLOUD_TOKEN=%s\n' \
			"$p_url" "$p_user" "$l_url" "$l_user" "$token" | put "$env" 600
		env_changed=1
	fi
	chmod 600 "$env"

	if [[ ! -f /etc/apt/sources.list.d/grafana.list ]]; then
		install -d -m 755 /etc/apt/keyrings
		curl -fsSL https://apt.grafana.com/gpg.key | gpg --dearmor --yes -o /etc/apt/keyrings/grafana.gpg
		echo 'deb [signed-by=/etc/apt/keyrings/grafana.gpg] https://apt.grafana.com stable main' >/etc/apt/sources.list.d/grafana.list
		apt-get update -q
	fi
	apt-get install -y -q alloy >/dev/null

	# node_reboot_required, for the "reboot pending" alert.
	install -d -m 755 "$TEXTFILE_DIR"
	put /usr/local/sbin/reboot-required-metric 755 <<-EOF
		#!/bin/sh
		v=0
		[ -f /run/reboot-required ] && v=1
		printf '# TYPE node_reboot_required gauge\nnode_reboot_required %s\n' "\$v" >$TEXTFILE_DIR/reboot.prom.tmp
		mv $TEXTFILE_DIR/reboot.prom.tmp $TEXTFILE_DIR/reboot.prom
	EOF
	put /etc/systemd/system/reboot-required-metric.service <<-'EOF'
		[Unit]
		Description=Write node_reboot_required for Alloy

		[Service]
		Type=oneshot
		ExecStart=/usr/local/sbin/reboot-required-metric
	EOF
	put /etc/systemd/system/reboot-required-metric.timer <<-'EOF'
		[Unit]
		Description=Write node_reboot_required every 15 minutes

		[Timer]
		OnBootSec=1min
		OnUnitActiveSec=15min

		[Install]
		WantedBy=timers.target
	EOF

	put /etc/systemd/system/alloy.service.d/10-grafana-cloud.conf <<-EOF
		[Service]
		EnvironmentFile=$env
		SupplementaryGroups=systemd-journal
	EOF
	local restart=$((CHANGED || env_changed))

	# job/instance match Grafana Cloud's Linux integration, so its dashboards work.
	put /etc/alloy/config.alloy <<-EOF
		// Managed by infrastructure/vps/harden.sh
		prometheus.exporter.unix "host" {
		  disable_collectors = ["arp", "bonding", "btrfs", "fibrechannel", "infiniband", "ipvs", "mdadm", "nfs", "nfsd", "tapestats", "xfs", "zfs"]
		  textfile {
		    directory = "$TEXTFILE_DIR"
		  }
		  filesystem {
		    mount_points_exclude = "^/(dev|proc|run|sys|var/lib/docker/.+|var/lib/containerd/.+)(\$|/)"
		    fs_types_exclude     = "^(autofs|binfmt_misc|cgroup2?|configfs|debugfs|devpts|devtmpfs|fusectl|hugetlbfs|mqueue|nsfs|overlay|proc|pstore|securityfs|squashfs|sysfs|tmpfs|tracefs)\$"
		  }
		  netdev {
		    device_exclude = "^(lo|veth.*|br-.*|docker.*|vx-.*)\$"
		  }
		}

		discovery.relabel "host" {
		  targets = prometheus.exporter.unix.host.targets
		  rule {
		    target_label = "instance"
		    replacement  = constants.hostname
		  }
		  rule {
		    target_label = "job"
		    replacement  = "integrations/node_exporter"
		  }
		  rule {
		    target_label = "service"
		    replacement  = "$SERVICE"
		  }
		}

		prometheus.scrape "host" {
		  targets         = discovery.relabel.host.output
		  scrape_interval = "60s"
		  forward_to      = [prometheus.remote_write.cloud.receiver]
		}

		prometheus.remote_write "cloud" {
		  endpoint {
		    url = sys.env("GCLOUD_PROM_URL")
		    basic_auth {
		      username = sys.env("GCLOUD_PROM_USER")
		      password = sys.env("GCLOUD_TOKEN")
		    }
		  }
		}

		loki.relabel "journal" {
		  forward_to = []
		  rule {
		    source_labels = ["__journal__systemd_unit"]
		    target_label  = "unit"
		  }
		  rule {
		    source_labels = ["__journal_priority_keyword"]
		    target_label  = "level"
		  }
		}

		loki.source.journal "host" {
		  max_age       = "12h"
		  relabel_rules = loki.relabel.journal.rules
		  labels        = {job = "integrations/node_exporter", instance = constants.hostname, service = "$SERVICE"}
		  forward_to    = [loki.write.cloud.receiver]
		}

		loki.write "cloud" {
		  endpoint {
		    url = sys.env("GCLOUD_LOKI_URL")
		    basic_auth {
		      username = sys.env("GCLOUD_LOKI_USER")
		      password = sys.env("GCLOUD_TOKEN")
		    }
		  }
		}
	EOF
	((CHANGED)) && restart=1
	alloy fmt /etc/alloy/config.alloy >/dev/null || die "Alloy config invalid"

	systemctl daemon-reload
	systemctl enable --now reboot-required-metric.timer >/dev/null 2>&1
	systemctl enable alloy >/dev/null 2>&1
	if ((restart)) || ! systemctl is-active --quiet alloy; then
		systemctl restart alloy
	fi
	sleep 5
	systemctl is-active --quiet alloy || die "Alloy failed to start: journalctl -u alloy -n 50"
	log "running; check Grafana for instance=\"$(hostname)\""
}

audit() {
	step "Audit"
	if command -v docker >/dev/null; then
		local open
		open=$({ docker ps --format '{{.Ports}}' && docker service ls --format '{{.Ports}}' 2>/dev/null; } |
			grep -oE '(0\.0\.0\.0|\[::\]|\*):[0-9]+' | grep -oE '[0-9]+$' | sort -un | grep -vxE '80|443' | tr '\n' ' ' || true)
		[[ -n $open ]] && note "Docker publishes these ports to the internet, ufw can't block them: $open"
	fi
	if [[ -f /run/reboot-required ]]; then
		note "Reboot pending: package updates (running kernel $(uname -r))"
	fi
	if docker_restart_pending; then
		note "Reboot pending: Docker has not loaded /etc/docker/daemon.json yet"
	fi
	return 0
}

preflight
packages
timezone
swap
journald
no_llmnr
auto_upgrades
docker_logs
docker_prune
firewall
ssh_hardening
install_alloy
audit

step "Done"
for n in "${NOTES[@]}"; do log "- $n"; done
if [[ $REBOOT == yes ]] && { [[ -f /run/reboot-required ]] || docker_restart_pending; }; then
	log "Rebooting in 1 minute (shutdown -c cancels)"
	shutdown -r +1
fi
