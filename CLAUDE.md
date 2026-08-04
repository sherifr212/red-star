# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

RedStar is a .NET CLI for chatting with a locally/self-hosted LLM server (Unsloth Studio) over its
OpenAI-compatible `/v1` API. It's a thin client: `RedStar.Cli` (the `redstar` executable) drives
`RedStar.Base` (the client library), which wraps the official `OpenAI` .NET SDK via
`Microsoft.Extensions.AI`'s `IChatClient` abstraction rather than talking HTTP directly for chat.

## Commands

The solution file lives at `src/RedStar.slnx` (not the repo root) — run dotnet commands from `src/`.

```
dotnet build RedStar.slnx
dotnet test RedStar.slnx
dotnet test RedStar.slnx --filter "FullyQualifiedName~ChatSessionTests"   # single test class
dotnet test RedStar.slnx --filter "FullyQualifiedName~SendAsync_PassesFullHistory_ToTheChatClient"  # single test
dotnet run --project RedStar.Cli -- chat -p "hello"                      # one-shot prompt
dotnet run --project RedStar.Cli -- models                               # list models on the server
```

Test project is xUnit (`RedStar.UnitTest`), referencing `RedStar.Base` only — it has no dependency on
the CLI project.

`chat` (and the root command, which behaves identically) accepts every shared flag; `models` only
takes `--endpoint`/`--api-key`. Long form and short-alias form, all flags spelled out:

```
dotnet run --project RedStar.Cli -- chat --endpoint "http://127.0.0.1:8888/v1" --api-key "ab-..." --model "unsloth/gemma-4-E4B-it-GGUF" --prompt "hello" --system "be terse"

dotnet run --project RedStar.Cli -- chat --endpoint "http://127.0.0.1:8888/v1" --api-key "ab-..." -m "unsloth/gemma-4-E4B-it-GGUF" -p "hello" -s "be terse"
```

`-m`/`-p`/`-s` are the only flags with short aliases (`--endpoint`/`--api-key` are long-only). Omit
`--prompt` for an interactive session instead of a one-shot exchange. Any flag left out falls back
through the config layering above (env var, then `appsettings.local.json`, then `appsettings.json`'s
default).

## Architecture

**Project layout** (`src/`): `RedStar.Base` (client library) ← `RedStar.Cli` (System.CommandLine
entry point) and `RedStar.UnitTest` both depend on it.

**Config resolution** (`RedStarOptions`, bound from the `RedStar` section): layered as
`appsettings.json` → `appsettings.local.json` → environment variables (`RedStar__*`) → CLI flags
(`--endpoint`, `--api-key`, `--model`), each layer overriding the last. `appsettings.json` is the
checked-in template listing every key with its default; `appsettings.local.json` holds the real
API key/model for local dev. Only `appsettings.Production.json` is git-ignored — `appsettings.json`
and `appsettings.local.json` are both tracked, so double-check `appsettings.local.json`'s contents
before any commit/push that touches it; it currently holds a live Unsloth API key.
`RedStarOptions.ApplyOverrides` implements the CLI-flags-win-if-non-blank step; `WebSearchEnabled`
has no CLI flag (config/env-only), but `ApplyOverrides` must still carry its value through into the
new instance it returns — it was dropped (silently reset to `false`) for a while because the method
only copied the three overridable fields, and every CLI run rebuilds `RedStarOptions` via
`ApplyOverrides`. See the `ApplyOverrides_PreservesWebSearchEnabled_WhichHasNoCliOverride` test.

**No-auth mode**: `RedStarOptions.ApiKey` empty means "talk to a server with no auth" — but the
OpenAI SDK requires a non-empty credential object, so `RedStarChatClientFactory.Create` always
passes a placeholder credential and relies on `ConditionalAuthHandler` (a `DelegatingHandler`) to
strip the `Authorization` header from outgoing requests when no real key is configured. Don't try
to "fix" this by passing a null/empty credential to the SDK — it throws.

**Model resolution** (`ModelSelector.SelectDefault`): when no model is specified on the command
line, resolution order is (1) `RedStarOptions.DefaultModel` if set — trusted even if the server
doesn't currently list it as available, since Unsloth Studio can load models on demand — (2) the
server's currently-loaded model, (3) the first model the server reports, (4) `null` if the server
has none. This is why `ChatCommandHandler.ResolveDefaultModelAsync` hits `/v1/models` before
building the chat client whenever `DefaultModel` is blank.

**Unsloth-specific request fields via `Patch`**: Unsloth's API extends OpenAI's chat-completions
schema with fields the OpenAI SDK doesn't model (`enable_thinking`, `enable_tools`,
`enabled_tools`, `session_id`). These get attached through `OpenAI.Chat.ChatCompletionOptions`'s
experimental `Patch` property (`System.ClientModel.Primitives.JsonPatch`, gated behind diagnostic
`SCME0001` — suppress locally with a scoped `#pragma`, don't blanket-suppress project-wide), then
handed to `Microsoft.Extensions.AI` via `ChatOptions.RawRepresentationFactory`. See
`RedStarChatClientFactory.CreateChatOptions` for the current example (toggles Unsloth's server-side
`web_search` tool from `RedStarOptions.WebSearchEnabled`). Extending to other Unsloth-specific
fields follows the same pattern: build/extend the `ChatCompletionOptions` inside
`CreateChatOptions`, not inside `ChatSession`, which stays transport-agnostic.

**Two HTTP paths, not one**: `ModelsClient` (`GET /v1/models`) is a plain hand-rolled
`HttpClient`/`System.Text.Json` client — it does not go through the OpenAI SDK or `IChatClient`,
because model listing isn't part of that abstraction. Chat completions go through
`RedStarChatClientFactory` → OpenAI SDK → `IChatClient`. Keep that split in mind when adding
features: "does this belong on the models endpoint or the chat endpoint" determines which client
it touches.

**`ChatSession`** just owns message history and streams one turn through whatever `IChatClient` /
`ChatOptions` it's given — it has no knowledge of Unsloth, HTTP, or config. Tests fake the
dependency with `FakeChatClient` (`RedStar.UnitTest/Fakes/`), which records `LastMessages` /
`LastOptions` for assertions rather than making real calls.
