# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

RedStar is a .NET CLI for chatting with a locally/self-hosted LLM server (Unsloth Studio) over its
OpenAI-compatible `/v1` API. It's a thin client: `RedStar.Cli` (the `redstar` executable) drives
`RedStar.Base` (the client library), which wraps the official `OpenAI` .NET SDK via the
Microsoft Agent Framework's `AIAgent` abstraction (`Microsoft.Agents.AI`) rather than talking HTTP
directly for chat. `AIAgent` itself wraps `Microsoft.Extensions.AI`'s `IChatClient` — RedStar builds
the `IChatClient` exactly as before and wraps it one layer higher instead of consuming it directly.

## Commands

The solution file lives at `src/RedStar.slnx` (not the repo root) — run dotnet commands from `src/`.

```
dotnet build RedStar.slnx
dotnet test RedStar.slnx
dotnet test RedStar.slnx --filter "FullyQualifiedName~ChatSessionTests"   # single test class
dotnet test RedStar.slnx --filter "FullyQualifiedName~SendAsync_MergesInstructions_IntoChatOptions"  # single test
dotnet run --project RedStar.Cli -- chat -p "hello"                      # one-shot prompt
dotnet run --project RedStar.Cli -- models                               # list models on the server
```

Test project is xUnit (`RedStar.UnitTest`), referencing both `RedStar.Base` and `RedStar.Cli`.
`RedStar.Cli`'s classes (`ChatCommandHandler`, `ModelsCommandHandler`, `RedStarOptionsFactory`, ...)
are `internal`, exposed to the test project via `[assembly: InternalsVisibleTo("RedStar.UnitTest")]`
in `RedStar.Cli/AssemblyInfo.cs`. `ChatCommandHandler.RunAsync`/`ModelsCommandHandler.RunAsync` take
optional trailing factory delegates (`agentFactory`, `modelsClientFactory`) that default to their real
production construction (`UnslothAgentFactory.Create`, `new ModelsClient(options)`) — tests
substitute `FakeChatClient`/`FakeModelsClient` there instead. This only covers the one-shot path and
model-resolution branching; the interactive REPL loop (`Console.ReadLine`-driven) has no tests, since
it isn't cheaply testable without redirecting stdin. One consequence worth knowing: referencing
`RedStar.Cli` from the test project transitively copies its `appsettings.local.json` (real, holds a
live API key) into `RedStar.UnitTest`'s build output too — harmless since `bin/`/`obj/` are
git-ignored, but it means `RedStarOptionsFactory` tests can't assume a blank-slate environment and
must only assert override *precedence*, not absolute "no config present" defaults.

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

**Project layout** (`src/`): `RedStar.Base` (client library) ← `RedStar.Cli` (Spectre.Console.Cli
entry point) and `RedStar.UnitTest` both depend on it. Backend-specific agent code lives under
`RedStar.Base/Agents/<AgentName>/` — e.g. `RedStar.Base/Agents/Unsloth/UnslothAgentFactory.cs` —
so `RedStar.Base`'s top-level namespace stays reserved for agent-agnostic pieces (`ChatSession`,
`RedStarOptions`, telemetry) that only know about the `AIAgent` abstraction, never about a specific
backend. There's only one agent (Unsloth) today; a shared `IRedStarAgent`-style interface and
config-level namespacing (e.g. `RedStar:Agents:Unsloth:*`) are deliberately not designed yet since
there's no second agent to validate that abstraction against.

