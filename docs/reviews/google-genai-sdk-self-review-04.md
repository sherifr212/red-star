# Self-review round 4 (sequence 4 of 4): GoogleAI / Google.GenAI SDK work (PR #45)

**This is review #4 in the sequence, following on from
[`google-genai-sdk-self-review-03.md`](./google-genai-sdk-self-review-03.md) (review #3), which itself
followed [`google-genai-sdk-self-review-02.md`](./google-genai-sdk-self-review-02.md) (review #2) and
[`google-genai-sdk-self-review.md`](./google-genai-sdk-self-review.md) (review #1).** Review #3 found no
new issues in its own diff, but this round re-reviewed the whole GoogleAI agent from scratch --
including decompiling the third-party `Google.GenAI` 1.18.0 SDK itself (via `ilspycmd`) rather than
trusting this codebase's doc comments about its behavior -- and surfaced four findings, one of them a
real multi-turn correctness bug in `RawRepresentationFactory`'s handling of native-only hosted tools
that none of rounds 1-3 caught, since none of their tests ever invoked the same built `ChatOptions`'
delegates more than once (i.e. never simulated a multi-turn session). All four are fixed below. All 252
tests in the solution (`RedStar.UnitTest`: 218, `RedStar.UnitTest.Cli`: 17,
`RedStar.UnitTest.Controller`: 17) pass, and `dotnet build RedStar.slnx` is clean, as of the change
described below.

## Disposition of every review #4 finding

| # | Finding | Disposition |
|---|---|---|
| 11 | `RawRepresentationFactory` for native-only hosted tools reuses one mutable `List<Tool>` across every turn, so the SDK's in-place append of `ChatOptions.Tools`-derived entries compounds across turns | **Fixed** |
| 12 | `ThinkingEffort` numeric strings (e.g. `"3"`, `"47"`) silently parse via `Enum.TryParse`, contradicting the documented "unrecognized value is ignored" behavior | **Fixed** |
| 13 | The end-to-end test verifying `Create()`'s actual wiring (endpoint/model/HttpClient) sends a real request was deleted during the SDK swap and never replaced | **Fixed** |
| 14 | GoogleAI's `HostedTools`/`ThinkingEffort`/`IncludeThoughts` never appear in the startup info box or `redstar.config.*` OTel tags, unlike Unsloth's `EnabledTools` | **Fixed** |

### 11. `RawRepresentationFactory` mutable-list reuse across turns -- Fixed

`GoogleAIAgentFactory.CreateChatOptions` (`src/RedStar.Base/Agents/GoogleAI/GoogleAIAgentFactory.cs`)
built its `RawRepresentationFactory` delegate as `_ => new GenerateContentConfig { Tools =
nativeOnlyTools }`, closing over the same `nativeOnlyTools` list on every invocation. `ChatOptions` is
built once per agent (in `Create`) and reused as the agent's default options for every turn of a
session, so the same delegate instance runs once per request. Decompiling `Google.GenAI` 1.18.0's
`GoogleGenAIChatClient.CreateRequest` confirmed it mutates the `GenerateContentConfig.Tools` list
returned by this factory *in place*, appending whatever `ChatOptions.Tools`-derived entries the same
request also carries (client-injected tools, or a `MappedTools` hosted tool like `GoogleSearch`). With
one shared list, turn 1 leaves it with the appended entries still attached; turn 2's call to the same
delegate returns that already-mutated list, which the SDK appends to *again* -- growing unboundedly and
sending duplicate tool declarations to Gemini on every subsequent turn of an interactive session that
combines a native-only hosted tool (`UrlContext`) with any other tool source.

Fixed by having the delegate return a fresh copy on every call:
`chatOptions.RawRepresentationFactory = _ => new GenerateContentConfig { Tools = new
List<Tool>(nativeOnlyTools) };`. Each invocation now starts from an unmutated snapshot of the
originally-configured native-only tools, so whatever the SDK appends to it that turn never survives
into the next call.

Coverage: `CreateChatOptions_RawRepresentationFactory_ReturnsAFreshToolsListEachCall` (new,
`RedStar.UnitTest/GoogleAIAgentFactoryTests.cs`) invokes the factory delegate twice, mutates the first
call's returned list (simulating the SDK's in-place append) between calls, and asserts the second call
returns a distinct list still holding only the originally-configured entry.

### 12. `ThinkingEffort` numeric strings silently parsed -- Fixed

`Enum.TryParse<ReasoningEffort>(googleAI.ThinkingEffort, ignoreCase: true, out var parsedEffort)`
succeeds for any numeric string regardless of whether it names a real enum member -- `"3"` parses to
whatever `ReasoningEffort` member has underlying value 3 (or an undefined boxed value for an
out-of-range number like `"47"`) instead of failing, contradicting `GoogleAIAgentOptions.ThinkingEffort`'s
own doc comment that an unrecognized value is treated the same as blank. A mistyped or copy-pasted
numeric config value would silently apply an unintended (or undefined) thinking-effort level rather than
falling back to the model's own default.

Fixed by checking the value against the enum's actual member names (`Enum.GetNames<ReasoningEffort>()`)
before parsing, rather than relying on `TryParse`'s looser numeric-string acceptance:

```csharp
ReasoningEffort? effort = null;
if (!string.IsNullOrWhiteSpace(googleAI.ThinkingEffort) &&
    Enum.GetNames<ReasoningEffort>().Any(
        name => name.Equals(googleAI.ThinkingEffort, StringComparison.OrdinalIgnoreCase)))
{
    effort = Enum.Parse<ReasoningEffort>(googleAI.ThinkingEffort, ignoreCase: true);
}
```

Coverage: `CreateChatOptions_LeavesEffortNull_WhenThinkingEffortIsANumericString` (new) asserts
`ThinkingEffort = "3"` leaves `chatOptions.Reasoning.Effort` null, same as any other unrecognized value.
Every existing `ThinkingEffort` test (named-value, case-insensitive, blank, unrecognized-word) still
passes unmodified.

### 13. Missing end-to-end test for `Create()`'s wiring on the new SDK -- Fixed

The SDK swap (`04d47eb`) deleted `Create_BuiltAgent_SendsRequestToConfiguredEndpointAndModel`, which
used to prove the built agent's `BaseUrl`/model id/`HttpClient` wiring actually produced a working
OpenAI-shim request, and never replaced it with an equivalent for the new `Google.GenAI`-based path.
`GoogleAIThoughtSignaturePreservationTests` build a `Client`/`IChatClient` by hand, bypassing
`GoogleAIAgentFactory.Create()` entirely, so nothing in the suite would catch a future regression in
`Create()`'s own endpoint/model/HttpClient threading.

Added back an equivalent test using the same `CapturingHandler` pattern, adjusted for the new SDK's
request shape (`{model}:generateContent` under the configured `BaseUrl`, confirmed by decompiling
`Models.PrivateGenerateContentAsync`/`GenerateContentParametersToMldev`, not guessed): `Create_BuiltAgent_
SendsRequestToConfiguredEndpointAndModel` builds the agent with `BaseUrl =
"https://generativelanguage.googleapis.com/"` and model `"my-model"`, runs it, and asserts the captured
request URI starts with the configured base URL, contains the model id and `:generateContent`, and the
captured body contains the sent message text.

### 14. GoogleAI's hosted-tools/thinking settings invisible in startup diagnostics -- Fixed

CLAUDE.md documents `ChatCommandHandler.PrintStartupInfoBox` as showing "every documented [agent]
tool's enabled/disabled state ... when the active agent has such a concept" plus mirroring the same
fields onto OTel tags. `AgentConfigurationResolver.Resolve` passed `null, null` for
`Tools`/`KnownToolNames` on the GoogleAI branch, so `HostedTools` (`GoogleSearch`/`CodeExecution`/
`UrlContext`) never appeared in the startup box or `redstar.config.*` tags, and `ThinkingEffort`/
`IncludeThoughts` had no representation there either -- a misconfigured hosted tool (including either
of findings #11/#12's edge cases) would be invisible until the model's behavior itself revealed it.

Fixed in two places:
- `AgentConfigurationResolver.Resolve`'s GoogleAI branch now passes the names of every hosted tool
  currently enabled in `options.Agents.GoogleAI.HostedTools` as `Tools`, and `GoogleAIHostedTools.Known`
  as `KnownToolNames` -- the same "every known tool, on/off" shape `FormatToolsSummary` already renders
  for Unsloth's `EnabledTools`, so the existing "Tools" row and `redstar.config.enabled_tools` tag pick
  this up for free.
- `ChatStartupConsole.PrintStartupInfoBox` (`src/RedStar.Cli/Rendering/ChatStartupConsole.cs`) adds a
  GoogleAI-only "Thinking effort"/"Include thoughts" row pair (mirrored onto
  `redstar.config.google_ai.thinking_effort`/`redstar.config.google_ai.include_thoughts` tags), showing
  `"model default"` when `ThinkingEffort` is blank rather than an empty value.

Coverage: `AgentConfigurationResolverTests.Resolve_SurfacesEnabledHostedTools_ForGoogleAI` (new,
`RedStar.UnitTest.Cli/AgentConfigurationResolverTests.cs`) builds a `HostedTools` dictionary with a mix
of enabled/disabled tools and asserts `Resolve` returns exactly the enabled ones as `Tools` and
`GoogleAIHostedTools.Known` as `KnownToolNames`. `ChatStartupConsole`'s rendering itself has no existing
test coverage for any agent (it's console output, not logic under test elsewhere in this codebase
either), so the new rows follow the same untested-rendering precedent as the rest of that file --
verified manually by inspection instead.

## Fresh look at the round-4 diff

Re-reviewed every changed file for the same categories prior rounds checked:

- **No new duplication or dead code.** The `RawRepresentationFactory` fix is a one-line change to an
  existing closure; the `ThinkingEffort` fix replaces one condition with an equivalent, correctly-scoped
  one, adding no new state.
- **No behavior change for existing callers** beyond the four fixes' intended scope: hosted-tool
  dictionaries with only correctly-named, non-numeric values behave identically to before; the startup
  box's new GoogleAI rows are additive and only render when `active.AgentName == AgentNames.GoogleAI`.
- **Test placement matches convention**: the `CreateChatOptions`/`Create` tests stay in
  `RedStar.UnitTest/GoogleAIAgentFactoryTests.cs` alongside their siblings; the new resolver test lives in
  `RedStar.UnitTest.Cli` since `AgentConfigurationResolver` is `internal` to `RedStar.Cli`, exposed only
  via `InternalsVisibleTo`, matching every other CLI-internal-type test in that project.
- **No leftover scratch files or commented-out code** from any of the four fixes.

No further findings surfaced in this pass.
