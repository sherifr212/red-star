# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

RedStar is a .NET CLI for chatting with a locally/self-hosted LLM server (Unsloth Studio or LM Studio
— see `RedStarOptions.Agent`) over its OpenAI-compatible `/v1` API. It's a thin client: `RedStar.Cli`
(the `redstar` executable) drives
`RedStar.Base` (the client library), which wraps the official `OpenAI` .NET SDK via the
Microsoft Agent Framework's `AIAgent` abstraction (`Microsoft.Agents.AI`) rather than talking HTTP
directly for chat. `AIAgent` itself wraps `Microsoft.Extensions.AI`'s `IChatClient` — RedStar builds
the `IChatClient` exactly as before and wraps it one layer higher instead of consuming it directly.

See [README.md](README.md) for user-facing docs (features, flags, config keys, project layout). This
file is the deeper architectural walkthrough — the non-obvious invariants and gotchas a change here
needs to respect.

## Commands

The solution file lives at `src/RedStar.slnx` (not the repo root) — run dotnet commands from `src/`.

```
dotnet build RedStar.slnx
dotnet test RedStar.slnx
dotnet test RedStar.slnx --filter "FullyQualifiedName~ChatSessionTests"   # single test class
dotnet test RedStar.slnx --filter "FullyQualifiedName~SendAsync_MergesInstructions_IntoChatOptions"  # single test
dotnet run --project RedStar.Cli -- chat -p "hello"                      # one-shot prompt
dotnet run --project RedStar.Cli -- models                               # list models on the server
dotnet watch run --project RedStar.WebApp                                # web frontend, live-reloading
```

`chat` (and the root command, which behaves identically) accepts every shared flag; `models` only
takes `--agent`/`--endpoint`/`--api-key`. Long form and short-alias form, all flags spelled out:

```
dotnet run --project RedStar.Cli -- chat --endpoint "http://127.0.0.1:8888/v1" --api-key "ab-..." --model "unsloth/gemma-4-E4B-it-GGUF" --prompt "hello" --system "be terse"

dotnet run --project RedStar.Cli -- chat --endpoint "http://127.0.0.1:8888/v1" --api-key "ab-..." -m "unsloth/gemma-4-E4B-it-GGUF" -p "hello" -s "be terse"

dotnet run --project RedStar.Cli -- chat --agent LMStudio -p "hello"
```

