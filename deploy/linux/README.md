# Linux deployment (strict split paths)

Canonical production layout:

- Source checkout: `/opt/elrucio-src`
- Published app: `/opt/elrucio`
- Runtime data (SQLite/media/artifacts): `/var/lib/elrucio/data`
- Environment file: `/etc/elrucio/elrucio.env`
- Backups: `/var/backups/elrucio`

This keeps runtime state out of both source and published app directories.

## Mode A: system service (recommended for servers)

### 1) Install/update with one command

```bash
sudo bash /opt/elrucio-src/deploy/linux/install.sh
```

Run preflight only:

```bash
sudo bash /opt/elrucio-src/deploy/linux/preflight.sh
```

### 2) Configure env file

```bash
sudo mkdir -p /etc/elrucio
sudo cp /opt/elrucio-src/deploy/linux/elrucio.env.example /etc/elrucio/elrucio.env
sudo chmod 640 /etc/elrucio/elrucio.env
sudo nano /etc/elrucio/elrucio.env
```

Set at least:
- `Platform__Provider=slack`
- `Slack__AppToken`
- `Slack__BotToken`
- `Voice__OpenAiApiKey` (if voice enabled)
- `ElRucio__AllowedChatIds__0`
- `ElRucio__DataDir=/var/lib/elrucio/data`

### 3) Verify service

```bash
sudo systemctl status elrucio --no-pager
sudo journalctl -u elrucio -f
```

### 4) Update rollout

```bash
sudo bash /opt/elrucio-src/deploy/linux/update.sh
```

`update.sh` runs preflight and backup by default. Disable one-off:

```bash
sudo RUN_PREFLIGHT=0 bash /opt/elrucio-src/deploy/linux/update.sh
sudo RUN_BACKUP=0 bash /opt/elrucio-src/deploy/linux/update.sh
```

## Mode B: user service (rootless)

This mode keeps service ownership with the current user and uses user-scoped paths.

### 1) Publish app and create user env

```bash
mkdir -p ~/.local/share/elrucio/app ~/.config/elrucio ~/.local/state/elrucio/data
cd /opt/elrucio-src
dotnet restore ElRucio.slnx
dotnet publish src/ElRucio.Host/ElRucio.Host.csproj -c Release -o ~/.local/share/elrucio/app
cp deploy/linux/elrucio.env.example ~/.config/elrucio/elrucio.env
chmod 600 ~/.config/elrucio/elrucio.env
```

Edit `~/.config/elrucio/elrucio.env` and set:

- `ElRucio__DataDir=/home/<user>/.local/state/elrucio/data`
- your Slack/OpenAI settings (or Telegram settings if `Platform__Provider=telegram`).

### 2) Install and start user unit

```bash
mkdir -p ~/.config/systemd/user
cp /opt/elrucio-src/deploy/linux/elrucio.user.service ~/.config/systemd/user/elrucio.service
systemctl --user daemon-reload
systemctl --user enable --now elrucio
```

Optional persistence across logouts:

```bash
loginctl enable-linger "$USER"
```

### 3) Verify

```bash
systemctl --user status elrucio --no-pager
journalctl --user -u elrucio -f
```

## Backup and restore

System mode backup/restore scripts:

Manual backup:

```bash
sudo bash /opt/elrucio-src/deploy/linux/backup.sh
```

Nightly backup timer:

```bash
sudo bash /opt/elrucio-src/deploy/linux/enable-backup-timer.sh
sudo systemctl status elrucio-backup.timer --no-pager
```

Restore:

```bash
sudo bash /opt/elrucio-src/deploy/linux/restore.sh /var/backups/elrucio/elrucio-backup-YYYYMMDD-HHMMSSZ.tar.gz
```

Backup/restore use `ElRucio__DataDir` from env when present, otherwise `/var/lib/elrucio/data`.

User mode tip: use user-owned backups from your configured data dir (for example `~/.local/state/elrucio/data`) and `~/.config/elrucio/elrucio.env`.

## Health and diagnostics

```bash
sudo bash /opt/elrucio-src/deploy/linux/healthcheck.sh
sudo bash /opt/elrucio-src/deploy/linux/doctor.sh
sudo bash /opt/elrucio-src/deploy/linux/hardening-check.sh
```

## Notes

- Keep `/etc/elrucio/elrucio.env` out of git.
- Production should not persist runtime data under source or app directories.
- System unit hardening allows writes under `/var/lib/elrucio`; if you change `ElRucio__DataDir` outside that tree, update `ReadWritePaths` in `deploy/linux/elrucio.service`.