**`RedStar.Cli` structure**: `Program.cs` wires a Spectre.Console.Cli `CommandApp<ChatCommand>` —
`ChatCommand` (`Commands/ChatCommand.cs`) is both the app's default command and the `chat`
subcommand (which is why `redstar -p "hi"` and `redstar chat -p "hi"` behave identically), and
`ModelsCommand` is the `models` subcommand. Each `*Command` class under `Commands/` is thin Spectre
glue only: it resolves options via `RedStarOptionsFactory.Build` and delegates straight to
`ChatCommandHandler`/`ModelsCommandHandler`, which hold all the actual logic and know nothing about
Spectre.Console.Cli. `RedStarOptionsFactory.Build` centralizes the `appsettings.json` →
`appsettings.local.json` → env vars → `RedStarOptions.ApplyOverrides` layering described below —
both commands call it instead of duplicating it. `ConsoleOutput.Error` is a markup-capable
`IAnsiConsole` pointed at stderr (`AnsiConsole`'s default console only writes stdout), used for
every warning/error message so streamed model output and error text can't interleave on the same
stream. `Program.cs` hooks `Ctrl+C` into a `CancellationTokenSource` once at startup and passes its
token into `app.RunAsync(args, token)`; Spectre.Console.Cli (0.55.0+) forwards that same token into
every `AsyncCommand<T>.ExecuteAsync`'s `CancellationToken` parameter automatically, so commands
don't need their own cancellation wiring.

**Interactive chat rendering** (`ChatCommandHandler`): user turns draw as boxed panels via
`PrintUserMessageBox`/`ReadUserMessageBoxed` (top border, a `>` prompt, bottom border). The
assistant side is multi-box, not one panel overwritten in place: `ProduceStageEventsAsync`
translates a streamed `ChatSession.SendAsync` call into `StageEvent`s (reasoning text, Unsloth
tool-status labels, web-search hits, answer text chunks) on an unbounded `Channel`, each tagged
with a `TurnStage` (`Other`/`Reasoning`/`Searching`/`Generating`); `RenderStageBoxesAsync` reads
that channel and opens one `AnsiConsole.Live` region per run of same-stage events via `StageBox`,
sealing it and opening the next once the stage changes — see the "Multi-box rendering" remarks
there. A still-growing box that would exceed `GetSafeBoxHeight()` (console height minus a margin)
seals early and reopens as a same-stage "(cont'd)" continuation instead of letting Spectre's `Live`
region silently crop it from the top once it overflows the console (see the remarks on
`GetSafeBoxHeight`/`StageBox.EstimatedBodyLines`). A sealed box's footer gets a "Copy" link to a
temp `.txt` file of its raw text (`StageBox.EnsureCopyFileUri`) — it's a real OSC-8 terminal
hyperlink, so most terminals (Windows Terminal included) require Ctrl+Click to open it, not a plain
click; that's the terminal deliberately guarding against a click during text selection, not a bug.
Continuation boxes in the same "(cont'd)" chain share one such file/URI (threaded through as
`StageBox`'s `sharedCopyFilePath`/`priorChainText` constructor args, tracked across the chain by
`RenderStageBoxesAsync`'s `chainCopyFilePath`/`chainText`), each rewriting it with the whole chain's
text so far — otherwise every continuation box's Copy link would resolve to only its own fragment
instead of the full message the height split cut apart.

`DrainStageAsync` (draining events into the current box), `TickFooterAsync` (the once-a-second
elapsed-time redraw), and the live-region's own redraw callback all read/write the same `StageBox`'s
internal `StringBuilder` and the same `LiveDisplayContext`, so they share one `SemaphoreSlim(1, 1)`
(`gate`) acquired via `WaitAsync`/`Release` around every mutation and every
`ctx.UpdateTarget`/`ctx.Refresh()` call — a plain `lock` doesn't work here since these call sites
need to hold it across `await`s (`Monitor` is thread-affine and can't cross an `await`). Skipping
the gate around a `StringBuilder.Append` while another task concurrently calls `.ToString()`
corrupts its internal chunk list and throws `ArgumentOutOfRangeException` ("chunkLength") out of
`StringBuilder.ToString()` — don't add a fourth mutator of a box's text/panel without taking the
same gate.

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

**Telemetry** (`RedStarTelemetry` in `RedStar.Base`, `TelemetryBootstrapper` in `RedStar.Cli`):
OpenTelemetry traces, metrics, and structured logs export to an OTLP collector (e.g. the standalone
Aspire Dashboard container) — never to the console or a file, so the boxed chat UI stays untouched.
`RedStarTelemetry` is the ambient surface library code uses (`ActivitySource`, `Meter`, a mutable
`ILoggerFactory` defaulting to a no-op) — it references no OTel SDK package, only BCL
(`System.Diagnostics`) and `Microsoft.Extensions.Logging.Abstractions`, so `ActivitySource.StartActivity`
calls return `null` harmlessly (and logging is silently dropped) in unit tests or any path that never
runs the bootstrapper. `RedStar.Cli/Telemetry/TelemetryBootstrapper.Configure` is the *only* place the
OTel SDK/exporter/instrumentation packages are referenced — it builds the `TracerProvider`/
`MeterProvider`/`ILoggerFactory` from `RedStarOptions.Otel` (`Enabled`, default `true`; `Endpoint`,
default `http://localhost:4317` — config/env-only like `WebSearchEnabled`, no CLI flag) and assigns the
logger factory to `RedStarTelemetry.LoggerFactory`. `Program.cs` holds it in a `using` around
`app.RunAsync` so providers flush on normal exit, an unhandled exception, or Ctrl+C. Each command
(`chat`/`models`) opens its own root `Activity` inside `ChatCommandHandler.RunAsync`/
`ModelsCommandHandler.RunAsync` (not in `Program.cs`, since only Spectre's parsed `CommandSettings` has
the `--run-id` value) tagged `run.correlation.id` — from `--run-id`, else the `REDSTAR_RUN_ID` env var,
else a generated GUID — so every child span/log for that invocation (chat turns, outbound HTTP calls
via `AddHttpClientInstrumentation`) shares one trace ID.

**No-auth mode**: `RedStarOptions.ApiKey` empty means "talk to a server with no auth" — but the
OpenAI SDK requires a non-empty credential object, so `UnslothAgentFactory.Create` always
passes a placeholder credential and relies on `ConditionalAuthHandler` (a `DelegatingHandler`) to
strip the `Authorization` header from outgoing requests when no real key is configured. Don't try
to "fix" this by passing a null/empty credential to the SDK — it throws.

**Model resolution** (`ModelSelector.SelectDefault`): `ChatCommandHandler.ResolveModelAsync` always
hits `/v1/models` before building the chat client, whether or not `RedStarOptions.DefaultModel` is
set, so a bad model id is caught here — with a clear error — instead of surfacing later as a
misleading "the model returned no response" once the chat stream unexpectedly ends empty (Unsloth
just silently drops the connection for an unloaded/nonexistent model rather than erroring). Every
outcome requires at least one model to be currently *loaded* — an id the server merely knows about
but hasn't loaded isn't usable. Resolution, in order: (1) zero models loaded anywhere → hard
failure (graceful error, `ChatCommandHandler` exits 1). (2) `DefaultModel` configured and non-blank
→ it must be one of the currently-loaded models, or resolution fails even though some *other* model
is loaded — silently substituting a different model than the one configured would be misleading, so
there's no fallback here, only an error+exit (`ModelSelectionSource.Explicit` on success). (3) no
`DefaultModel` configured and exactly one model is loaded → that model is used
(`ModelSelectionSource.Implicit`), with an informational message surfaced at startup and in
telemetry, since it wasn't asked for by name. (4) no `DefaultModel` configured and multiple models
are loaded → ambiguous, hard failure. `ModelSelector.SelectDefault` returns a `ModelSelectionResult`
(`Model`/`Source`/`InfoMessage` on success, `ErrorMessage` on failure) rather than a bare
model/`null`, so callers can distinguish *why* resolution failed and which of the two success paths
was taken. `ChatCommandHandler.PrintStartupInfoBox` prints a boxed summary of the run's effective
configuration (endpoint, whether an API key is configured, the resolved model plus its
implicit/explicit source, web search, telemetry export) once per run before any chat request goes
out, and mirrors the same fields onto the run's OTel activity as `redstar.config.*` tags plus one
structured log line, so the resolved configuration is recoverable from telemetry too.

**Unsloth-specific request fields via `Patch`**: Unsloth's API extends OpenAI's chat-completions
schema with fields the OpenAI SDK doesn't model (`enable_thinking`, `enable_tools`,
`enabled_tools`, `session_id`). These get attached through `OpenAI.Chat.ChatCompletionOptions`'s
experimental `Patch` property (`System.ClientModel.Primitives.JsonPatch`, gated behind diagnostic
`SCME0001` — suppress locally with a scoped `#pragma`, don't blanket-suppress project-wide), then
handed to `Microsoft.Extensions.AI` via `ChatOptions.RawRepresentationFactory`. See
`UnslothAgentFactory.CreateChatOptions` for the current example (toggles Unsloth's server-side
`web_search` tool from `RedStarOptions.WebSearchEnabled`). `Create` composes that `ChatOptions` as
the built agent's *default* `ChatOptions` (via `ChatClientAgentOptions`), so it applies to every
run automatically instead of every call site having to remember to pass it alongside `SendAsync`.
Extending to other Unsloth-specific fields follows the same pattern: build/extend the
`ChatCompletionOptions` inside `CreateChatOptions`, not inside `ChatSession`, which stays
agent-agnostic (it only knows about `AIAgent`, never about Unsloth or HTTP).

**Two HTTP paths, not one**: `ModelsClient` (`GET /v1/models`) is a plain hand-rolled
`HttpClient`/`System.Text.Json` client — it does not go through the OpenAI SDK or `IChatClient`,
because model listing isn't part of that abstraction. Chat completions go through
`UnslothAgentFactory` → OpenAI SDK → `IChatClient` → `AIAgent`. Keep that split in mind when
adding features: "does this belong on the models endpoint or the chat endpoint" determines which
client it touches.

**`ChatSession`** binds to a single `AIAgent` (passed in its constructor) and represents one
conversation with it. It lazily creates the framework's `AgentSession` on the first `SendAsync`
call and lets the agent's chat history provider (`InMemoryChatHistoryProvider` by default) own
message history — `ChatSession` no longer tracks messages itself; `Messages` reads them back via
`AgentSessionExtensions.TryGetInMemoryChatHistory`. The system prompt is no longer a runtime
`ChatSession` call: it's supplied once as `instructions` to `UnslothAgentFactory.Create`,
which becomes the agent's `Instructions` and is merged into `ChatOptions.Instructions` on every
run by `ChatClientAgent` — it is *not* injected as a `ChatRole.System` message. History is only
committed after a successful response (nothing is persisted, on either side of the turn, if the
call throws). Tests fake the underlying model with `FakeChatClient` (`RedStar.UnitTest/Fakes/`,
still a plain `IChatClient`) wrapped in a real `ChatClientAgent`, and assert via
`FakeChatClient.LastMessages`/`LastOptions` plus `ChatSession.Messages`.
