using JasperFx;

using Marten;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace RedStar.Base.Agents.GoogleAI;

public sealed class DbGoogleAIContextProvider : AIContextProvider
{
    private readonly IDocumentStore _documentStore;

    public DbGoogleAIContextProvider(IDocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    protected override async ValueTask<AIContext> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        await using (var dbSession = _documentStore.LightweightSession(System.Data.IsolationLevel.ReadCommitted)) // Since we are on Postgresql.
        
        {
            var part = new InferenceRequestPart
            {
                SessionStateBag = context.Session?.StateBag,
                SessionType = context.Session?.GetType().ToString(),

                AgentDescription = context.Agent.Description,
                AgentId = context.Agent.Id,
                AgentIdName = context.Agent.Name,
                AgentType = context.Agent.GetType().ToString(), // Might be costly!
                
                ContextInstructions = context.AIContext.Instructions,
                ContextMessages = context.AIContext.Messages,
                ContextTools = context.AIContext.Tools
            };

            dbSession.Insert(part);
            await dbSession.SaveChangesAsync(cancellationToken);
        }

        return await base.InvokingCoreAsync(context, cancellationToken);
    }

    protected override async ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        await using (var dbSession = _documentStore.LightweightSession(System.Data.IsolationLevel.ReadCommitted)) // Since we are on Postgresql.

        {
            var part = new InferenceResponsePart
            {
                SessionStateBag = context.Session?.StateBag,
                SessionType = context.Session?.GetType().ToString(),

                AgentDescription = context.Agent.Description,
                AgentId = context.Agent.Id,
                AgentIdName = context.Agent.Name,
                AgentType = context.Agent.GetType().ToString(), // Might be costly!

                RequestMessages = context.RequestMessages,
                ResponseMessages = context.ResponseMessages,
                
                InvokeExceptionType = context.InvokeException?.GetType().ToString(),
                InvokeExceptionMessage = context.InvokeException?.Message,
                InvokeExceptionStackTrace = context.InvokeException?.ToString()
            };

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
    public AgentSessionStateBag? SessionStateBag { get; init; }

    public string? AgentDescription { get; init; }
    public required string AgentId { get; init; }
    public string? AgentIdName { get; init; }
    public required string AgentType { get; init; }

    public string? ContextInstructions { get; init; }
    public IEnumerable<ChatMessage>? ContextMessages { get; init; }
    public IEnumerable<AITool>? ContextTools { get; init; }
}

public record InferenceResponsePart
{
    [Identity]
    public Guid Id { get; init; }
    public DateTimeOffset AtUtc { get; } = DateTimeOffset.UtcNow;

    public string? SessionType { get; init; }
    public AgentSessionStateBag? SessionStateBag { get; init; }

    public string? AgentDescription { get; init; }
    public required string AgentId { get; init; }
    public string? AgentIdName { get; init; }
    public required string AgentType { get; init; }

    public IEnumerable<ChatMessage>? RequestMessages { get; init; }
    public IEnumerable<ChatMessage>? ResponseMessages { get; init; }
    public string? InvokeExceptionType { get; init; }
    public string? InvokeExceptionMessage { get; init; }
    public string? InvokeExceptionStackTrace { get; init; }
}