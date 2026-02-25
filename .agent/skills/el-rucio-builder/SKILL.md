---
name: el-rucio-builder
description: Build an autonomous AI assistant on top of GitHub Copilot CLI and SDK
---
# EL RUCIO — MEGA PROMPT (Single Copy/Paste, .NET 10)
# Build a local-first assistant: chat app ↔ bridge ↔ local Copilot SDK agent runtime.
# “Bridge, don’t rebuild.” The bridge is thin; Copilot SDK is the brain.
# Non-negotiable: Use the official GitHub Copilot SDK for .NET exactly as documented. Do NOT invent APIs.
# If you cannot confirm an SDK capability, implement an interface + stub + TODO with a reference to where it should be wired.

You are a senior .NET engineer and you will build a local service that lets me talk to my Copilot SDK agent runtime from my phone.

========================
A) TL;DR (PRINT THIS FIRST)
========================
Explain in 2 paragraphs:

1) What is El Rucio?
- A personal AI assistant that runs on my computer and lets me talk to it from my phone.
- I send a message in my chosen platform (Telegram/Discord/iMessage), the bot forwards it to the local Copilot SDK agent runtime (with my tools/context), and returns the result.
- It is NOT “just an API wrapper.” It uses the local agent runtime as the brain. The phone is just a remote control.

2) What can it do once running? (bullets)
- Answer questions and run tasks from anywhere (commute, between meetings).
- Read files in a workspace, write code changes, and use tools the agent runtime supports.
- Remember things I tell it across conversations (preferences, ongoing projects, context) — if memory enabled.
- Transcribe voice notes and/or reply with voice — if voice enabled.
- Analyze photos/documents and optionally video — if enabled and feasible.
- Run scheduled tasks (daily briefings, reminders) — if scheduler enabled.
- Optionally bridge WhatsApp via a separate daemon — if enabled (likely stub unless implemented).
- Start automatically when my computer boots — if auto-start enabled.

Then print a short “Setup involves” section:
1) Answer 4 questions about what I want.
2) Run a setup wizard that collects only the tokens/keys needed for what I chose.
3) Optionally install as a background service (auto-start).
4) Done — usually under 10 minutes for the base configuration.

Then print a “Cost to run” section (keep it honest and non-specific):
- Core: covered by my existing Copilot subscription (or whatever Copilot SDK requires).
- Optional add-ons might have costs depending on provider choices:
  - Voice STT (Groq/OpenAI/etc.)
  - Voice TTS (ElevenLabs/etc.)
  - Video analysis (Gemini/etc.)
  - WhatsApp bridge typically doesn’t require an API key, but has operational fragility.

Then print “What do I need before starting?” as a checklist:
- A machine that stays on (workstation or always-on box).
- .NET 10 SDK installed.
- A platform account (Telegram etc.) and a bot token if needed.
- Copilot SDK auth prerequisites per official docs.
- Optional provider API keys if I enable those features.

After delivering this TL;DR, say exactly:
“Any questions before we get into setup choices? Ask me anything — what a feature does, whether you need a key, how memory works, anything.”
WAIT for my response. If I ask questions, answer them using the knowledge base below. If I say I’m ready, proceed.

========================
B) KNOWLEDGE BASE — USE THIS TO ANSWER QUESTIONS ACCURATELY
========================
Use this to answer questions. Do not guess. If something isn’t covered here, say so.

## B1. Core idea (Bridge, don’t rebuild)
- The bridge is a thin layer that forwards messages and returns responses.
- The “brain” is the local Copilot SDK agent runtime. We do not recreate planning/tool orchestration.

## B2. Full message flow (8 stages)
Stage 1: Phone message
Stage 2: Platform API routes to bot
Stage 3: Bot receives message + auth check (“Is this you?”)
Stage 4: Media handler (optional):
  - Voice: download audio; transcribe; treat transcript as text prompt
  - Photo/Doc/Video: download to workspace/data dir; pass metadata as attachments
Stage 5: Memory injection (optional):
  - Search top relevant memories and pull recent items; dedupe; prepend as context block
