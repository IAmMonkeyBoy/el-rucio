# Linux service deployment (systemd)

## 1) Publish on the server

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
cd /opt/elrucio-src
git pull
dotnet publish src/ElRucio.Host/ElRucio.Host.csproj -c Release -o /opt/elrucio
sudo systemctl restart elrucio
```

## Notes
- Data is stored under `src/ElRucio.Host/data` by default unless overridden with `ElRucio__DataDir`.
- Keep `/etc/elrucio/elrucio.env` out of git and readable only by root.
