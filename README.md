# RedStar

RedStar is a .NET toolkit and CLI for chatting with multiple AI agents—including local self-hosted LLM servers like [Unsloth Studio](https://unsloth.ai/) and [LM Studio](https://lmstudio.ai/), cloud providers like [Google AI](https://aistudio.google.com/), and subprocess-based agents like [Claude Code](https://docs.anthropic.com/en/docs/agents-and-tools/claude-code/overview).

At its core is `RedStar.Base`, a client library that wraps the official [`OpenAI` .NET SDK](https://github.com/openai/openai-dotnet) (and other clients) via the [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)'s `AIAgent` abstraction (`Microsoft.Agents.AI`), providing a unified interface across very different backends.

## Components

The repository is structured into several projects:

- **`RedStar.Cli`**: A rich command-line client (`redstar` executable) offering interactive REPL sessions or one-shot chats. It features a multi-box streaming console UI that renders reasoning, tool status, web search hits, and answers live as they stream in.
- **`RedStar.Base`**: The core client library housing multiple agents (`Unsloth`, `LMStudio`, `ClaudeCode`, `GoogleAI`), chat sessions, model resolution, telemetry, and configuration.
- **`RedStar.Controller`**: An ASP.NET Core gateway API that acts as a proxy to local AI server management endpoints (e.g., LM Studio's native models API).
- **`RedStar.WebApp`**: An ASP.NET Core MVC web frontend built with TypeScript, Lit web components, Vite, and Tailwind CSS. (See [`src/RedStar.WebApp/GETTING_STARTED.md`](src/RedStar.WebApp/GETTING_STARTED.md) for detailed frontend setup instructions).

## Features

- **Four agent backends**:
  - `Unsloth` (default): Talks to a local Unsloth Studio OpenAI-compatible API. Supports Unsloth's custom server-side tools (`python`, `bash`, `web_search`).
  - `LMStudio`: Talks to a local LM Studio OpenAI-compatible API. Supports just-in-time model loading (models don't need to be pre-loaded).
  - `GoogleAI`: Talks to Google's Gemini models via the Gemini API.
  - `ClaudeCode`: Drives the local `claude` subprocess agent via its JSON stream protocol instead of HTTP.
- **Interactive and one-shot chat** (CLI): Start a REPL session, or pass `--prompt` for a single exchange.
- **Rich streaming console UI** (CLI): Reasoning, tool status, and the answer itself render as live-updating boxed panels.
- **Automatic model resolution**: Before every run, RedStar checks which model(s) are actually available or loaded on the server and fails fast with a clear error if ambiguous.
- **Layered configuration**: `appsettings.json` → `appsettings.local.json` → environment variables → CLI flags, each layer overriding the last.
- **OpenTelemetry built in**: Traces, metrics, and structured logs export to any OTLP collector (e.g. the Aspire Dashboard), correlated per run.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- For local agents: An OpenAI-compatible LLM server reachable over HTTP (Unsloth Studio or LM Studio)
- Node.js (current Active LTS) — only needed for `RedStar.WebApp`

## Getting started

The solution file lives at `src/RedStar.slnx` — run every `dotnet` command from `src/`.

```bash
cd src
dotnet build RedStar.slnx
```

Configure how RedStar reaches your agents. The quickest way is to configure `appsettings.local.json` in the respective project (e.g., `src/RedStar.Cli/appsettings.local.json`, which is git-tracked as a template).

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
      },
      "GoogleAI": {
        "ApiKey": "YOUR_GEMINI_API_KEY",
        "DefaultModel": "gemini-1.5-pro"
      },
      "ClaudeCode": {
        "AuthMode": "CliLogin",
        "ProcessMode": "PerTurn"
      }
    }
  }
}
```

Or override any of these per-invocation with CLI flags, or via `RedStar__*` environment variables.

## Usage (CLI)

```bash
# Interactive session (uses default Agent in config)
dotnet run --project RedStar.Cli

# One-shot prompt
dotnet run --project RedStar.Cli -- chat -p "hello"

# List models available on the server
dotnet run --project RedStar.Cli -- models

# Talk to LM Studio instead of Unsloth
dotnet run --project RedStar.Cli -- chat --agent LMStudio -p "hello"
dotnet run --project RedStar.Cli -- models --agent LMStudio

# Talk to Claude Code (subprocess agent)
dotnet run --project RedStar.Cli -- chat --agent ClaudeCode -p "hello"

# Talk to Google AI
dotnet run --project RedStar.Cli -- chat --agent GoogleAI -p "hello"
```

| Flag | Short | Applies to | Description |
|---|---|---|---|
| `--agent` | | `chat`, `models` | Which agent backend to talk to: `Unsloth` (default), `LMStudio`, `ClaudeCode`, or `GoogleAI`. |
| `--endpoint` | | `chat`, `models` | Base URL of the active agent's API. |
| `--api-key` | | `chat`, `models` | Bearer API key for the active agent's API. |
| `--model` | `-m` | `chat` | Model id to use for this call. |
| `--prompt` | `-p` | `chat` | Send a single prompt and print the response, then exit. |
| `--system` | `-s` | `chat` | Optional system prompt to prime the conversation. |
| `--run-id` | | `chat`, `models` | Correlation ID tagged onto this run's telemetry trace. |

Settings like `--endpoint`, `--api-key`, and `--model` always apply to whichever agent `--agent` resolves to.

See CLI-specific options (e.g. Claude Code tool constraints) by running `dotnet run --project RedStar.Cli -- chat --help`.

## Testing

```bash
cd src
dotnet test RedStar.slnx
```

Tests use xUnit, split across unit test projects (e.g. `RedStar.UnitTest`, `RedStar.UnitTest.Cli`, `RedStar.UnitTest.Controller`). They fake the underlying model/HTTP/subprocess clients rather than hitting real servers.

## Further Reading

- [`CLAUDE.md`](CLAUDE.md) — The main architectural walkthrough for the backend projects (`RedStar.Base`, `RedStar.Cli`).
- [`src/RedStar.WebApp/CLAUDE.md`](src/RedStar.WebApp/CLAUDE.md) — Architectural overview of the Vite/Lit frontend pipeline.
- [`src/RedStar.WebApp/GETTING_STARTED.md`](src/RedStar.WebApp/GETTING_STARTED.md) — How to run and work on the WebApp.

## Contributing

Issues and pull requests are welcome. Please make sure `dotnet build RedStar.slnx` and `dotnet test RedStar.slnx` pass before submitting a change.
