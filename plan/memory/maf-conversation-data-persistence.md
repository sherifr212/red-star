# Microsoft Agent Framework: Storing Data Flowing Between Models/Agents and the User

Research report on whether and how Microsoft Agent Framework (MAF) — the
`Microsoft.Agents.AI` library RedStar builds `AIAgent`/`ChatSession` on top of (see
`CLAUDE.md`) — supports persisting the data that flows between a model/agent and the user
across turns, runs, and process restarts. All sources below are Microsoft Learn
(`learn.microsoft.com`) pages, fetched 2026-08-19.

**Short answer: yes.** MAF has a dedicated, layered persistence model for exactly this —
`AgentSession` (conversation state container), `ChatHistoryProvider`/`AIContextProvider`
(what gets stored and how it's injected back), and `AgentSessionStore` (durable storage for
self-hosted, stateless-request scenarios) — plus explicit serialize/deserialize APIs and
security guidance for multi-tenant hosting.

> **Naming note:** current docs consistently use `AgentSession` (`Microsoft.Agents.AI`), not
> `AgentThread`. Live Microsoft Learn search results turned up an `AgentThread` .NET API page
> and a `ChatMessageStore` .NET API page, but both returned HTTP 404 when fetched directly —
> they look like stale/renamed API surface from an earlier preview. Treat `AgentThread` as
> superseded by `AgentSession` and `ChatMessageStore` as superseded by `ChatHistoryProvider`
> unless you find a live page proving otherwise.

## 1. Core building blocks

### `AgentSession` — the conversation state container

`AgentSession` is the object passed into every `RunAsync`/`RunStreamingAsync` call to keep
context between invocations. It's an abstract base class; concrete implementations (created
via `agent.CreateSessionAsync()`) add whatever state a given agent/provider needs — e.g. a
remote conversation id when service-managed history is used.

```csharp
AgentSession session = await agent.CreateSessionAsync();

var first = await agent.RunAsync("My name is Alice.", session);
var second = await agent.RunAsync("What is my name?", session);
```

Sessions can also be created from an existing service-side conversation id (varies by agent
type — `ChatClientAgent.CreateSessionAsync(conversationId)`, `A2AAgent.CreateSessionAsync(contextId, taskId)`).

Source: [Session | Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session)

### Two storage models

| Mode | What is stored | Typical usage |
|---|---|---|
| **Local session state** | Full chat history held client-side, by default via `InMemoryChatHistoryProvider` | Providers/services with no native persistent conversation support |
| **Service-managed storage** | Conversation state lives in the AI service; the session just holds a remote id (e.g. OpenAI `resp_*`/`conv_*`) | Providers with native persistent conversation support (e.g. OpenAI Responses/Conversations) |

For service-managed storage, the doc explicitly warns: those remote ids are opaque and scoped
to the backing API key/project by default — if one hosted app/key serves multiple end users,
you must store the ids server-side and verify caller ownership before resuming, not treat the
id itself as an authorization boundary.

```csharp
// Service-managed example — casting to inspect the remote id
ChatClientAgentSession typedSession = (ChatClientAgentSession)session;
Console.WriteLine(typedSession.ConversationId);
```

```csharp
// Local in-memory example — reading history back out of the session
var provider = agent.GetService<InMemoryChatHistoryProvider>();
List<ChatMessage>? messages = provider?.GetMessages(session);
```

Source: [Storage | Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/storage)

### `ChatHistoryProvider` — what conversation messages get stored, and where

`Microsoft.Agents.AI.ChatHistoryProvider` is the base class for pluggable message history.
It participates in the agent pipeline (`InvokingCoreAsync`/`InvokedCoreAsync`, or the simpler
`ProvideChatHistoryAsync`/`StoreChatHistoryAsync` override points) and can store/load history
against any backend — a database, Redis, blob storage, or the built-in in-memory store.
Custom implementations must **not** keep session-specific state on the provider instance
itself (one provider instance is shared across all sessions); instead they store per-session
data — e.g. a database key — inside the `AgentSession` via a `ProviderSessionState<T>` helper.

RedStar already uses this exact class: `CLAUDE.md` notes `ChatSession` relies on
`InMemoryChatHistoryProvider` (the framework default) and reads messages back via
`AgentSessionExtensions.TryGetInMemoryChatHistory`.

