# RedStar

RedStar is a .NET CLI for chatting with a locally or self-hosted LLM server — [Unsloth
Studio](https://unsloth.ai/) or [LM Studio](https://lmstudio.ai/) — over its OpenAI-compatible `/v1`
API. Pick which one a run talks to with `--agent Unsloth` (the default) or `--agent LMStudio`.

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
- **No-auth friendly** — works against a server with no API key configured (LM Studio's default).
- **Two agent backends** — Unsloth Studio and LM Studio, selected with `--agent`; each keeps its own
  endpoint/API key/default model, so switching one never disturbs the other's settings.
- **Unsloth extensions** — server-side web search tool support via Unsloth's extended
  chat-completions fields.
- **LM Studio just-in-time loading** — a configured default model that isn't currently loaded doesn't
  have to be a hard failure like it is for Unsloth; LM Studio can load it on the first request, and
  RedStar's model resolution knows to trust that instead of erroring. `redstar models --agent
  LMStudio` also surfaces LM Studio's richer per-model info (type, context length, quantization) that
  Unsloth's API doesn't report.
- **OpenTelemetry built in** — traces, metrics, and structured logs export to any OTLP collector
  (e.g. the [Aspire Dashboard](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone)),
  correlated per run.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An OpenAI-compatible LLM server reachable over HTTP (e.g. Unsloth Studio or LM Studio running
  locally, with its API server enabled)
- Node.js (current Active LTS) — only needed for `RedStar.WebApp`; see
  [`RedStar.WebApp/GETTING_STARTED.md`](src/RedStar.WebApp/GETTING_STARTED.md) for setup

## Getting started

The solution file lives at `src/RedStar.slnx` — run every `dotnet` command from `src/`.

```bash
cd src
dotnet build RedStar.slnx
```

Configure how RedStar reaches your server. The quickest way is `src/RedStar.Cli/appsettings.local.json`
(git-tracked as a template for local overrides — fill in your own values, don't commit real secrets).
Each agent has its own section; `RedStar:Agent` picks which one a run without `--agent` uses:

```json
{
  "RedStar": {
    "Agent": "Unsloth",
    "Agents": {
      "Unsloth": {
        "BaseUrl": "http://127.0.0.1:8888/v1",
        "ApiKey": "",
        "DefaultModel": ""
      },
      "LMStudio": {
        "BaseUrl": "http://127.0.0.1:1234/v1",
        "ApiKey": "",
        "DefaultModel": ""
      }
    }
  }
}
```

LM Studio's server has no auth by default, so `ApiKey` can usually stay empty — only fill it in if
you've enabled an API token under LM Studio's Server Settings.

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

# Talk to LM Studio instead of Unsloth
dotnet run --project RedStar.Cli -- chat --agent LMStudio -p "hello"
dotnet run --project RedStar.Cli -- models --agent LMStudio
```

The root command and the `chat` subcommand behave identically — `redstar -p "hi"` is the same as
`redstar chat -p "hi"`. Omit `--prompt`/`-p` for an interactive session instead of a one-shot
exchange.

| Flag | Short | Applies to | Description |
|---|---|---|---|
| `--agent` | | `chat`, `models` | Which agent backend to talk to: `Unsloth` (default) or `LMStudio`. |
| `--endpoint` | | `chat`, `models` | Base URL of the active agent's OpenAI-compatible API. |
| `--api-key` | | `chat`, `models` | Bearer API key for the active agent's server. |
| `--model` | `-m` | `chat` | Model id to use for this call. |
| `--prompt` | `-p` | `chat` | Send a single prompt and print the response, then exit. |
| `--system` | `-s` | `chat` | Optional system prompt to prime the conversation. |
| `--run-id` | | `chat`, `models` | Correlation ID tagged onto this run's telemetry trace. |

`--endpoint`/`--api-key`/`--model` always apply to whichever agent `--agent` resolves to (the flag if
passed, else `RedStar:Agent` config/env, else `Unsloth`) — they never touch the other agent's
settings.

Any flag left out falls back through the configuration layers described below.

## Configuration

Options bind from the `RedStar` section and are layered, each overriding the last:

1. `appsettings.json` — checked-in template listing every key with its default.
2. `appsettings.local.json` — local overrides (e.g. your real API key/model). Git-tracked, so
   double-check its contents before committing/pushing if you've put a real key in it.
3. Environment variables, prefixed `RedStar__` (e.g. `RedStar__Agents__Unsloth__ApiKey`,
   `RedStar__Agents__LMStudio__DefaultModel`).
4. CLI flags (`--agent`, `--endpoint`, `--api-key`, `--model`) — take precedence over everything else.

RedStar treats `RedStar.Base` as a home for multiple agents, so agent-specific settings live under
their own agent's section rather than flat at the top level — `RedStar:Agents:Unsloth:*` and
`RedStar:Agents:LMStudio:*` today, with room for a sibling `RedStar:Agents:<AgentName>` section per
future agent. `RedStar:Agent` picks which one is active. Settings that are genuinely agent-agnostic,
like telemetry, stay top-level.

| Key | Default | Description |
|---|---|---|
| `RedStar:Agent` | `Unsloth` | Which agent backend to use: `Unsloth` or `LMStudio`. |
| `RedStar:Agents:Unsloth:BaseUrl` | `http://127.0.0.1:8888/v1` | Base URL of Unsloth's OpenAI-compatible API. |
| `RedStar:Agents:Unsloth:ApiKey` | *(empty)* | Bearer API key. Empty means "talk to a server with no auth." |
| `RedStar:Agents:Unsloth:DefaultModel` | *(empty)* | Model to use when none is specified. If left empty, the currently loaded model is auto-detected (fails if zero or more than one model is loaded). |
| `RedStar:Agents:Unsloth:WebSearchEnabled` | `false` | Enables Unsloth's server-side web search tool. Config/env-only, no CLI flag. |
| `RedStar:Agents:LMStudio:BaseUrl` | `http://127.0.0.1:1234/v1` | Base URL of LM Studio's OpenAI-compatible API. |
| `RedStar:Agents:LMStudio:ApiKey` | *(empty)* | Bearer API key. LM Studio has no auth by default, so this is usually left empty. |
| `RedStar:Agents:LMStudio:DefaultModel` | *(empty)* | Model to use when none is specified. Unlike Unsloth, this doesn't have to already be loaded — LM Studio can load it just-in-time on the first request. Auto-detects the same way as Unsloth if left empty. |
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

Tests use xUnit, split across two projects: `RedStar.UnitTest` (tests `RedStar.Base` only) and
`RedStar.UnitTest.Cli` (tests `RedStar.Cli`). Both fake the underlying model/HTTP clients rather than
hitting a real server. The interactive REPL loop isn't covered, since it isn't cheaply testable
without redirecting stdin — the one-shot chat path and model-resolution logic are.

## Project layout

```
src/
├── RedStar.Base/      # Client library: agents, chat session, model resolution, telemetry, config
│   └── Agents/
│       ├── Unsloth/    # Unsloth-specific agent construction (UnslothAgentFactory)
│       └── LMStudio/   # LM Studio-specific agent construction (LMStudioAgentFactory)
├── RedStar.Cli/        # `redstar` executable: Spectre.Console.Cli commands, console rendering
├── RedStar.UnitTest/   # xUnit tests for RedStar.Base
├── RedStar.UnitTest.Cli/ # xUnit tests for RedStar.Cli
└── RedStar.WebApp/     # ASP.NET Core MVC web frontend: Lit + Vite + Tailwind, references RedStar.Base
```

- **`RedStar.Base`** is a host for multiple agents — Unsloth and LM Studio today. Agent-specific
  code lives under `RedStar.Base/Agents/<AgentName>/` (e.g. `Agents/Unsloth/UnslothAgentFactory.cs`,
  `Agents/LMStudio/LMStudioAgentFactory.cs`, each wrapping the OpenAI SDK's chat completions behind
  `Microsoft.Agents.AI`'s `AIAgent`); everything agent-agnostic — `ChatSession`, model resolution,
  config, telemetry — stays in the top-level `RedStar.Base` namespace. `RedStarOptions.Agent`
  selects between them; `ChatCommandHandler`/`ModelsCommandHandler` pick the matching
  factory/response-extractor/models-client with an explicit switch, not a plugin registry.
- **`RedStar.Cli`** is thin Spectre.Console.Cli glue over `RedStar.Base`, plus the multi-box
  streaming console UI that renders reasoning, tool status, and answer text as they arrive.
- **`RedStar.WebApp`** hosts multiple pages, each client-rendered by TypeScript (Lit web components)
  with a plain static navbar — no SPA router. See
  [`RedStar.WebApp/CLAUDE.md`](src/RedStar.WebApp/CLAUDE.md) for its architecture and
  [`RedStar.WebApp/GETTING_STARTED.md`](src/RedStar.WebApp/GETTING_STARTED.md) for setup, commands,
  and troubleshooting.

See [`CLAUDE.md`](CLAUDE.md) for a deeper architectural walkthrough.

## Contributing

Issues and pull requests are welcome. Please make sure `dotnet build RedStar.slnx` and
`dotnet test RedStar.slnx` pass before submitting a change.
