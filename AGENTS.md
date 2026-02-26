# Agent Instructions for El Rucio

> Canonical file: this `AGENTS.md` is the source of truth for agent instructions in this repository.
> Keep `.github/copilot-instructions.md` synchronized with this file.

## Code style and patterns
- Target framework is `.NET 10` (`net10.0`) with nullable reference types and implicit usings enabled.
- Prefer `sealed` classes, constructor injection (including primary constructors), and `async` APIs with `CancellationToken`.
- Use module-level DI registration extension methods (`AddElRucioMemory`, `AddElRucioPlatform`, etc.) and compose only in host startup.
- Use strongly typed options from `ElRucio.Shared.Options`, bind from configuration sections, and validate critical options on startup.
- Use structured logging via `ILogger<T>` with template parameters; never log secrets or full token-bearing URLs.

## Architecture boundaries
- Keep composition and hosting concerns in `src/ElRucio.Host`.
- Keep Copilot session/runtime logic inside `src/ElRucio.Agent`.
- Keep transport adapters and media features in `src/ElRucio.Platform`.
- Keep persistence and schema logic in `src/ElRucio.Memory`.
- Keep cron execution loop behavior in `src/ElRucio.Scheduler`.
- Avoid cross-layer leakage: platform should call shared abstractions, not raw persistence internals.

## Build and test workflow
- Primary commands:
  - `dotnet restore ElRucio.slnx`
  - `dotnet build ElRucio.slnx`
  - `dotnet test ElRucio.slnx`
  - `dotnet run --project src/ElRucio.Host/ElRucio.Host.csproj`
- Tests use `xUnit` and `Moq`; prefer behavior-focused tests and real temporary SQLite for integration scenarios.
- For scheduler or command changes, add/adjust tests near existing scheduling integration tests before broad refactors.

## Project-specific conventions
- Telegram command UX (`/status`, `/diag`, `/schedule`, `/approve`, `/cancel`) is first-class; preserve current behavior.
- Handle Telegram command mentions (for example `/diag@botname`) when parsing commands.
- Memory model is app-specific (semantic/episodic + salience), so avoid replacing with generic chat-history logic.
- Keep feature toggles (`Voice`, `Video`, `Scheduler`) controlled by platform options and existing fallback stubs.
- Maintain local-first operation: SQLite persistence and local orchestration are default assumptions.

## Integration guidance
- Keep `GitHub.Copilot.SDK` usage isolated to agent runtime wrappers.
- Use `TelegramApiClient` for Bot API operations (polling, send, file retrieval) rather than duplicating HTTP logic.
- Reuse existing OpenAI STT and vision/video abstractions; avoid ad hoc media pipelines outside platform services.
- Scheduler behavior should continue using `Cronos` and persisted task metadata.

## Security and operations
- Never commit secrets; use environment variables, user secrets, or Linux env files (`/etc/elrucio/elrucio.env`).
- Respect `.gitignore` for `.env`, secret files, `data/`, and SQLite artifacts.
- Preserve allowlist gating (`AllowedChatIds`) and risky-action approval flow.
- For Linux operational changes, align with scripts in `deploy/linux` (`install`, `update`, `healthcheck`, `backup`, `restore`, `doctor`, `preflight`).
- Keep backups and generated secret files permission-restricted.
