# Self-review: GoogleAI / Google.GenAI SDK work (PR #45)

Author's own review of everything landed on `feature/google-genai-sdk` so far: the SDK swap,
inference-parameter config, CLI thinking-mode flags, the client-tool injection point, and the
hosted-tools config. Findings are ranked by how much they'd actually bite someone, not by how many
lines they touch.

## Confirmed bugs

### 1. `HostedTools` dictionary keys are case-sensitive, but nothing else in this codebase is

`GoogleAIAgentOptions.HostedTools` is a plain `Dictionary<string, bool>`, and `.NET` config binding
merges JSON object keys into it **verbatim**, using the default ordinal string comparer. Every other
matched-against-config string in this codebase (`RedStarOptions.Agent`, `GoogleAIAgentOptions
.ThinkingEffort`, `ClaudeCodeAgentOptions.AuthMode`/`ProcessMode`) is explicitly matched
case-insensitively, and `CLAUDE.md` calls this out as deliberate house style. `HostedTools` quietly
breaks that pattern.

Verified empirically (temporary test, not committed): binding

```json
"HostedTools": { "googleSearch": true }
```

against a fresh `RedStarOptions` produces a dictionary with **four** entries --
`GoogleSearch=False, CodeExecution=False, UrlContext=False, googleSearch=True` -- a brand new key
alongside the untouched default, rather than overriding it. `CreateChatOptions` then reads
`HostedTools.GetValueOrDefault(GoogleAIHostedTools.GoogleSearch)` (the PascalCase constant), which is
still `false`. A user who writes `"googleSearch": true` -- the natural camelCase spelling, and the same
casing Gemini's own REST API uses for its field names -- silently gets no tool enabled and no error.

**Fix**: construct the dictionary with `StringComparer.OrdinalIgnoreCase`
(`new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)`), which also fixes lookups from a
future CLI flag or a hand-edited config entry with different casing. No test in
`GoogleAIAgentFactoryTests.cs` or `RedStarOptionsTests.cs` catches this -- every test constructs the
dictionary with exact-matching C# constants, never through actual `IConfiguration` binding, so the gap
went unnoticed until this review.

## Design smells

### 2. Hosted-tool mapping is a hardcoded `if` chain that will get uglier with every addition

`CreateChatOptions` has three near-identical `if (googleAI.HostedTools.GetValueOrDefault(...))`
blocks, one per known tool, and the `UrlContext` case additionally requires special-casing
`RawRepresentationFactory` because it has no `Microsoft.Extensions.AI` equivalent. The user's ask was
explicitly open-ended ("...but similar things they offer out of the box"), and Gemini exposes several
more (`GoogleMaps`, `ComputerUse`, `EnterpriseWebSearch`, `FileSearch`/`Retrieval`) that weren't
included in this pass. Each new one means another `if` block here, another constant in
`GoogleAIHostedTools`, another dictionary key -- linear, copy-pasted growth rather than a small
declarative table (e.g. `IReadOnlyDictionary<string, Func<AITool?>>` for the mapped ones, one
`RawRepresentationFactory` merge step for everything unmapped). Not urgent at three tools, but this is
exactly the kind of code that should have been shaped for the next five before shipping the first
three, given the user explicitly asked for extensibility.

### 3. `RawRepresentationFactory` is a single slot, and this code claims it outright

`CreateChatOptions` sets `chatOptions.RawRepresentationFactory` unconditionally whenever `UrlContext`
is enabled, with a lambda that **always returns a brand-new `GenerateContentConfig`**. That's fine
today because nothing else in this codebase uses `RawRepresentationFactory` for GoogleAI. But it's a
landmine for whoever adds the next raw-config need (safety settings, cached-content references,
anything else not modeled by `Microsoft.Extensions.AI`) -- they'll either have to know to thread their
config through this exact lambda or silently clobber the `UrlContext` tool entry. There's no comment
at the call site warning about this, only in `CreateChatOptions`'s own doc comment, which someone
adding an unrelated raw-config need has no reason to read first.

### 4. No client-side validation that hosted tools and function-declaration tools can actually be combined

