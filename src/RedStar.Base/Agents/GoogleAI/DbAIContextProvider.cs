using JasperFx;

using Marten;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace RedStar.Base.Agents.GoogleAI;

public sealed class DbAIContextProvider : AIContextProvider
{
    // private readonly string _runId;
    private readonly IDocumentStore _documentStore;

    public DbAIContextProvider(IDocumentStore documentStore)
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(documentStore);

        // _runId = runId;
        _documentStore = documentStore;
    }

    protected override async ValueTask<AIContext> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var newAIContext = await base.InvokingCoreAsync(context, cancellationToken);

        var options = context.Agent.GetService<ChatOptions>();

        var newUserMessages = newAIContext.Messages?.Where(
                x => x.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.ChatHistory)
            ?? [];
        
        var part = new InferenceRequestPart
        {
            SessionType = context.Session?.GetType().ToString(),

            ChatOptions = options,

            AgentDescription = context.Agent.Description,
            AgentId = context.Agent.Id,
            AgentIdName = context.Agent.Name,
            AgentType = context.Agent.GetType().ToString(), // Might be costly!

            ContextInstructions = newAIContext.Instructions,
            ContextMessages = newUserMessages,
            ContextTools = newAIContext.Tools
        };

        await using (var dbSession = _documentStore.LightweightSession(System.Data.IsolationLevel.ReadCommitted)) // Since we are on Postgresql.
        {
            dbSession.Insert(part);
            await dbSession.SaveChangesAsync(cancellationToken);
        }

        return newAIContext;
    }

    protected override async ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        var newResponseMessages = context.ResponseMessages?.Where(
                x => x.GetAgentRequestMessageSourceType() == AgentRequestMessageSourceType.External) 
                ?? [];
        
        var part = new InferenceResponsePart
        {
            SessionType = context.Session?.GetType().ToString(),

            AgentDescription = context.Agent.Description,
            AgentId = context.Agent.Id,
            AgentIdName = context.Agent.Name,
            AgentType = context.Agent.GetType().ToString(), // Might be costly!

            ResponseMessages = newResponseMessages,

            InvokeExceptionString = context.InvokeException?.ToString(),
            InvokeExceptionType = context.InvokeException?.GetType().ToString(),
            InvokeExceptionMessage = context.InvokeException?.Message,
            InvokeExceptionStackTrace = context.InvokeException?.StackTrace,
        };

        await using (var dbSession = _documentStore.LightweightSession(System.Data.IsolationLevel.ReadCommitted)) // Since we are on Postgresql.
        {
            dbSession.Insert(part);
            await dbSession.SaveChangesAsync(cancellationToken);
        }

        await base.InvokedCoreAsync(context, cancellationToken);
    }
}

public record InferenceRequestPart
{
    [Identity]
    public Guid Id { get; init; }
    public DateTimeOffset UtcAt { get; } = DateTimeOffset.UtcNow;

    public string? SessionType { get; init; }

    public string? AgentDescription { get; init; }
    public required string AgentId { get; init; }
    public string? AgentIdName { get; init; }
    public required string AgentType { get; init; }

    public string? ContextInstructions { get; init; }
    public IEnumerable<ChatMessage>? ContextMessages { get; init; }
    public IEnumerable<AITool>? ContextTools { get; init; }
    public ChatOptions? ChatOptions { get; init; }
}

public record InferenceResponsePart
{
    [Identity]
    public Guid Id { get; init; }
    public DateTimeOffset AtUtc { get; } = DateTimeOffset.UtcNow;

    public string? SessionType { get; init; }

    public string? AgentDescription { get; init; }
    public required string AgentId { get; init; }
    public string? AgentIdName { get; init; }
    public required string AgentType { get; init; }

    public IEnumerable<ChatMessage>? ResponseMessages { get; init; }

    public string? InvokeExceptionType { get; init; }
    public string? InvokeExceptionMessage { get; init; }
    public string? InvokeExceptionStackTrace { get; init; }
    public string? InvokeExceptionString { get; init; }
}