`-m`/`-p`/`-s` are the only flags with short aliases (`--agent`/`--endpoint`/`--api-key` are
long-only). `--agent` selects which agent backend `--endpoint`/`--api-key`/`--model` apply to
(`Unsloth`, the default, or `LMStudio` — see [Config resolution](#config-resolution)). Omit
`--prompt` for an interactive session instead of a one-shot exchange. Any flag left out falls back
through the config layering described in [Config resolution](#config-resolution).

### Tests

Tests are split across two xUnit projects by what they exercise, not just by source location:
`RedStar.UnitTest` references only `RedStar.Base` and covers everything agent-agnostic (`ChatSession`,
`RedStarOptions`, `ModelSelector`, `ModelsClient`, `ConditionalAuthHandler`, the Unsloth agent
factory/extractor). `RedStar.UnitTest.Cli` references both `RedStar.Base` and `RedStar.Cli` and covers
`ChatCommandHandler`'s one-shot path and model-resolution branching. Keep new tests in whichever
project matches what they construct — a test that never touches `RedStar.Cli` types belongs in
`RedStar.UnitTest`, not the other way around, so `RedStar.UnitTest` doesn't regain a `RedStar.Cli`
reference by accretion.

`RedStar.Cli`'s classes (`ChatCommandHandler`, `ModelsCommandHandler`, `RedStarOptionsFactory`, ...)
are `internal`, exposed to `RedStar.UnitTest.Cli` via
`[assembly: InternalsVisibleTo("RedStar.UnitTest.Cli")]` in `RedStar.Cli/AssemblyInfo.cs`.
`ChatCommandHandler.RunAsync`/`ModelsCommandHandler.RunAsync` take optional trailing factory delegates
(`agentFactory`, `modelsClientFactory`) that default to their real production construction
(`UnslothAgentFactory.Create`/`new ModelsClient(options)`, or their LM Studio equivalents, depending on
`RedStarOptions.Agent`) — tests substitute `FakeChatClient`/`FakeModelsClient` there instead. This only
covers the one-shot path and model-resolution branching; the interactive REPL loop
(`Console.ReadLine`-driven) has no tests, since it isn't cheaply testable without redirecting stdin.

`FakeChatClient` is duplicated (not shared via a project reference) between `RedStar.UnitTest/Fakes/`
and `RedStar.UnitTest.Cli/Fakes/`, since `ChatSessionTests` (Base) and `ChatCommandHandlerTests` (Cli)
both need it and neither test project should depend on the other.

One consequence worth knowing: referencing `RedStar.Cli` from a test project transitively copies its
`appsettings.local.json` (real, holds a live API key) into that project's build output too — harmless
since `bin/`/`obj/` are git-ignored, but it means `RedStar.UnitTest.Cli` can't assume a blank-slate
environment and must only assert override *precedence*, not absolute "no config present" defaults.
`RedStar.UnitTest` no longer references `RedStar.Cli`, so it doesn't have this constraint.

## Architecture

### Project layout

`src/`: `RedStar.Base` (client library) ← `RedStar.Cli` (Spectre.Console.Cli entry point),
`RedStar.UnitTest`, and `RedStar.UnitTest.Cli` all depend on it; `RedStar.UnitTest.Cli` additionally
depends on `RedStar.Cli`.

`RedStar.Base` is meant to host multiple agents over time — agent-specific code lives under
`RedStar.Base/Agents/<AgentName>/` (e.g. `RedStar.Base/Agents/Unsloth/UnslothAgentFactory.cs`,
namespace `RedStar.Base.Agents.Unsloth`; `RedStar.Base/Agents/LMStudio/LMStudioAgentFactory.cs`,
namespace `RedStar.Base.Agents.LMStudio`), while `ChatSession`/`RedStarOptions`/telemetry stay in the
top-level `RedStar.Base` namespace since they're agent-agnostic (they only know about `AIAgent`,
never about any one agent's specifics). There are two concrete agents now (Unsloth, LM Studio), and
still no generic pluggable-agent interface/registry — that's deliberate, not an oversight left over
from when there was only one: `RedStarOptions.Agent` (see [Config resolution](#config-resolution))
selects between them via a plain string (`AgentNames.Unsloth`/`AgentNames.LMStudio`), and
`ChatCommandHandler`/`ModelsCommandHandler` pick the right factory/response-extractor/models-client
with an explicit two-way switch on that string. A third agent grows that switch to three arms; a
registry only becomes worth its indirection well past that.

`RedStar.WebApp/` is an ASP.NET Core MVC web frontend (Lit + Vite + Tailwind, referencing
`RedStar.Base`) — see `RedStar.WebApp/CLAUDE.md` for its architecture and
`RedStar.WebApp/GETTING_STARTED.md` for setup, commands, and troubleshooting; both are nested inside
that project rather than covered here.

### `RedStar.Cli` structure

`Program.cs` wires a Spectre.Console.Cli `CommandApp<ChatCommand>` — `ChatCommand`
(`Commands/ChatCommand.cs`) is both the app's default command and the `chat` subcommand (which is
why `redstar -p "hi"` and `redstar chat -p "hi"` behave identically), and `ModelsCommand` is the
`models` subcommand.

Each `*Command` class under `Commands/` is thin Spectre glue only: it resolves options via
`RedStarOptionsFactory.Build` and delegates straight to `ChatCommandHandler`/`ModelsCommandHandler`,
which hold all the actual logic and know nothing about Spectre.Console.Cli.
`RedStarOptionsFactory.Build` centralizes the `appsettings.json` → `appsettings.local.json` → env
vars → `RedStarOptions.ApplyOverrides` layering (see [Config resolution](#config-resolution)) — both
commands call it instead of duplicating it.

`ConsoleOutput.Error` is a markup-capable `IAnsiConsole` pointed at stderr (`AnsiConsole`'s default
console only writes stdout), used for every warning/error message so streamed model output and error
text can't interleave on the same stream.

`Program.cs` hooks `Ctrl+C` into a `CancellationTokenSource` once at startup and passes its token into
`app.RunAsync(args, token)`; Spectre.Console.Cli (0.55.0+) forwards that same token into every
`AsyncCommand<T>.ExecuteAsync`'s `CancellationToken` parameter automatically, so commands don't need
their own cancellation wiring.

### Interactive chat rendering

`ChatCommandHandler` draws each turn as a sequence of boxed panels, not a single overwritten panel.

- **User turns**: `PrintUserMessageBox`/`ReadUserMessageBoxed` draw a box (top border, a `>` prompt,
  bottom border).
- **Assistant turns are multi-box**: `ProduceStageEventsAsync` translates a streamed
  `ChatSession.SendAsync` call into `StageEvent`s (reasoning text, tool-status labels, web-search hits,
  answer text chunks) on an unbounded `Channel`, each tagged with a `TurnStage`
  (`Other`/`Reasoning`/`Searching`/`Generating`). `RenderStageBoxesAsync` reads that channel and opens
  one `AnsiConsole.Live` region per run of same-stage events via `StageBox`, sealing it and opening the
  next once the stage changes — see the "Multi-box rendering" remarks there. Tool-status labels and
  web-search hits come from an injected `IAgentResponseExtractor` (`RunAsync`'s `responseExtractor`
  parameter, defaulting to `UnslothAgentResponseExtractor`) rather than `ProduceStageEventsAsync`
  calling a concrete agent's extraction statics directly — see
  [Agent response extraction](#agent-response-extraction).
- **Height overflow**: a still-growing box that would exceed `GetSafeBoxHeight()` (console height minus
  a margin) seals early and reopens as a same-stage "(cont'd)" continuation instead of letting
  Spectre's `Live` region silently crop it from the top once it overflows the console (see the remarks
  on `GetSafeBoxHeight`/`StageBox.EstimatedBodyLines`).
- **Copy links**: a sealed box's footer gets a "Copy" link to a temp `.txt` file of its raw text
  (`StageBox.EnsureCopyFileUri`) — it's a real OSC-8 terminal hyperlink, so most terminals (Windows
  Terminal included) require Ctrl+Click to open it, not a plain click; that's the terminal
  deliberately guarding against a click during text selection, not a bug. Continuation boxes in the
  same "(cont'd)" chain share one such file/URI (threaded through as `StageBox`'s
  `sharedCopyFilePath`/`priorChainText` constructor args, tracked across the chain by
  `RenderStageBoxesAsync`'s `chainCopyFilePath`/`chainText`), each rewriting it with the whole chain's
  text so far — otherwise every continuation box's Copy link would resolve to only its own fragment
  instead of the full message the height split cut apart.

**Concurrency gotcha**: `DrainStageAsync` (draining events into the current box), `TickFooterAsync`
(the once-a-second elapsed-time redraw), and the live-region's own redraw callback all read/write the
same `StageBox`'s internal `StringBuilder` and the same `LiveDisplayContext`, so they share one
`SemaphoreSlim(1, 1)` (`gate`) acquired via `WaitAsync`/`Release` around every mutation and every
`ctx.UpdateTarget`/`ctx.Refresh()` call — a plain `lock` doesn't work here since these call sites need
to hold it across `await`s (`Monitor` is thread-affine and can't cross an `await`). Skipping the gate
around a `StringBuilder.Append` while another task concurrently calls `.ToString()` corrupts its
internal chunk list and throws `ArgumentOutOfRangeException` ("chunkLength") out of
`StringBuilder.ToString()` — don't add a fourth mutator of a box's text/panel without taking the same
gate.

### Config resolution

`RedStarOptions`, bound from the `RedStar` section, is layered as `appsettings.json` →
`appsettings.local.json` → environment variables (`RedStar__*`) → CLI flags (`--agent`, `--endpoint`,
`--api-key`, `--model`), each layer overriding the last. `appsettings.json` is the checked-in template
listing every key with its default; `appsettings.local.json` holds the real API key/model for local
dev. Only `appsettings.Production.json` is git-ignored — `appsettings.json` and
`appsettings.local.json` are both tracked, so double-check `appsettings.local.json`'s contents before
any commit/push that touches it; it currently holds a live Unsloth API key. `appsettings.local.json`
has no `LMStudio` section checked in (LM Studio's server has no auth by default, so there's no secret
to hold) — add one there, following `appsettings.json`'s template, to persist a local LM Studio
endpoint/default model instead of passing them as flags every run.

`RedStarOptions.Agent` (config key `RedStar:Agent`, env var `RedStar__Agent`, CLI flag `--agent`,
default `AgentNames.Unsloth`) selects which agent backend a run talks to — see
[Project layout](#project-layout) for how that selection is consumed. `RedStarOptions` is treated as a
"mega project" config root rather than a flat bag of Unsloth settings: agent-specific settings
(`BaseUrl`, `ApiKey`, `DefaultModel`, plus Unsloth's own `EnabledTools`) are nested under
`RedStarOptions.Agents.Unsloth`/`RedStarOptions.Agents.LMStudio` (`UnslothAgentOptions`/
`LMStudioAgentOptions` records, config keys `RedStar:Agents:Unsloth:*`/`RedStar:Agents:LMStudio:*`,
env vars `RedStar__Agents__Unsloth__*`/`RedStar__Agents__LMStudio__*`) instead of living flat on
`RedStarOptions` as if they were global. `RedStarOptions.ApplyOverrides`'s `agent`/`baseUrl`/`apiKey`/
`defaultModel` parameters apply the latter three to whichever single agent section `agent` (or,
unspecified, the already-configured `Agent`) resolves to — the other agent's section is always left
untouched by a given call. `Otel` stays top-level (`RedStarOptions.Otel`) since telemetry export is
genuinely agent-agnostic, not specific to any one agent.

`AgentsOptions`/`UnslothAgentOptions`/`LMStudioAgentOptions` are `record`s (not classes) specifically so
`RedStarOptions.ApplyOverrides` can rebuild them with `with` expressions rather than a deep-clone by
hand. `RedStarOptions.ApplyOverrides` implements the CLI-flags-win-if-non-blank step against whichever
single agent's section the resolved `Agent` points at (`Agents.Unsloth`'s three overridable fields, or
`Agents.LMStudio`'s); `Unsloth.EnabledTools` has no CLI flag (config/env-only), but
`ApplyOverrides` must still carry its value through into the new instance it returns — this list's
predecessor, a single `WebSearchEnabled` bool, was dropped (silently reset to `false`) for a while
because the method only copied the three overridable fields, and every CLI run rebuilds
`RedStarOptions` via `ApplyOverrides`. See the
`ApplyOverrides_PreservesEnabledTools_WhichHasNoCliOverride` test.

### Telemetry

`RedStarTelemetry` (`RedStar.Base`) is the ambient surface library code uses (`ActivitySource`,
`Meter`, a mutable `ILoggerFactory` defaulting to a no-op) — it references no OTel SDK package, only
BCL (`System.Diagnostics`) and `Microsoft.Extensions.Logging.Abstractions`, so
`ActivitySource.StartActivity` calls return `null` harmlessly (and logging is silently dropped) in
unit tests or any path that never runs the bootstrapper.

`RedStar.Cli/Telemetry/TelemetryBootstrapper.Configure` is the *only* place the OTel SDK/exporter/
instrumentation packages are referenced — it builds the `TracerProvider`/`MeterProvider`/
`ILoggerFactory` from `RedStarOptions.Otel` (`Enabled`, default `true`; `Endpoint`, default
`http://localhost:4317` — config/env-only like `EnabledTools`, no CLI flag) and assigns the logger
factory to `RedStarTelemetry.LoggerFactory`. `Program.cs` holds it in a `using` around `app.RunAsync`
so providers flush on normal exit, an unhandled exception, or Ctrl+C.

Each command (`chat`/`models`) opens its own root `Activity` inside
`ChatCommandHandler.RunAsync`/`ModelsCommandHandler.RunAsync` (not in `Program.cs`, since only
Spectre's parsed `CommandSettings` has the `--run-id` value) tagged `run.correlation.id` — from
`--run-id`, else the `REDSTAR_RUN_ID` env var, else a generated GUID — so every child span/log for that
invocation (chat turns, outbound HTTP calls via `AddHttpClientInstrumentation`) shares one trace ID.

Traces, metrics, and structured logs export to an OTLP collector (e.g. the standalone Aspire Dashboard
container) — never to the console or a file, so the boxed chat UI stays untouched.

### No-auth mode

An empty `ApiKey` (`RedStarOptions.Agents.Unsloth.ApiKey` or `Agents.LMStudio.ApiKey`) means "talk to a
server with no auth" — the default and expected state for LM Studio, which has authentication disabled
out of the box; Unsloth requires a bearer token, so this is the exceptional case there. Either way, the
OpenAI SDK requires a non-empty credential object, so `UnslothAgentFactory.Create`/
`LMStudioAgentFactory.Create` always pass a placeholder credential and rely on `ConditionalAuthHandler`
(a `DelegatingHandler`, top-level `RedStar.Base` namespace — agent-agnostic, shared by both factories)
to strip the `Authorization` header from outgoing requests when no real key is configured. Don't try to
"fix" this by passing a null/empty credential to the SDK — it throws.

### Model resolution

`ChatCommandHandler.ResolveModelAsync` always lists models (via whichever `IModelsClient` the active
agent defaults to — see [Two HTTP paths, not one](#two-http-paths-not-one)) before building the chat
client, whether or not a default model is configured, so a bad model id is caught here — with a clear
error — instead of surfacing later as a misleading "the model returned no response" once the chat
stream unexpectedly ends empty (Unsloth just silently drops the connection for an unloaded/nonexistent
model rather than erroring).

Every outcome requires at least one model to be currently *loaded* — an id the server merely knows
about but hasn't loaded isn't usable — unless the caller opts into `allowJitLoad` (below), which only
`ChatCommandHandler` does for the LM Studio agent. `ModelSelector.SelectDefault` also excludes
embeddings-type models (`ModelInfo.Type == "embeddings"`, only ever populated by
`LMStudioModelsClient`; always null and therefore never excluded for Unsloth) from every rule below —
an embeddings model can't serve a chat request, so it must never be auto-selected or count toward
"multiple models loaded". Resolves in order:

1. **`allowJitLoad` is true and the configured default names a chat-capable model the server knows
   about but hasn't loaded** → succeeds immediately (`ModelSelectionSource.PendingJitLoad`), trusting
   the server to load it on the first request instead of failing. This is LM Studio's just-in-time
   loading; `ChatCommandHandler` passes `allowJitLoad: true` only when `RedStarOptions.Agent` is
   `AgentNames.LMStudio` — Unsloth has no equivalent capability, so this rule never fires for it.
2. **Zero (chat-capable) models loaded anywhere** → hard failure (graceful error, `ChatCommandHandler`
   exits 1).
3. **A configured default model, non-blank** → it must be one of the currently-loaded models, or
   resolution fails even though some *other* model is loaded — silently substituting a
   different model than the one configured would be misleading, so there's no fallback here, only an
   error+exit (`ModelSelectionSource.Explicit` on success).
4. **No default configured and exactly one (chat-capable) model is loaded** → that model is used
   (`ModelSelectionSource.Implicit`), with an informational message surfaced at startup and in
   telemetry, since it wasn't asked for by name.
5. **No default configured and multiple (chat-capable) models are loaded** → ambiguous, hard failure.

`ModelSelector.SelectDefault` returns a `ModelSelectionResult` (`Model`/`Source`/`InfoMessage` on
success, `ErrorMessage` on failure) rather than a bare model/`null`, so callers can distinguish *why*
resolution failed and which of the three success paths was taken.

`ChatCommandHandler.PrintStartupInfoBox` prints a boxed summary of the run's effective configuration
(which agent, endpoint, whether an API key is configured, the resolved model plus how it was picked,
every documented Unsloth tool's enabled/disabled state (via `UnslothTools.Known`, see
[Unsloth-specific request fields via `Patch`](#unsloth-specific-request-fields-via-patch)) when the
active agent has such a concept, telemetry export) once per run before any chat request goes out,
and mirrors the same fields
onto the run's OTel activity as `redstar.config.*` tags plus one structured log line, so the resolved
configuration is recoverable from telemetry too.

### Unsloth-specific request fields via `Patch`

Unsloth's API extends OpenAI's chat-completions schema with fields the OpenAI SDK doesn't model
(`enable_thinking`, `enable_tools`, `enabled_tools`, `session_id`). These get attached through
`OpenAI.Chat.ChatCompletionOptions`'s experimental `Patch` property
(`System.ClientModel.Primitives.JsonPatch`, gated behind diagnostic `SCME0001` — suppress locally with
a scoped `#pragma`, don't blanket-suppress project-wide), then handed to `Microsoft.Extensions.AI` via
`ChatOptions.RawRepresentationFactory`. See `UnslothAgentFactory.CreateChatOptions` for the current
example: whenever `RedStarOptions.Agents.Unsloth.EnabledTools` is non-empty, it sends `enable_tools:
true` and `enabled_tools` as that list verbatim. `EnabledTools` is free-form (any string Unsloth's
server recognizes works, not just the documented `python`/`bash`/`web_search` three catalogued in
`RedStar.Base.Agents.Unsloth.UnslothTools.Known`) — adding a newly-documented Unsloth tool needs no
code change here, only a config value; `UnslothTools.Known` only exists so
`ChatCommandHandler.PrintStartupInfoBox` (see [Model resolution](#model-resolution)) can enumerate
every documented tool's on/off state at startup, not to gate what `CreateChatOptions` will send.

`Create` composes that `ChatOptions` as the built agent's *default* `ChatOptions` (via
`ChatClientAgentOptions`), so it applies to every run automatically instead of every call site having
to remember to pass it alongside `SendAsync`.

Extending to other Unsloth-specific fields follows the same pattern: build/extend the
`ChatCompletionOptions` inside `CreateChatOptions`, not inside `ChatSession`, which stays
agent-agnostic (it only knows about `AIAgent`, never about Unsloth or HTTP).

### Agent response extraction

`IAgentResponseExtractor` (`RedStar.Base/IAgentResponseExtractor.cs`, top-level namespace —
agent-agnostic like `ChatSession`/`RedStarOptions`) is the seam for pulling provider-specific
side-channel data out of a streamed `AgentResponseUpdate`: `TryGetToolStatus` (a human-readable
tool-activity label) and `TryGetWebSearchResults` (a completed search-hit list, `WebSearchResult`
records). `RedStar.Base.Agents.Unsloth.UnslothAgentResponseExtractor` unwraps Unsloth's custom
`tool_status`/`tool_end` SSE events, which sit outside the OpenAI chat-completions chunk schema, from
`AgentResponseUpdate.RawRepresentation`. `RedStar.Base.Agents.LMStudio.LMStudioAgentResponseExtractor`
is a deliberate no-op (both methods always return null) — LM Studio's OpenAI-compatible streaming
carries no equivalent custom SSE events, so there's nothing to unwrap; see
[LM Studio agent](#lm-studio-agent).

`ChatCommandHandler.RunAsync` takes an optional `responseExtractor` parameter (same
inject-for-tests pattern as `agentFactory`/`modelsClientFactory`), defaulting to
`new UnslothAgentResponseExtractor()` or `new LMStudioAgentResponseExtractor()` depending on
`RedStarOptions.Agent`; `ProduceStageEventsAsync` calls the interface, never a concrete agent's type,
so each agent plugs in its own extractor without any agent-type branching below
`ChatCommandHandler.RunAsync` itself. Tests substitute `FakeAgentResponseExtractor`
(`RedStar.UnitTest.Cli/Fakes/`) here instead of depending on real Unsloth SSE JSON shapes.

### LM Studio agent

`RedStar.Base.Agents.LMStudio.LMStudioAgentFactory.Create` builds the LM Studio agent the same way
`UnslothAgentFactory.Create` builds Unsloth's — same `OpenAIClient`/`ConditionalAuthHandler` shape,
default endpoint `http://127.0.0.1:1234/v1` (LM Studio's default local server port) — but with no
`CreateChatOptions`/`Patch` step: LM Studio's tool-calling/structured-output/streaming all use the
standard OpenAI schema that the OpenAI SDK/`Microsoft.Extensions.AI` already model directly, unlike
Unsloth's `enable_tools`/`enabled_tools` extension (see
[Unsloth-specific request fields via `Patch`](#unsloth-specific-request-fields-via-patch)).

Model listing goes through `LMStudioModelsClient`, not a config of the shared `ModelsClient` —
LM Studio's OpenAI-compatible `GET /v1/models` (what `ModelsClient` calls for Unsloth) reports no
load-state field at all, so `LMStudioModelsClient` instead calls LM Studio's *native*
`GET /api/v0/models`, which reports `state` (loaded/not-loaded), `type` (llm/vlm/embeddings),
`max_context_length`, and `quantization` per model. That endpoint hangs off the server root rather
than the `/v1` OpenAI-compat prefix `LMStudioAgentOptions.BaseUrl` is configured with, so
`LMStudioModelsClient` derives the root by stripping a trailing `/v1`. These extra fields are why
`ModelInfo` grew nullable `Type`/`MaxContextLength`/`Quantization` properties (always null from
Unsloth's `ModelsClient`, which has no equivalent JSON to populate them from) — `Type` feeds
`ModelSelector`'s embeddings-model exclusion (see [Model resolution](#model-resolution)), and all
three surface as an extra "Details" column in `redstar models`'s output.

LM Studio can load a downloaded-but-not-currently-loaded model on demand when a request references it
(just-in-time loading) instead of requiring it pre-loaded like Unsloth does — `ChatCommandHandler`
passes `allowJitLoad: true` to `ModelSelector.SelectDefault` only for this agent, see
[Model resolution](#model-resolution) for the resulting `ModelSelectionSource.PendingJitLoad` outcome.
LM Studio's server also has authentication disabled by default (unlike Unsloth, which requires a
bearer token) — `ChatCommandHandler` skips the "no API key configured" warning entirely for this
agent rather than printing Unsloth's wording, which would be actively wrong here.

### Two HTTP paths, not one

`ModelsClient` (Unsloth, `GET /v1/models`) and `LMStudioModelsClient` (LM Studio, `GET /api/v0/models`
— see [LM Studio agent](#lm-studio-agent) for why a different endpoint) are both plain hand-rolled
`HttpClient`/`System.Text.Json` clients — neither goes through the OpenAI SDK or `IChatClient`,
because model listing isn't part of that abstraction. Chat completions go through
`UnslothAgentFactory`/`LMStudioAgentFactory` → OpenAI SDK → `IChatClient` → `AIAgent`. Keep that split
in mind when adding features: "does this belong on the models endpoint or the chat endpoint"
determines which client it touches.

### `ChatSession`

Binds to a single `AIAgent` (passed in its constructor) and represents one conversation with it. It
lazily creates the framework's `AgentSession` on the first `SendAsync` call and lets the agent's chat
history provider (`InMemoryChatHistoryProvider` by default) own message history — `ChatSession` no
longer tracks messages itself; `Messages` reads them back via
`AgentSessionExtensions.TryGetInMemoryChatHistory`.

The system prompt is no longer a runtime `ChatSession` call: it's supplied once as `instructions` to
`UnslothAgentFactory.Create`, which becomes the agent's `Instructions` and is merged into
`ChatOptions.Instructions` on every run by `ChatClientAgent` — it is *not* injected as a
`ChatRole.System` message.

History is only committed after a successful response (nothing is persisted, on either side of the
turn, if the call throws). Tests fake the underlying model with `FakeChatClient`
(`RedStar.UnitTest/Fakes/`, still a plain `IChatClient`) wrapped in a real `ChatClientAgent`, and
assert via `FakeChatClient.LastMessages`/`LastOptions` plus `ChatSession.Messages`.
