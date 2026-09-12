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
using TeamPilot.Application.Llm;
using TeamPilot.Application.Pipelines;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Reviews;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Users;
using TeamPilot.Infrastructure.Auth;
using TeamPilot.Infrastructure.Git;
using TeamPilot.Infrastructure.Llm;
using TeamPilot.Infrastructure.Persistence;
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
        services.AddScoped<IInstructionRepository, InstructionRepository>();
        services.AddScoped<IInstructionTemplateRepository, InstructionTemplateRepository>();
        services.AddScoped<ICommitRepository, CommitRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IConflictRepository, ConflictRepository>();
        services.AddScoped<IPipelineRunRepository, PipelineRunRepository>();
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

        return services;
    }

    private static void AddLlmConnector(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration.GetSection(LlmOptions.SectionName)["Provider"] ?? "Claude";

        switch (provider)
        {
            case "Claude":
                services.AddHttpClient<ILlmConnector, ClaudeLlmConnector>();
                break;
            default:
                throw new NotSupportedException($"LLM provider '{provider}' is not supported. Supported providers: Claude.");
        }
    }
}
