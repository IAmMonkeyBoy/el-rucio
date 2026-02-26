# Slack Envelope Fixtures

These JSON files drive `SlackSocketEnvelopeFixtureTests` and validate `SlackSocketEnvelopeParser` behavior using realistic Socket Mode payloads.

## File naming
- Use lowercase snake case, for example: `events_app_mention_with_file.json`.
- Keep one scenario per file.

## Fixture shape
Each fixture file uses this structure:

```json
{
  "name": "human-readable scenario name",
  "payload": { /* full Slack Socket Mode envelope */ },
  "expect": {
    "actionType": "InboundMessage|SlashCommand|Disconnect|None",
    "dedupKey": "expected dedup key",
    "channelId": "string or null",
    "userId": "string or null",
    "threadTs": "string or null",
    "text": "string or null",
    "fileCount": 0,
    "slashCommand": "string or null",
    "slashText": "string or null"
  }
}
```

## Adding a new case
1. Copy an existing fixture file in this folder.
2. Set `payload` to the Slack envelope you want to validate.
3. Update all `expect` fields to match parser output.
4. Run `dotnet test ElRucio.slnx`.

## Notes
- Prefer fixtures for parser behavior before adding new code-based tests.
- Keep edge-case tests in `SlackSocketEnvelopeParserTests` only when fixtures are not a good fit (for example malformed JSON exceptions).
