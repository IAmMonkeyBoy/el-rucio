# El Rucio

Local-first AI assistant: Slack-first bot bridge -> local GitHub Copilot SDK runtime (Telegram also supported).

## What this build includes
- Slack Socket Mode transport (default) with Telegram transport support.
- Copilot SDK-backed runtime sessions with `chat_id -> session_id` SQLite mapping.
- Full memory mode: SQLite + FTS5 + semantic/episodic sectors + salience decay.
- Safety gate + approval queue (`/approve`, `/cancel`).
- Scheduler with cron polling loop and chat-command controls.
- Voice STT via OpenAI Whisper transcription.
- Video feature selected as explicit stub (interface in place, provider not wired).
- Service install artifacts generator (manual install commands/files).

## Prerequisites
- .NET 10 SDK.
- GitHub Copilot CLI installed and authenticated.
- Slack app credentials (Socket Mode app token + bot token) for default provider.
- OpenAI API key for STT if voice is enabled.

## Configuration
Primary config file: `src/ElRucio.Host/appsettings.json`.

Set these values before first run:
- `Platform:Provider` (`slack` by default, or `telegram`)
- `Slack:AppToken`
- `Slack:BotToken`
- `ElRucio:AllowedChatIds` (recommended)
- `Voice:OpenAiApiKey` (if STT enabled)
- `Video:ApiKey` (if video/image analysis enabled with `Video:Provider=openai`)
- install `ffmpeg` on host for video file analysis (frame sampling)

Environment variable overrides are supported via standard .NET conventions, for example:
- `Platform__Provider`
- `Slack__AppToken`
- `Slack__BotToken`
- `Voice__OpenAiApiKey`
- `ElRucio__AllowedChatIds__0`

## Copilot SDK auth prerequisites
El Rucio uses the official .NET package `GitHub.Copilot.SDK` and standard Copilot CLI auth flow.

Verify:
1) `copilot --version`
2) `copilot auth status`

If your environment requires custom CLI path/url, set:
- `Copilot:CliPath`
- `Copilot:CliUrl`

## Run
From repo root:

```bash
dotnet restore ElRucio.slnx
dotnet build ElRucio.slnx
dotnet run --project src/ElRucio.Host/ElRucio.Host.csproj
```

## Docker (verification path)

Build image:

```bash
docker build -t elrucio:local .
```

Run container with strict state separation (`/var/lib/elrucio` volume):

```bash
docker run --rm -it \
  -v elrucio-data:/var/lib/elrucio \
  -e Platform__Provider="slack" \
  -e Slack__AppToken="<xapp-token>" \
  -e Slack__BotToken="<xoxb-token>" \
  -e Voice__OpenAiApiKey="<openai-key>" \
  -e ElRucio__AllowedChatIds__0="slack:<channel-id>" \
  elrucio:local
```

One-time Copilot auth bootstrap in the same persistent volume:

```bash
docker run --rm -it \
  -v elrucio-data:/var/lib/elrucio \
  --entrypoint /bin/sh \
  elrucio:local -c "copilot auth login"
```

Optional auth check:

```bash
docker run --rm -it \
  -v elrucio-data:/var/lib/elrucio \
  --entrypoint /bin/sh \
  elrucio:local -c "copilot auth status"
```

Helper script (same flow):

```bash
bash scripts/docker-auth.sh login
bash scripts/docker-auth.sh status
```

Notes:
- The container runs as non-root user `elrucio`.
- Runtime data is persisted under `ElRucio__DataDir=/var/lib/elrucio/data`.
- `ffmpeg` is installed in the image for video frame extraction.
- Copilot CLI is installed in the image at `/usr/local/bin/copilot`.
- Copilot auth state is stored under `/var/lib/elrucio`; keep that volume persistent.

## Chat commands
- `/start`
- `/status`
- `/newchat`
- `/approve <id>`
- `/cancel <id>`
- `/voice on|off`
- `/schedule create <cron>|<prompt>`
- `/schedule list`
- `/schedule pause <id>`
- `/schedule resume <id>`
- `/schedule delete <id>`

Provider note:
- Slack is the default provider.
- Telegram remains supported by setting `Platform:Provider=telegram` and `Telegram:BotToken`.

## Try these (10 prompts)
1. `Summarize what you can do in 5 bullets.`
2. `Remember that I prefer short answers and dark themes.`
3. `What do you remember about my preferences?`
4. `Create a quick checklist for tomorrow morning.`
5. `Explain the difference between semantic and episodic memory in this system.`
6. `Draft a daily standup update from these notes: ...`
7. `Plan a refactor for a medium-sized .NET service.`
8. `Schedule this prompt every day at 8:00 UTC | Give me a 3-bullet daily focus list.`
9. `Analyze this voice note and return action items.`
10. `Pretend I asked a risky command and show how approval works.`

## Troubleshooting
- Bot not responding:
  - confirm provider-specific credentials (`Slack:AppToken` + `Slack:BotToken` for default Slack mode, or `Telegram:BotToken` for Telegram mode)
  - clear stale updates by restarting service
  - verify chat id is allowed (for Slack use `slack:<channel-id>`)
- Copilot errors:
  - run `copilot auth status`
  - ensure local Copilot CLI is installed and authenticated
- STT not working:
  - verify `Voice:OpenAiApiKey`
  - inspect downloaded voice/media files under `data/media/...`
- Scheduler not firing:
  - check cron syntax
  - ensure `Scheduler:Enabled=true`
  - review `scheduled_tasks` rows in SQLite

## Service setup artifacts
When `ServiceInstall:Enabled=true`, startup generates files under `data/service/`:
- Windows: `windows-service.txt` (`sc.exe` commands)
- Linux: `elrucio.service` (systemd user service template)
- macOS: `com.elrucio.agent.plist` (launchd template)

For production Linux/systemd deployment steps, use:
- `deploy/linux/README.md`
- `deploy/linux/elrucio.service`
- `deploy/linux/elrucio.env.example`

## Explicit stubs / limitations
- `Video:Provider=openai` supports image analysis directly and video analysis via sampled frames extracted with `ffmpeg`.
- Very long videos are sampled (not fully transcribed frame-by-frame), so output is an informed summary, not exhaustive coverage.
- WhatsApp bridge is not implemented in this selection.
- TTS voice replies are not implemented in this selection (STT only).