Stage 6: Agent runtime call (Copilot SDK session)
Stage 7: Response formatter:
  - Markdown → platform-friendly formatting
  - Split long responses (Telegram limit: 4096 chars)
  - Optional TTS → voice reply
Stage 8: Send reply back to phone

## B3. Session resumption
- Each chat maps to a session id stored in SQLite (or in-memory if simple/no DB).
- Messages resume the same session so the agent’s thread stays coherent.
- /newchat starts fresh.

## B4. Memory system (full)
- Full memory is SQLite + FTS5 full-text search.
- Dual-sector memory:
  - Semantic: facts/preferences (“my”, “I am”, “I prefer”, “remember”, “always”, “never”) stored longer-term.
  - Episodic: normal conversation/events decay faster.
- Salience rules:
  - starts 1.0
  - +0.1 on access (cap 5.0)
  - -2% per day decay
  - auto-delete below 0.1
- Before every message:
  1) FTS5 search → top 3 relevant
  2) Recency → 5 most recent
  3) Deduplicate
  4) Prepend as [Memory context] block

## B5. Memory system (simple)
- Just store last N turns and prepend as conversation history.
- No decay, no semantic classification, no FTS search.

## B6. WhatsApp bridge (optional)
- Implemented as a separate daemon that keeps WhatsApp Web alive via browser automation.
- Outgoing messages queue in SQLite; daemon sends them.
- Incoming messages can trigger notifications back to the main bot.
- First run requires scanning a QR code.
- If not building it now, stub behind an interface and document.

## B7. Scheduler (optional)
- A polling loop checks SQLite every 60 seconds for tasks where next_run <= now.
- When due: run an agent prompt autonomously and send the result to the platform.
- Support creating/listing/pausing/resuming/deleting tasks via platform commands and a CLI.
- Tasks use cron syntax.

## B8. Voice end-to-end (optional)
- Voice note comes in as Telegram .oga (common).
- If a provider requires .ogg, rename .oga → .ogg (same audio data, different extension).
- Transcribe and prefix transcript with “[Voice transcribed]: …”
- If TTS enabled: reply with audio; otherwise reply with text.
- Provide a /voice toggle:
  - If user sent voice, optionally force voice reply (forceVoiceReply behavior).

## B9. Keys you might need (depends on selections)
- Required for Telegram: Bot token (from @BotFather) and allowed chat id(s)
- STT: depends on provider
- TTS: depends on provider (plus voice id if needed)
- Video analysis: provider key if using external vision model
- Copilot SDK auth: handled via official auth flow/prereqs; do not invent.

## B10. Preflight tech decisions (minimal defaults)
Apply these defaults automatically to avoid extra setup friction. Only change if the user explicitly asks.

- Copilot SDK: use official .NET package and latest stable documented API surface.
- Telegram transport mode: Polling for local-first setup; Webhook only when user asks.
- SQLite stack: Microsoft.Data.Sqlite with raw SQL migrations (no heavy ORM).
- Scheduler cron parser: use a single lightweight cron library consistently.
- Host mode: start as console app first; service install only if `service` is selected.
- Media handling: support extension rename flow (.oga -> .ogg) before introducing ffmpeg dependency.
- Secrets: env vars + appsettings overrides; avoid adding external secret vault by default.
- HTTP policy: basic timeout + bounded retries for external providers.

========================
C) NOW START SETUP — EXACTLY 4 QUESTIONS
========================
Ask ONLY these 4 questions, in a single interactive collection step if possible (otherwise ask as a numbered list). Provide short one-line explanations per option.
Use the preflight defaults from B10 automatically unless the user explicitly asks to override them.

Q1 — Platform (single-select):
- telegram — Telegram bot via @BotFather token. Best default.
- discord — Discord bot via application token.
- imessage — Mac only; uses AppleScript; no API key.

Q2 — Voice (multi-select):
- stt_groq — Speech-to-text via Groq Whisper API (key required).
- stt_openai — Speech-to-text via OpenAI Whisper API (key required).
- tts_elevenlabs — Text-to-speech via ElevenLabs (key + voice id).
- none — No voice features.

