# El Rucio

Local-first AI assistant: Telegram bot -> bridge -> local GitHub Copilot SDK runtime.

## What this build includes
- Telegram polling transport.
- Copilot SDK-backed runtime sessions with `chat_id -> session_id` SQLite mapping.
- Full memory mode: SQLite + FTS5 + semantic/episodic sectors + salience decay.
- Safety gate + approval queue (`/approve`, `/cancel`).
- Scheduler with cron polling loop and Telegram controls.
- Voice STT via OpenAI Whisper transcription.
- Video feature selected as explicit stub (interface in place, provider not wired).
- Service install artifacts generator (manual install commands/files).

## Prerequisites
- .NET 10 SDK.
- GitHub Copilot CLI installed and authenticated.
- Telegram bot token from @BotFather.
- OpenAI API key for STT if voice is enabled.

## Configuration
Primary config file: `src/ElRucio.Host/appsettings.json`.

Set these values before first run:
- `Telegram:BotToken`
- `ElRucio:AllowedChatIds` (recommended)
- `Voice:OpenAiApiKey` (if STT enabled)

Environment variable overrides are supported via standard .NET conventions, for example:
- `Telegram__BotToken`
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

## Telegram commands
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
  - confirm `Telegram:BotToken`
  - clear stale updates by restarting service
  - verify chat id is allowed (or leave list empty temporarily)
- Copilot errors:
  - run `copilot auth status`
  - ensure local Copilot CLI is installed and authenticated
- STT not working:
  - verify `Voice:OpenAiApiKey`
  - inspect downloaded voice files under `data/media/telegram/...`
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
- Video analysis provider is intentionally stubbed (`VideoAnalyzerStub`) and throws `NotSupportedException` until provider wiring is added.
- WhatsApp bridge is not implemented in this selection.
- TTS voice replies are not implemented in this selection (STT only).