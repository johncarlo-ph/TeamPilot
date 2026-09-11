using FluentValidation;
using Microsoft.Extensions.Logging;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Commits.Dtos;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Llm;
using TeamPilot.Application.Orchestration.Dtos;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Orchestration;

public sealed class OrchestrationService(
    ITicketRepository ticketRepository,
    IAgentRepository agentRepository,
    IProjectRepository projectRepository,
    IGitService gitService,
    IGitCredentialProtector credentialProtector,
    ILlmConnector llmConnector,
    IProjectAccessGuard projectAccessGuard,
    IUnitOfWork unitOfWork,
    IValidator<AssignSubAgentsRequest> assignSubAgentsValidator,
    ILogger<OrchestrationService> logger) : IOrchestrationService
{
    public async Task<TicketDto> AssignSubAgentsAsync(Guid ticketId, AssignSubAgentsRequest request, CancellationToken cancellationToken = default)
    {
        await assignSubAgentsValidator.EnsureValidAsync(request, cancellationToken);

        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        foreach (var agentId in request.AgentIds)
        {
            var agent = await agentRepository.GetByIdAsync(agentId, cancellationToken)
                ?? throw new NotFoundException(nameof(Agent), agentId);

            EnsureAgentBelongsToTicketProject(agent, ticket);
            ticket.AssignAgent(agent);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TicketMappings.ToDto(ticket);
    }

    public async Task<AgentWorkResultDto> ExecuteAgentWorkAsync(Guid ticketId, Guid agentId, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        var agent = await agentRepository.GetByIdAsync(agentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        EnsureAgentBelongsToTicketProject(agent, ticket);

        var prompt = $"You are the {agent.Role} agent working on ticket '{ticket.Title}'. Description: {ticket.Description}";
        var llmResponse = await llmConnector.SendPromptAsync(new LlmRequest(prompt), cancellationToken);

        CommitDto? commitDto = null;

        if (agent.Role == AgentRole.Coding)
        {
            commitDto = await CommitAgentWorkAsync(ticket, agent, llmResponse.Content, cancellationToken);
        }
        else
        {
            logger.LogInformation(
                "Agent {AgentId} ({Role}) produced output for ticket {TicketId} in project {ProjectId}: {Output}",
                agent.Id,
                agent.Role,
                ticket.Id,
                ticket.ProjectId,
                llmResponse.Content);
        }

        return new AgentWorkResultDto(ticket.Id, agent.Id, agent.Role, llmResponse.Content, commitDto);
    }

    private static void EnsureAgentBelongsToTicketProject(Agent agent, Ticket ticket)
    {
        if (agent.ProjectId != ticket.ProjectId)
        {
            throw new InvalidOperationException($"Agent '{agent.Id}' does not belong to project '{ticket.ProjectId}'.");
        }
    }

    private async Task<CommitDto> CommitAgentWorkAsync(Ticket ticket, Agent agent, string llmOutput, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticket.BranchName))
        {
            throw new InvalidOperationException("Ticket must have a linked branch before a coding agent can commit.");
        }

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

        var message = $"{agent.Role} agent work for '{ticket.Title}'";
        var relativeFilePath = $"tickets/{ticket.Id}.md";

        var commitResult = await gitService.CommitFileAsync(
            project.RepositoryPath,
            ticket.BranchName,
            relativeFilePath,
            llmOutput,
            message,
            agent.Name,
            cancellationToken);

        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);
        await gitService.PushAsync(project.RepositoryPath, ticket.BranchName, accessToken, cancellationToken);

        var commit = Commit.Create(ticket.Id, ticket.BranchName, commitResult.CommitHash, message, commitResult.DiffContent, agent.Id);
        ticket.AddCommit(commit);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommitDto(commit.Id, commit.TicketId, commit.BranchName, commit.CommitHash, commit.Message, commit.DiffContent, commit.AuthorAgentId, commit.CreatedAtUtc);
    }
}