```csharp
AIAgent agent = new OpenAIClient("<your_api_key>")
    .GetChatClient(modelName)
    .AsAIAgent(new ChatClientAgentOptions
    {
        Name = "Assistant",
        ChatOptions = new() { Instructions = "You are a helpful assistant." },
        ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
        {
            ChatReducer = new MessageCountingChatReducer(20) // cap history growth
        })
    });
```

Source: [Storage | Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/storage)
(custom-provider section), [Step 4: Memory & Persistence | Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/get-started/memory)

### `AIContextProvider` — memory/personalization beyond raw message history

`Microsoft.Agents.AI.AIContextProvider` is the broader extension point: not just "store the
transcript" but "inject arbitrary extra context before a run, and extract/persist arbitrary
state after a run" — e.g. remembered user preferences, retrieved facts, a pointer to a memory
service. Two override levels, same shape as `ChatHistoryProvider`:

- Simple: `ProvideAIContextAsync` (return extra instructions/messages/tools) +
  `StoreAIContextAsync` (extract data from the turn and persist it).
- Advanced: `InvokingCoreAsync`/`InvokedCoreAsync` for full control over merging/filtering.

Like `ChatHistoryProvider`, a context provider instance is shared across sessions, so any
session-specific state (e.g. a memory-service container id) is stored in the `AgentSession`
itself via the same `ProviderSessionState<T>` helper, not on the provider.

```csharp
AIAgent agent = new OpenAIClient("<your_api_key>")
    .GetChatClient(modelName)
    .AsAIAgent(new ChatClientAgentOptions()
    {
        ChatOptions = new() { Instructions = "You are a helpful assistant." },
        AIContextProviders = [ new MyCustomMemoryProvider() ],
    });
```

Source: [Context Providers | Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/context-providers)

## 2. Serialization — persisting across process restarts

Regardless of storage mode, `AgentSession` supports explicit serialize/deserialize so an
in-process conversation can survive a restart:

```csharp
JsonElement serialized = agent.SerializeSession(session);
// Store serialized payload in durable storage (your own DB/blob/file).
AgentSession resumed = await agent.DeserializeSessionAsync(serialized);
```

Guidance: treat `AgentSession` as an **opaque** state object; persist and restore it with the
same agent/provider configuration that created it, since sessions are agent/service-specific.
Store the whole serialized session, not just message text — depending on configuration it may
carry more than chat history (memory/context-provider state, a service conversation id, etc.).

Source: [Session | Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session),
[Storage | Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/storage)

## 3. `AgentSessionStore` — durable storage for stateless hosts (ASP.NET Core, etc.)

For request/response hosting (a web API, serverless function) where nothing survives between
HTTP calls in memory, `Microsoft.Agents.AI.Hosting`'s `AgentSessionStore` lets the host
load/save an `AgentSession` by an opaque *continuation id* supplied per-request:

```csharp
public sealed class MyAgentSessionStore : AgentSessionStore
{
    public override ValueTask SaveSessionAsync(
        AIAgent agent, string sessionStoreId, AgentSession session,
        CancellationToken cancellationToken = default) { /* persist */ throw new NotImplementedException(); }

    public override ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent, string sessionStoreId,
        CancellationToken cancellationToken = default) { /* restore or create */ throw new NotImplementedException(); }

    public override ValueTask DeleteSessionAsync(
        AIAgent agent, string sessionStoreId,
        CancellationToken cancellationToken = default) { /* delete */ throw new NotImplementedException(); }
}
```

Key points from the doc:

- MAF ships **no general-purpose durable store** — `InMemoryAgentSessionStore` exists only for
  local/dev use and loses everything on process exit / doesn't share state across instances.
  Production needs a real backing store you implement.
- A saved session can hold more than chat messages: service-managed conversation id,
  framework-managed chat history, memory/context-provider state, queued messages, pending
  approvals, etc. — "persist the complete `AgentSession`."
- **Security**: a continuation id proves nothing about ownership. `IsolationKeyScopedAgentSessionStore` /
  `AgentIsolationKeyProvider` combine an authenticated-caller isolation key (e.g. a claims-based
  user id via `UseClaimsBasedAgentIsolation()`) with the raw continuation id, so two different
  users presenting the same id resolve to two different stored sessions. This is opt-in but the
  doc treats it as required for any multi-tenant hosted scenario.
