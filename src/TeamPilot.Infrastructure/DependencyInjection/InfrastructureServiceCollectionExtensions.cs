using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamPilot.Application.Agents;
using TeamPilot.Application.AuditLog;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Commits;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Conflicts;
using TeamPilot.Application.Git;
using TeamPilot.Application.Instructions;
using TeamPilot.Application.InstructionTemplates;
using TeamPilot.Application.LiveAgentChat;
using TeamPilot.Application.Llm;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Reviews;
using TeamPilot.Application.TicketAgentEvents;
using TeamPilot.Application.TicketQuestions;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Users;
using TeamPilot.Application.Workflow;
using TeamPilot.Infrastructure.Auth;
using TeamPilot.Infrastructure.BackgroundTasks;
using TeamPilot.Infrastructure.Git;
using TeamPilot.Infrastructure.Llm;
using TeamPilot.Infrastructure.Persistence;
using TeamPilot.Infrastructure.RealTime;
using TeamPilot.Infrastructure.Repositories;

namespace TeamPilot.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        services.AddDbContext<TeamPilotDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IWorkflowStageRepository, WorkflowStageRepository>();
        services.AddScoped<IStageExecutionRepository, StageExecutionRepository>();
        services.AddScoped<ITicketQuestionRepository, TicketQuestionRepository>();
        services.AddScoped<ITicketAgentEventRepository, TicketAgentEventRepository>();
        services.AddScoped<IInstructionRepository, InstructionRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IInstructionTemplateRepository, InstructionTemplateRepository>();
        services.AddScoped<ICommitRepository, CommitRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IConflictRepository, ConflictRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();

        services.Configure<GitOptions>(configuration.GetSection(GitOptions.SectionName));
        services.AddScoped<IGitService, LibGit2SharpGitService>();
        services.AddDataProtection();
        services.AddSingleton<IGitCredentialProtector, DataProtectionGitCredentialProtector>();

        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        AddLlmConnector(services, configuration);

        services.Configure<AuthSettings>(configuration.GetSection(AuthSettings.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<Dictionary<string, ExternalProviderConfig>>(configuration.GetSection("Auth:Providers"));

        services.AddSingleton<IExternalIdentityValidator, ExternalIdentityValidator>();
        services.AddSingleton<IAccessTokenGenerator, AccessTokenGenerator>();
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();

        services.AddSingleton<IBackgroundTaskRunner, BackgroundTaskRunner>();
        services.AddSingleton<IPipelineRunTracker, PipelineRunTracker>();
        services.AddSingleton<IProjectEventBroadcaster, ProjectEventBroadcaster>();

        return services;
    }

    private static void AddLlmConnector(IServiceCollection services, IConfiguration configuration)
    {
        var llmSection = configuration.GetSection(LlmOptions.SectionName);
        var provider = llmSection["Provider"] ?? "Claude";

        switch (provider)
        {
            case "Claude":
                var timeoutSeconds = llmSection.GetValue("TimeoutSeconds", 60);
                services.AddHttpClient<ILlmConnector, ClaudeLlmConnector>()
                    .AddStandardResilienceHandler(resilience =>
                    {
                        // A single completion can legitimately take up to Llm:TimeoutSeconds, so the
                        // attempt timeout must match it (the library default of 10s would otherwise
                        // make every slow-but-successful call retry pointlessly). The circuit
                        // breaker's sampling window is required to be at least 2x the attempt
                        // timeout, and the total budget must cover every retry attempt.
                        var attemptTimeout = TimeSpan.FromSeconds(timeoutSeconds);
                        resilience.AttemptTimeout.Timeout = attemptTimeout;
                        resilience.CircuitBreaker.SamplingDuration = attemptTimeout * 2;
                        resilience.TotalRequestTimeout.Timeout = attemptTimeout * (resilience.Retry.MaxRetryAttempts + 1);
                    });
                break;
            default:
                throw new NotSupportedException($"LLM provider '{provider}' is not supported. Supported providers: Claude.");
        }
    }
}
