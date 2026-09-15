using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Approval;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Conflicts;
using TeamPilot.Application.Instructions;
using TeamPilot.Application.InstructionTemplates;
using TeamPilot.Application.LiveAgentChat;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.TicketAgentEvents;
using TeamPilot.Application.TicketQuestions;
using TeamPilot.Application.Users;
using TeamPilot.Application.Workflow;

namespace TeamPilot.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<Tickets.Validators.CreateTicketRequestValidator>();

        services.AddScoped<IProjectAccessGuard, ProjectAccessGuard>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IUserService, UserService>();

        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<IInstructionService, InstructionService>();
        services.AddScoped<ILiveAgentChatService, LiveAgentChatService>();
        services.AddScoped<IInstructionTemplateService, InstructionTemplateService>();
        services.AddScoped<IOrchestrationService, OrchestrationService>();
        services.AddScoped<IApprovalGateService, ApprovalGateService>();
        services.AddScoped<ITicketQuestionService, TicketQuestionService>();
        services.AddScoped<ITicketAgentEventService, TicketAgentEventService>();
        services.AddScoped<IConflictResolutionService, ConflictResolutionService>();
        services.AddScoped<IWorkflowService, WorkflowService>();

        return services;
    }
}