- The `Microsoft.Agents.AI.Hosting`/`Microsoft.Agents.AI.Hosting.AspNetCore` packages are
  explicitly **prerelease** — review release notes before using in production.

Source: [Self-host Agent Framework applications | Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting/)

## 4. Workflow state (multi-agent / multi-executor data sharing)

Separately from per-conversation session storage, MAF **Workflows** have their own
`QueueStateUpdateAsync`/`ReadStateAsync` scoped key-value state mechanism for sharing data
*between executors/agents inside a single workflow run* (not user-facing conversation data
per se, but relevant if RedStar ever grows a multi-agent workflow):

```csharp
// Executor A: write to a shared scope
await context.QueueStateUpdateAsync("Response", blanketResponse, scopeName: "SharedResponse", cancellationToken);

// Executor B: read from that same scope
var finalResponse = await context.ReadStateAsync<string>("Response", scopeName: "SharedResponse", cancellationToken);
```

Notable caveat: **agent state inside a workflow is managed via each agent's own session**, and
those sessions are persisted *across workflow runs* by default — i.e., if the same workflow
instance is reused for a second, unrelated task, the agent will still "remember" the first
task's conversation unless you deliberately construct a fresh workflow/agent/session per task
(the doc's recommended pattern is a `createWorkflow()`-style factory function).

Source: [Microsoft Agent Framework Workflows - State | Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/workflows/state)

## 5. Framework-level positioning

The framework overview page frames "session-based state management" as one of the four
headline building blocks (alongside model clients, context providers, and MCP clients), and
describes it as inherited from Semantic Kernel's enterprise-state-management heritage —
i.e. this isn't a bolt-on feature, it's a first-class part of MAF's design.

Source: [Microsoft Agent Framework Overview | Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/overview/)

## 6. How this maps onto RedStar today

- `ChatSession` (`RedStar.Base`) already rides the framework default: an `AIAgent`-owned
  `InMemoryChatHistoryProvider`, read back via `AgentSessionExtensions.TryGetInMemoryChatHistory`
  — i.e. RedStar is currently on the "local session state" storage mode with no custom
  `ChatHistoryProvider`/`AIContextProvider` and no `AgentSessionStore` (RedStar is a CLI, not a
  stateless web host, so nothing forces that yet).
- If RedStar ever needs to survive process restarts (e.g. resume a chat across `redstar` CLI
  invocations) or add durable "remember this about me" behavior, the natural extension points
  are, in order of how much RedStar has to build itself:
  1. `agent.SerializeSession(session)` / `DeserializeSessionAsync` to a local file — cheapest,
     no new abstractions.
  2. A custom `ChatHistoryProvider` (e.g. SQLite/JSON-file backed) if history needs to outlive
     a single `ChatSession` instance across runs, matching the "Third-party/Custom storage
     pattern" shown above.
  3. A custom `AIContextProvider` if the goal is closer to "remember facts about the user"
     than "replay the whole transcript" (personalization/memory rather than history replay).
  4. `AgentSessionStore` only becomes relevant if RedStar grows a hosted/web mode (it already
     has `RedStar.WebApp`) serving multiple users statelessly between requests — at that point
     the isolation-key guidance above is directly applicable.

## Sources (Microsoft Learn only)

- [Conversations & Memory overview in Agent Framework](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/)
- [Session](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session)
- [Storage](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/storage)
- [Context Providers](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/context-providers)
- [Step 4: Memory & Persistence (get-started tutorial)](https://learn.microsoft.com/en-us/agent-framework/get-started/memory)
- [Self-host Agent Framework applications](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting/)
- [Microsoft Agent Framework Workflows - State](https://learn.microsoft.com/en-us/agent-framework/workflows/state)
- [Microsoft Agent Framework Overview](https://learn.microsoft.com/en-us/agent-framework/overview/)

### Checked but returned HTTP 404 (likely stale/renamed API surface — not cited above)

- `https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.agentthread` — `AgentThread`
  appears superseded by `AgentSession`.
- `https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.chatmessagestore` —
  `ChatMessageStore` appears superseded by `ChatHistoryProvider`.
- `https://learn.microsoft.com/en-us/agent-framework/agents/conversations/chat-history-memory-provider` —
  linked from the conversations overview's guide-map table but not resolvable directly.