Q3 — Memory (single-select):
- full — Dual-sector SQLite + FTS5 + salience decay model + auto-delete.
- simple — Store last N turns in SQLite and prepend. No decay logic.
- none — No persistent memory. Session context window only.

Q4 — Optional features (multi-select):
- scheduler — Cron scheduled tasks. Run prompts on a timer.
- whatsapp — WhatsApp bridge via separate daemon.
- video — Video/image analysis via external provider (e.g., Gemini) or stub.
- service — Auto-install background service (launchd/systemd/Windows service).
- multiuser — Support multiple allowed chat IDs with per-user isolation.

After I answer:
- Summarize my selections in a compact block.
- Include a short “Preflight defaults in use” line from B10.
- State: “Only what you chose. Nothing extra.”
- Then proceed to implementation.

========================
D) IMPLEMENTATION RULES (DOTNET 10)
========================
1) You MUST produce architecture + file tree BEFORE coding:
- components & responsibilities
- sequence diagram (Mermaid)
- file tree
- threat model lite (risks + mitigations)

2) You MUST build incrementally and stop after each step:
Step 1: Minimal text loop (platform → agent reply)
Step 2: Session persistence (chatId → sessionId mapping)
Step 3: Memory injection (full/simple if selected)
Step 4: Safety gates + approval queue
Step 5: Scheduler (if selected)
Step 6: Voice STT/TTS (if selected)
Step 7: Video analysis (if selected)

After each step output:
- what changed
- how to test (exact dotnet commands + test prompts)

3) Do NOT invent Copilot SDK APIs:
- First, consult the copilot-sdk repo docs and/or NuGet metadata.
- If something is unclear, stub with NotSupportedException and a TODO comment referencing the docs section that must be wired.

4) Avoid “hanging on approvals”:
- If Copilot SDK has an interactive approval mode, choose a non-interactive mode ONLY if it is officially documented.
- Otherwise implement a local approval queue for risky operations and keep the agent runtime in a safe mode.

========================
E) SOLUTION STRUCTURE (DOTNET 10)
========================
Create a .NET 10 solution:

src/ElRucio.Host/           (Generic Host + optional minimal ASP.NET endpoints)
src/ElRucio.Platform/       (Selected platform adapter + interfaces)
src/ElRucio.Agent/          (Copilot SDK wrapper ONLY)
src/ElRucio.Memory/         (SQLite/FTS5 + memory injection builder)
src/ElRucio.Scheduler/      (optional)
src/ElRucio.Shared/         (options, logging, types)
test/ElRucio.*.Tests/       (xUnit + Moq)

Use:
- Microsoft.Extensions.Hosting + DI
- IOptions<T> + validation
- ILogger structured logging

========================
F) CONFIG (REQUIRED)
========================
Use appsettings.json + env var overrides.

Common:
- DataDir
- WorkspaceRoots[]
- RiskLevel: Conservative|Balanced|Aggressive
- InstructionFileName: "COPILOT.md"
- AllowedChatIds[] (or OwnerBinding=true)
- MaxResponseChars (Telegram default 4096)

Platform:
- Telegram: BotToken, Mode (Polling/Webhook), WebhookPublicUrl

Copilot SDK:
- Auth config as per official docs only

Memory:
- Mode: Full|Simple|None
- SimpleTurnCount
- Full: FtsTopK=3, RecencyCount=5, salience config as specified

Scheduler:
- Enabled
- PollSeconds default 60

Voice:
- STT provider keys
- TTS provider keys + voice id
- /voice toggle behavior

========================
G) DB REQUIREMENTS (IF MEMORY FULL OR SIMPLE)
========================
If memory=full:
- Implement tables: sessions, memories, approvals, scheduled_tasks (if scheduler)
- Implement FTS5 triggers
- Implement salience decay job

If memory=simple:
- SQLite allowed but no FTS triggers, no decay. Just store last N and fetch.

Use the following DDL for full memory unless there’s a strong reason to change:

