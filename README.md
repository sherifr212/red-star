# RedStar

RedStar is a .NET CLI for chatting with a locally or self-hosted LLM server — such as
[Unsloth Studio](https://unsloth.ai/) — over its OpenAI-compatible `/v1` API.

It's a thin client: `RedStar.Cli` (the `redstar` executable) drives `RedStar.Base` (the client
library), which wraps the official [`OpenAI` .NET SDK](https://github.com/openai/openai-dotnet) via
the [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)'s `AIAgent`
abstraction (`Microsoft.Agents.AI`), rather than talking HTTP directly for chat. `AIAgent` itself
wraps `Microsoft.Extensions.AI`'s `IChatClient` — RedStar builds the `IChatClient` exactly as before
and wraps it one layer higher instead of consuming it directly.

## Features

- **Interactive and one-shot chat** — start a REPL session, or pass `--prompt` for a single
  exchange and exit.
- **Rich streaming console UI** — reasoning, tool status, web search hits, and the answer itself
  each render as their own live-updating boxed panel as they stream in.
- **Automatic model resolution** — before every run, RedStar checks which model(s) are actually
  loaded on the server and fails fast with a clear error instead of silently sending requests to a
  model that isn't there.
- **Layered configuration** — `appsettings.json` → `appsettings.local.json` → environment
  variables → CLI flags, each layer overriding the last.
- **No-auth friendly** — works against a server with no API key configured.
- **Unsloth extensions** — server-side web search tool support via Unsloth's extended
  chat-completions fields.
- **OpenTelemetry built in** — traces, metrics, and structured logs export to any OTLP collector
  (e.g. the [Aspire Dashboard](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone)),
  correlated per run.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An OpenAI-compatible LLM server reachable over HTTP (e.g. Unsloth Studio running locally)

## Getting started

The solution file lives at `src/RedStar.slnx` — run every `dotnet` command from `src/`.

```bash
cd src
dotnet build RedStar.slnx
```

Configure how RedStar reaches your server. The quickest way is `src/RedStar.Cli/appsettings.local.json`
(git-tracked as a template for local overrides — fill in your own values, don't commit real secrets):

```json
{
  "RedStar": {
    "BaseUrl": "http://127.0.0.1:8888/v1",
    "ApiKey": "",
    "DefaultModel": ""
  }
}
```

Or override any of these per-invocation with CLI flags, or via `RedStar__*` environment variables.

## Usage

```bash
# Interactive session
dotnet run --project RedStar.Cli

# One-shot prompt
dotnet run --project RedStar.Cli -- chat -p "hello"

# With an explicit endpoint, key, model, and system prompt
dotnet run --project RedStar.Cli -- chat \
  --endpoint "http://127.0.0.1:8888/v1" \
  --api-key "ab-..." \
  -m "unsloth/gemma-4-E4B-it-GGUF" \
  -p "hello" \
  -s "be terse"

# List models available on the server
dotnet run --project RedStar.Cli -- models
```

The root command and the `chat` subcommand behave identically — `redstar -p "hi"` is the same as
`redstar chat -p "hi"`. Omit `--prompt`/`-p` for an interactive session instead of a one-shot
exchange.

| Flag | Short | Applies to | Description |
|---|---|---|---|
| `--endpoint` | | `chat`, `models` | Base URL of the OpenAI-compatible API. |
| `--api-key` | | `chat`, `models` | Bearer API key for the server. |
| `--model` | `-m` | `chat` | Model id to use for this call. |
| `--prompt` | `-p` | `chat` | Send a single prompt and print the response, then exit. |
| `--system` | `-s` | `chat` | Optional system prompt to prime the conversation. |
| `--run-id` | | `chat`, `models` | Correlation ID tagged onto this run's telemetry trace. |

Any flag left out falls back through the configuration layers described below.

## Configuration

Options bind from the `RedStar` section and are layered, each overriding the last:

1. `appsettings.json` — checked-in template listing every key with its default.
2. `appsettings.local.json` — local overrides (e.g. your real API key/model). Git-tracked, so
   double-check its contents before committing/pushing if you've put a real key in it.
3. Environment variables, prefixed `RedStar__` (e.g. `RedStar__ApiKey`, `RedStar__DefaultModel`).
4. CLI flags (`--endpoint`, `--api-key`, `--model`) — take precedence over everything else.

| Key | Default | Description |
|---|---|---|
| `RedStar:BaseUrl` | `http://127.0.0.1:8888/v1` | Base URL of the OpenAI-compatible API. |
| `RedStar:ApiKey` | *(empty)* | Bearer API key. Empty means "talk to a server with no auth." |
| `RedStar:DefaultModel` | *(empty)* | Model to use when none is specified. If left empty, the currently loaded model is auto-detected (fails if zero or more than one model is loaded). |
| `RedStar:WebSearchEnabled` | `false` | Enables Unsloth's server-side web search tool. Config/env-only, no CLI flag. |
| `RedStar:Otel:Enabled` | `true` | Enables OpenTelemetry export. |
| `RedStar:Otel:Endpoint` | `http://localhost:4317` | OTLP gRPC endpoint traces/metrics/logs export to. |

## Testing

```bash
cd src
dotnet test RedStar.slnx

# Run a single test class or test
dotnet test RedStar.slnx --filter "FullyQualifiedName~ChatSessionTests"
dotnet test RedStar.slnx --filter "FullyQualifiedName~SendAsync_MergesInstructions_IntoChatOptions"
```

Tests use xUnit (`RedStar.UnitTest`) and fake the underlying model/HTTP clients rather than hitting
a real server. The interactive REPL loop isn't covered, since it isn't cheaply testable without
redirecting stdin — the one-shot chat path and model-resolution logic are.

## Project layout

```
src/
├── RedStar.Base/      # Client library: chat session, model resolution, telemetry, config
├── RedStar.Cli/        # `redstar` executable: Spectre.Console.Cli commands, console rendering
└── RedStar.UnitTest/   # xUnit tests for RedStar.Base and RedStar.Cli
```

- **`RedStar.Base`** wraps the OpenAI SDK's chat completions behind `Microsoft.Agents.AI`'s
  `AIAgent`, resolves which model to use, and forwards traces/metrics/logs to an OTLP collector.
- **`RedStar.Cli`** is thin Spectre.Console.Cli glue over `RedStar.Base`, plus the multi-box
  streaming console UI that renders reasoning, tool status, and answer text as they arrive.

See [`CLAUDE.md`](CLAUDE.md) for a deeper architectural walkthrough.

## Contributing

Issues and pull requests are welcome. Please make sure `dotnet build RedStar.slnx` and
`dotnet test RedStar.slnx` pass before submitting a change.
