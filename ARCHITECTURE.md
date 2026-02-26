# El Rucio Architecture

## Selection Summary
- Platform: telegram
- Voice: stt_openai
- Memory: full
- Optional: scheduler, video, service
- Preflight defaults in use: polling mode, Microsoft.Data.Sqlite + raw SQL migrations, console-first host, env vars + appsettings, bounded HTTP retry, .oga->.ogg rename path.
- Only what you chose. Nothing extra.

## Components & Responsibilities
- ElRucio.Host
  - Generic Host bootstrapping, DI registration, options validation, service wiring.
  - Runs background workers (Telegram polling, scheduler, salience decay).
- ElRucio.Platform
  - Telegram transport (long polling), command routing, response formatting/splitting.
  - OpenAI STT integration and media download handling.
  - Video analyzer interface + selected stub.
- ElRucio.Agent
  - Thin wrapper over GitHub Copilot SDK (`CopilotClient`, `CreateSessionAsync`, `ResumeSessionAsync`, `SendAsync`, `SessionIdleEvent`).
  - Per-chat runtime session map with persistent `chat_id -> session_id` mapping.
- ElRucio.Memory
  - SQLite bootstrap + migrations.
  - Full-memory implementation (semantic/episodic sectors, FTS5 search, salience updates/decay).
  - Session map persistence, approval queue persistence, scheduled task persistence.
- ElRucio.Scheduler
  - Cron polling loop and due task execution.
- ElRucio.Shared
  - Options, enums, DTOs, interfaces.

## Sequence Diagram
```mermaid
sequenceDiagram
  participant Phone
  participant TelegramAPI
  participant Bot as TelegramPollingService
  participant Memory as SqliteMemoryStore
  participant Agent as CopilotAgentRuntime
  participant SDK as Local Copilot SDK Runtime

  Phone->>TelegramAPI: Send text/voice/media
  TelegramAPI->>Bot: getUpdates()
  Bot->>Bot: auth check (AllowedChatIds)
  alt voice enabled + voice note
    Bot->>TelegramAPI: getFile + file download
    Bot->>Bot: .oga -> .ogg rename (if required)
    Bot->>Bot: OpenAI STT transcription
  end
  Bot->>Memory: BuildMemoryContext(chatId)
  Memory-->>Bot: top FTS + recency block
  Bot->>Agent: SendPrompt(chatId, prompt+memory)
  Agent->>SDK: SendAsync()
  SDK-->>Agent: AssistantMessageEvent / SessionIdleEvent
  Agent-->>Bot: assistant text
  Bot->>Memory: Save turn + salience updates
  Bot->>TelegramAPI: sendMessage() (split <= 4096)
```

## File Tree
```text
src/
  ElRucio.Host/
    Program.cs
    appsettings.json
    Workers/
      ServiceInstallerHostedService.cs
      SalienceDecayHostedService.cs
  ElRucio.Platform/
    Telegram/
      TelegramPollingService.cs
      TelegramApiClient.cs
      TelegramModels.cs
      TelegramHtmlFormatter.cs
    Voice/
      OpenAiSpeechToText.cs
    Video/
      VideoAnalyzerStub.cs
    Orchestration/
      ChatOrchestrator.cs
  ElRucio.Agent/
    CopilotAgentRuntime.cs
  ElRucio.Memory/
    Sqlite/
      SqliteDbInitializer.cs
      SqliteSessionStore.cs
      SqliteMemoryStore.cs
      SqliteApprovalStore.cs
      SqliteScheduledTaskStore.cs
  ElRucio.Scheduler/
    SchedulerWorker.cs
  ElRucio.Shared/
    Options/
    Contracts/
    Models/
test/
  ElRucio.Memory.Tests/
    SqliteMemoryStoreTests.cs
  ElRucio.Platform.Tests/
    TelegramHtmlFormatterTests.cs
```

## Threat Model Lite
- Unauthorized bot control
  - Risk: unknown chat sends commands.
  - Mitigation: `AllowedChatIds` gate before processing.
- Prompt injection via media content
  - Risk: malicious attachment content changes behavior.
  - Mitigation: prepend trusted system guardrails and isolate memory block markup.
- Risky tool execution
  - Risk: destructive operations.
  - Mitigation: conservative risk detector + local approval queue (`/approve`, `/cancel`).
- Secret leakage
  - Risk: tokens written to logs.
  - Mitigation: never log full tokens, options validated, secure env overrides.
- Unbounded retries / API storms
  - Risk: provider instability amplifies traffic.
  - Mitigation: fixed timeout and bounded retries for HTTP provider calls.