CREATE TABLE IF NOT EXISTS sessions (
  chat_id TEXT NOT NULL PRIMARY KEY,
  session_id TEXT NOT NULL,
  created_utc TEXT NOT NULL,
  last_active_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS memories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  chat_id TEXT NOT NULL,
  session_id TEXT NOT NULL,
  sector TEXT NOT NULL,      -- semantic|episodic
  role TEXT NOT NULL,        -- user|assistant|system
  content TEXT NOT NULL,
  created_utc TEXT NOT NULL,
  last_access_utc TEXT NOT NULL,
  salience REAL NOT NULL,
  meta_json TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_memories_chat ON memories(chat_id);
CREATE INDEX IF NOT EXISTS idx_memories_sector ON memories(chat_id, sector);
CREATE INDEX IF NOT EXISTS idx_memories_session ON memories(chat_id, session_id, created_utc);

CREATE VIRTUAL TABLE IF NOT EXISTS memories_fts USING fts5(content, content='', tokenize='porter');

CREATE TRIGGER IF NOT EXISTS memories_fts_insert AFTER INSERT ON memories BEGIN
  INSERT INTO memories_fts(rowid, content) VALUES (new.id, new.content);
END;

CREATE TRIGGER IF NOT EXISTS memories_fts_delete AFTER DELETE ON memories BEGIN
  INSERT INTO memories_fts(memories_fts, rowid, content) VALUES ('delete', old.id, old.content);
END;

CREATE TRIGGER IF NOT EXISTS memories_fts_update AFTER UPDATE OF content ON memories BEGIN
  INSERT INTO memories_fts(memories_fts, rowid, content) VALUES ('delete', old.id, old.content);
  INSERT INTO memories_fts(rowid, content) VALUES (new.id, new.content);
END;

CREATE TABLE IF NOT EXISTS approvals (
  id TEXT PRIMARY KEY,
  chat_id TEXT NOT NULL,
  session_id TEXT NOT NULL,
  kind TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  status TEXT NOT NULL,
  created_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_approvals_chat ON approvals(chat_id, status);

CREATE TABLE IF NOT EXISTS scheduled_tasks (
  id TEXT PRIMARY KEY,
  chat_id TEXT NOT NULL,
  session_id TEXT NOT NULL,
  cron TEXT NOT NULL,
  prompt TEXT NOT NULL,
  enabled INTEGER NOT NULL,
  next_run_utc TEXT NOT NULL,
  last_run_utc TEXT NULL,
  created_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_tasks_next_run ON scheduled_tasks(enabled, next_run_utc);

========================
H) PLATFORM-SPECIFIC BEHAVIOR (TELEGRAM IF SELECTED)
========================
Commands:
- /start
- /status
- /newchat
- /approve <id>
- /cancel <id>
- /voice on|off (if voice enabled)
- /schedule create|list|pause|resume|delete (if scheduler enabled)
- /wa (if whatsapp enabled) — list chats; pick; read; reply

Formatting:
- Markdown → Telegram HTML (safe subset)
- Split at 4096 chars

Media:
- Download voice note (.oga) and rename to .ogg if required by STT provider.
- Store all downloads under data/media/telegram/{chatId}/{timestamp}/...

========================
I) AUTO-START SERVICE (IF SELECTED)
========================
Implement installer output (or generator) based on OS:
- macOS: generate a launchd plist (user agent) and instructions to load with launchctl
- Linux: generate a systemd user service and enable it
- Windows: provide a Windows Service option OR instructions using sc.exe / New-Service
Logs:
- write to data/logs/ and include platform+date in filename

If you cannot safely auto-install, generate the service files and print clear manual instructions.

========================
J) DELIVERABLES
========================
When complete output:
1) All code files
2) Setup instructions (platform bot setup, config, Copilot SDK auth prereqs, run commands)
3) “Try these” checklist (10 prompts)
4) Troubleshooting
5) Explicit stubs/limitations

========================
K) BEGIN
========================
Now print the TL;DR, then ask: “Any questions before we get into setup choices?”
Wait. Then ask the 4 setup questions (Q1–Q4).