`CreateChatOptions` happily merges `HostedWebSearchTool` + `HostedCodeInterpreterTool` +
caller-supplied `AIFunction`s into one `ChatOptions.Tools` list with zero awareness that Gemini's API
has historically restricted which tool *kinds* can be combined in a single request (built-in tools and
custom function declarations don't mix freely across all model versions). If a future caller enables
`GoogleSearch` and also passes a custom tool through the `tools` injection point, the failure mode is
an opaque 400 from Gemini at request time, not a clear error from RedStar. This was never tested
against a live API (see Untested below), so it's speculative, but it's the kind of gotcha that halfway
justified writing `UnslothTools.Known`-style enumeration/validation in the first place, and it isn't
here.

### 5. Six repetitive `(float?)googleAI.X` casts

```csharp
Temperature = (float?)googleAI.Temperature,
TopP = (float?)googleAI.TopP,
...
FrequencyPenalty = (float?)googleAI.FrequencyPenalty,
PresencePenalty = (float?)googleAI.PresencePenalty,
```

`GoogleAIAgentOptions` stores these as `double?` (matching `ClaudeCodeAgentOptions.MaxBudgetUsd`'s
precedent) while `Microsoft.Extensions.AI.ChatOptions` wants `float?`, so every one of them gets an
inline narrowing cast with silent precision loss and zero range validation -- a user who types
`"Temperature": 5.0` (Gemini's valid range is roughly 0-2) gets no local feedback, only whatever error
message Gemini's API returns. Not wrong, just uninspected: nobody checked whether `double` was the
right storage type to begin with, given the target is always `float`.

### 6. `Google.GenAI.Types.UrlContext` and `GoogleAIHostedTools.UrlContext` share a bare name

`new Tool { UrlContext = new UrlContext() }` sits directly below/near code referencing
`GoogleAIHostedTools.UrlContext` (a `string` constant) and
`googleAI.HostedTools[GoogleAIHostedTools.UrlContext]` (a dictionary lookup) in the same method. They
resolve to completely different things (a type vs. a string constant) purely by C# overload/context
rules, which is fine for the compiler and mildly confusing for a human skimming the diff. A more
distinct constant name (e.g. `GoogleAIHostedTools.UrlContextToolName` or just accepting the type alias
collision as a known wart) would have avoided the need to read carefully to tell them apart.

## Process / laziness

### 7. The thought-signature preservation claim rests entirely on reading SDK source, never on a real request

Every doc comment and commit message in this PR states, as settled fact, that Gemini's thought
signatures survive tool-calling turns because "the `Google.GenAI` SDK's own message-to-request
conversion already looks for the `TextReasoningContent` immediately following a `FunctionCallContent`
in history and reuses its signature." That's true of the SDK source as read on GitHub at the time of
writing, but **no test in this repo ever exercises it against the real wire format** -- there's no
`CapturingHandler`-style test asserting `thoughtSignature` actually appears correctly in an outgoing
request body, the way the old (pre-rewrite) `GoogleAIAgentFactoryTests.cs` did for the OpenAI-compat
path before this PR deleted it. The original wire-format test was traded away for the SDK-swap and
never replaced with an equivalent for the new SDK. This was a deliberate scope call under time
pressure (documented in-conversation as "too risky to get right without more source reading" given the
effort budget), not an oversight, but it means the single most-repeated correctness claim in this PR's
documentation is unverified by anything that runs in CI.

### 8. Zero testing against a live Gemini endpoint, ever

Every PR in this branch's history lists "manual smoke test against a real Gemini API key" as an open
checkbox and never checks it off -- there is no `appsettings.local.json` key configured in this
environment, so nothing in this entire SDK swap (chat, thinking mode, inference parameters, hosted
tools, the `HostedTools` case-sensitivity bug above) has been run against the actual Gemini API even
once. All 232 passing tests are unit tests against mocked/local constructions of `ChatOptions`/
`RedStarOptions`; none of them hit `generativelanguage.googleapis.com`. Reasonable given no key is
available here, but worth being explicit about: "the tests pass" and "this works against real Gemini"
are not the same claim, and only the first one has been demonstrated.

### 9. `appsettings.local.json` was left inconsistent with `appsettings.json`'s template

`appsettings.json` (the checked-in template) lists every `GoogleAI` key including `ThinkingEffort`,
`IncludeThoughts`, all eight inference parameters, and now `HostedTools`. `appsettings.local.json`
(also checked in, meant to mirror the template for local overrides) only carries `BaseUrl`/`ApiKey`/
`DefaultModel` -- none of the newer fields were ever added there across three separate commits that
each extended the template. Each commit message says "this file doesn't repeat every key, consistent
with X," which is true of the *original* file's intent, but the drift was never revisited to check
whether it was still the right call once the template grew to a dozen keys.

## What's solid

For balance: the `ApplyOverrides` "non-blank/non-null wins, `with`-expression preserves everything
else" pattern is followed correctly and has regression tests (`ApplyOverrides_Preserves*`) for every
new field group added in this branch. The `CreateChatOptions` unit tests are genuinely thorough for
what they do cover (default values, explicit overrides, explicit-null passthrough, tool merging,
unrecognized-key tolerance) -- the gaps above are about what's *not* covered, not about the quality of
what is. `ConditionalAuthHandler`/no-auth-mode reasoning was correctly identified as inapplicable to
GoogleAI (Gemini always needs a key) rather than being copy-pasted from Unsloth/LMStudio out of habit.
