# Self-review round 2 (sequence 2 of 3): GoogleAI / Google.GenAI SDK work (PR #45)

**This is review #2 in the sequence, following on from
[`google-genai-sdk-self-review.md`](./google-genai-sdk-self-review.md) (review #1), and followed by
[`google-genai-sdk-self-review-03.md`](./google-genai-sdk-self-review-03.md) (review #3).** Review #1 found 9
issues across the GoogleAI/`Google.GenAI` SDK work. This document tracks what happened to each one,
plus a fresh look at the code that changed while fixing them. All 247 tests in the solution
(`RedStar.UnitTest`: 214, `RedStar.UnitTest.Cli`: 16, `RedStar.UnitTest.Controller`: 17) pass, and
`dotnet build RedStar.slnx` is clean, as of the changes described below.

## Disposition of every review #1 finding

| # | Finding | Disposition |
|---|---|---|
| 1 | `HostedTools` dictionary keys case-sensitive | **Fixed** |
| 2 | Hardcoded per-tool `if` chain, won't scale | **Fixed** |
| 3 | `RawRepresentationFactory` single-slot landmine | **Fixed** |
| 4 | No validation that hosted + custom tools combine | **Resolved by research -- no code change** |
| 5 | Repetitive `double`→`float` casts, no range validation | **Deliberately not changed** |
| 6 | `UrlContext` type/constant name collision | **Fixed** |
| 7 | Thought-signature claim never tested against real wire format | **Fixed** |
| 8 | Zero testing against a live Gemini endpoint | **Cannot fix in this environment** |
| 9 | `appsettings.local.json` drifted from the growing template | **Fixed (clarified, not expanded)** |

### 1. `HostedTools` case-sensitivity -- Fixed

`GoogleAIAgentOptions.HostedTools` (`src/RedStar.Base/RedStarOptions.cs`) is now built with
`GoogleAIHostedTools.Known.ToDictionary(name => name, _ => false, StringComparer.OrdinalIgnoreCase)`
instead of the default ordinal comparer. Re-ran the exact empirical probe from review #1 (temporary
test binding `{"HostedTools": {"googleSearch": true}}` via a real `ConfigurationBuilder`, not
constructed directly in C#) -- it now correctly produces `GoogleSearch=True, CodeExecution=False,
UrlContext=False` (three entries, `googleSearch` merged into the existing `GoogleSearch` key) instead of
the four-entry duplicate-key result from review #1. That probe is no longer a throwaway: it's now a
permanent test, `GoogleAIHostedToolsBindingTests.HostedTools_BindsCaseInsensitively_RegardlessOfConfiguredKeyCasing`
(`src/RedStar.UnitTest.Cli/GoogleAIHostedToolsBindingTests.cs`), parameterized over `"googleSearch"`,
`"GOOGLESEARCH"`, and `"GoogleSearch"`. It lives in `RedStar.UnitTest.Cli` rather than
`RedStar.UnitTest` because exercising real config binding needs
`Microsoft.Extensions.Configuration.Binder`, which only `RedStar.Cli` references -- consistent with
this repo's project-split rule (it tests `RedStarOptions` binding, not any `RedStar.Cli` type, but the
package boundary forces it there).

Also added `GoogleAIAgentOptions_HostedTools_DefaultLookupIsCaseInsensitive` and
`CreateChatOptions_HostedToolLookup_IsCaseInsensitive_EvenWithCustomDictionary` in
`GoogleAIAgentFactoryTests.cs` covering the default dictionary's comparer directly and a
caller-constructed dictionary with a different (but still case-insensitive) comparer.

### 2 & 3. Hardcoded `if` chain / `RawRepresentationFactory` landmine -- Fixed together

`GoogleAIHostedTools.cs` now exposes two lookup tables instead of bare name constants:

- `MappedTools : IReadOnlyDictionary<string, Func<AITool>>` -- hosted tools with a native
  `Microsoft.Extensions.AI` marker (`GoogleSearch` → `HostedWebSearchTool`, `CodeExecution` →
  `HostedCodeInterpreterTool`), both dictionaries keyed with `StringComparer.OrdinalIgnoreCase`.
- `NativeOnlyTools : IReadOnlyDictionary<string, Func<Google.GenAI.Types.Tool>>` -- hosted tools with no
  `Microsoft.Extensions.AI` equivalent (`UrlContext` today).
- `Known` is now derived from the two tables (`[.. MappedTools.Keys, .. NativeOnlyTools.Keys]`) instead
  of being a third, independently-maintained list.

`GoogleAIAgentFactory.CreateChatOptions` replaced the three hardcoded `if` blocks with one
`foreach (var (name, enabled) in googleAI.HostedTools)` loop that looks each enabled name up in both
tables. Adding a future hosted tool is now a one-line dictionary entry in whichever table fits, not a
new branch here. The `RawRepresentationFactory` landmine is addressed the same way: every enabled
`NativeOnlyTools` entry accumulates into one `List<Tool>` first, and `RawRepresentationFactory` is set
**once**, after the loop, from that accumulated list -- so a second native-only tool added later
extends the same list rather than needing its own `if` block that would silently overwrite the first
one's factory. The method's doc comment now states explicitly, in bold, that this is the only place in
the GoogleAI agent that touches `RawRepresentationFactory` and that a future raw-config need must extend
this same list.

Coverage: `GoogleAIHostedToolsTests.cs` (new, `RedStar.UnitTest`) asserts `Known`'s derivation and
no-duplicates invariant, table membership, case-insensitive lookup on both tables directly, and that
each factory produces the right concrete type. The existing `CreateChatOptions_AddsHostedWebSearchTool_*`
/ `CreateChatOptions_AddsHostedCodeInterpreterTool_*` / `CreateChatOptions_SetsRawRepresentationFactory_*`
/ `CreateChatOptions_CombinesAllHostedTools_*` tests from review #1 all still pass unmodified against
the refactored implementation -- confirms the refactor is behavior-preserving, not just
differently-organized.

### 4. Hosted tools + custom function tools combination -- resolved by research, no code change

Review #1 flagged (as a hedge, not a confirmed bug -- "this was never tested against a live API... it's
speculative") that Gemini might reject combining built-in tools with custom function declarations in one
request, and that `CreateChatOptions` has no guard against it. Researched this directly: Google's own
documentation (["Combine built-in tools and function calling"](https://ai.google.dev/gemini-api/docs/tool-combination),
Gemini API docs) confirms this combination **is supported** by current Gemini models via "tool context
circulation," which explicitly preserves and shares context between built-in tool calls and custom
function calls in the same interaction. Adding a defensive guard/exception for a combination that is
actually valid would have been a wrong fix -- it would have blocked legitimate use of the `tools`
injection point together with `HostedTools`, for no real benefit. No code change made; this line item is
closed by evidence rather than by a diff.

### 5. Repetitive `(float?)` casts, no range validation -- deliberately not changed

Re-examined this against the codebase's own stated policy (this session's system instructions: "Don't
add error handling, fallbacks, or validation for scenarios that can't happen... Only validate at system
boundaries"). Gemini's valid ranges for `Temperature`/`TopP`/etc. are model-dependent and change across
model versions -- hardcoding a range check in RedStar risks being wrong or stale in a way that actively
blocks valid requests to a newer model, and the true validation boundary is Gemini's own API, which
already returns a clear error for an out-of-range value. Adding client-side range validation would be
scope creep this codebase's own conventions argue against, not a fix. The `double`→`float` casts stay:
extracting a one-line private helper for six call sites was considered and rejected as adding an
abstraction for a trivial, non-repeating-elsewhere pattern rather than removing real duplication.

### 6. `UrlContext` name collision -- Fixed

`GoogleAIHostedTools.cs` now imports `Google.GenAI.Types.Tool`/`Google.GenAI.Types.UrlContext` under
aliases (`GenAITool`, `GenAIUrlContext`) rather than an unqualified `using Google.GenAI.Types;`. The
factory `static () => new GenAITool { UrlContext = new GenAIUrlContext() }` now reads unambiguously next
to `GoogleAIHostedTools.UrlContext` (the `string` constant) in the same file, instead of relying on a
reader to track which bare `UrlContext` token means which thing. `GoogleAIAgentFactory.cs` no longer
references the SDK's `UrlContext` type at all (it moved into `GoogleAIHostedTools.cs` along with the
mapping table), which also shrinks that file's own collision surface.

### 7. Thought-signature preservation untested against real wire format -- Fixed

Added `GoogleAIThoughtSignaturePreservationTests.cs` (new, `RedStar.UnitTest`), which builds the same
`Client`/`IChatClient` construction `GoogleAIAgentFactory.Create` uses, against the shared
`RedStar.UnitTest.Fakes.FakeHttpMessageHandler` (reused rather than duplicating a bespoke capturing
handler -- an explicit fix for a smell this second pass would otherwise have introduced), and inspects
the literal outgoing request body:

- `PriorThoughtSignature_IsSentVerbatim_OnTheFollowUpRequest`: a hand-built history containing a
  `FunctionCallContent` immediately followed by a `TextReasoningContent("") { ProtectedData = <base64> }`
  results in an outgoing request body containing `"thoughtSignature":"<that exact base64>"`.
- `FunctionCall_WithNoPriorReasoning_StillSendsAValidRequest_UsingSkipValidationPlaceholder`: the same
  history with the reasoning content omitted still sends *some* `"thoughtSignature"` (the SDK's
  skip-validation placeholder), and it is provably not the specific signature from the first test.

Both passed on first run. This is now a real, CI-enforced guarantee instead of a claim resting on
reading `GoogleGenAIChatClient.AddPartsForAIContents`'s source on GitHub.

### 8. No testing against a live Gemini endpoint -- cannot fix here

Unchanged: no Gemini API key is configured in this environment (`appsettings.local.json`'s `GoogleAI`
section still has an empty `ApiKey`), so nothing in this branch has been exercised against
`generativelanguage.googleapis.com`. Finding #7's new tests substantially reduce how much this matters
for the thought-signature claim specifically (that mechanism is now verified against the real request
*shape*, just not a real server), but "the wire format we send is correct" and "Gemini accepts what we
send and behaves as documented" remain two different claims, and only the first is now backed by a test
that runs in this environment.

### 9. `appsettings.local.json` drift -- Fixed (clarified, not expanded)

Considered adding all twelve `GoogleAI` keys to `appsettings.local.json` to match `appsettings.json`'s
template, and rejected it: that file's purpose is machine-specific overrides (it holds a real API key
placeholder), not a duplicate of the template, and most of the newer fields (`ThinkingEffort`, the eight
inference parameters, `HostedTools`) have no machine-specific reason to differ from the checked-in
defaults. Instead added an explicit comment above the `GoogleAI` block stating which fields are
intentionally omitted and that they inherit `appsettings.json`'s defaults -- so the omission reads as a
documented decision on re-read, not as something that fell behind by accident (which is what review #1
actually found: three separate commits grew the template without anyone revisiting whether the local
file's minimalism was still the right call).

## New findings from reviewing the fixes themselves

### 10. (Minor, not fixed) Duplicate hosted-tool entries are possible if a caller hand-builds a mismatched dictionary

`CreateChatOptions`'s `foreach` loop matches each `HostedTools` key against `MappedTools`/
`NativeOnlyTools` using *those tables'* case-insensitive comparers, independent of whatever comparer the
caller's `HostedTools` dictionary itself uses. A caller who bypasses `GoogleAIAgentOptions`'s default
(case-insensitive) dictionary and hand-constructs one with an ordinal comparer containing both
`"GoogleSearch"` and `"googlesearch"` as separate `true` entries would get `HostedWebSearchTool` added
to `ChatOptions.Tools` twice. This requires deliberately misusing the option type in a way normal config
binding or the documented construction pattern never produces (config binding merges into the *existing*
case-insensitive dictionary, per finding #1's fix), so it's noted here rather than fixed -- a real fix
would mean re-validating/de-duplicating the dictionary in `CreateChatOptions` itself, which is defensive
code against a self-inflicted misuse rather than a real input this codebase's own conventions call for
guarding.

Everything else reviewed in the diff for findings #1-#3/#6/#7 (`RedStarOptions.cs`,
`GoogleAIAgentFactory.cs`, `GoogleAIHostedTools.cs`, and the four test files) held up under a second
pass: no new duplication, no unused code, no leftover scratch files, and the `Known`/`MappedTools`/
`NativeOnlyTools` derivation has its own test coverage rather than being trusted by inspection alone.
