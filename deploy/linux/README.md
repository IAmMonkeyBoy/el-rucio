# Linux service deployment (systemd)

## 1) Publish on the server

Quick path (one command installer):

```bash
sudo bash /opt/elrucio-src/deploy/linux/install.sh
```

Run preflight only:

```bash
sudo bash /opt/elrucio-src/deploy/linux/preflight.sh
```

Manual path:

```bash
cd /opt/elrucio-src
dotnet restore ElRucio.slnx
dotnet publish src/ElRucio.Host/ElRucio.Host.csproj -c Release -o /opt/elrucio
```

## 2) Install environment file

```bash
sudo mkdir -p /etc/elrucio
sudo cp /opt/elrucio-src/deploy/linux/elrucio.env.example /etc/elrucio/elrucio.env
sudo chmod 600 /etc/elrucio/elrucio.env
sudo nano /etc/elrucio/elrucio.env
```

Set:
- `Telegram__BotToken`
- `Voice__OpenAiApiKey`
- `ElRucio__AllowedChatIds__0`

## 3) Install service unit

```bash
sudo cp /opt/elrucio-src/deploy/linux/elrucio.service /etc/systemd/system/elrucio.service
sudo systemctl daemon-reload
sudo systemctl enable elrucio
sudo systemctl start elrucio
```

## 4) Verify

```bash
sudo systemctl status elrucio --no-pager
sudo journalctl -u elrucio -f
```

## 5) Update rollout

```bash
sudo bash /opt/elrucio-src/deploy/linux/update.sh
```

`update.sh` runs `backup.sh` first by default. Disable backup for a single run:

```bash
sudo RUN_BACKUP=0 bash /opt/elrucio-src/deploy/linux/update.sh
```

Disable preflight for a single run:

```bash
sudo RUN_PREFLIGHT=0 bash /opt/elrucio-src/deploy/linux/update.sh
```

## 6) Healthcheck

```bash
sudo bash /opt/elrucio-src/deploy/linux/healthcheck.sh
```

## 7) Manual backup

```bash
sudo bash /opt/elrucio-src/deploy/linux/backup.sh
```

## 8) Nightly backup timer (systemd)

Enable nightly backups (default 03:15 UTC with small random delay):

```bash
sudo bash /opt/elrucio-src/deploy/linux/enable-backup-timer.sh
```

Check timer:

```bash
sudo systemctl status elrucio-backup.timer --no-pager
sudo systemctl list-timers --all | grep elrucio-backup
```

Run backup immediately (on demand):

```bash
sudo systemctl start elrucio-backup.service
```

Disable timer:

```bash
sudo systemctl disable --now elrucio-backup.timer
```

## 9) Restore from backup

List backups:

```bash
ls -1 /var/backups/elrucio/elrucio-backup-*.tar.gz
```

Restore one archive (stops service, restores DB/env, starts service):

```bash
sudo bash /opt/elrucio-src/deploy/linux/restore.sh /var/backups/elrucio/elrucio-backup-YYYYMMDD-HHMMSSZ.tar.gz
```

Post-restore verification:

```bash
sudo systemctl status elrucio --no-pager
sudo journalctl -u elrucio -f
```

## 10) Doctor report (one-command diagnostics)

```bash
sudo bash /opt/elrucio-src/deploy/linux/doctor.sh
```

Includes:
- preflight checks
- service/process/log healthcheck
- latest backup recency warning

## Notes
- Data is stored under `src/ElRucio.Host/data` by default unless overridden with `ElRucio__DataDir`.
- Keep `/etc/elrucio/elrucio.env` out of git and readable only by root.
