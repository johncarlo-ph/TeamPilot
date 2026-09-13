using System.Text.Json;
using FluentValidation;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Instructions;
using TeamPilot.Application.LiveAgentChat.Dtos;
using TeamPilot.Application.Llm;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.LiveAgentChat;

/// <summary>
/// Drives a project's chat with its Live Agent: persists the conversation, then runs a bounded
/// Claude tool-use loop giving the model read-only, sandboxed access to the project's repo
/// files and tickets (list plus per-ticket detail), plus a ticket-drafting tool. See the tool
/// descriptions in <see cref="BuildTools"/> for the behavioral constraints (scoped access,
/// explicit-request-only drafting) enforced through prompting rather than code.
/// </summary>
public sealed class LiveAgentChatService(
    IConversationRepository conversationRepository,
    IAgentRepository agentRepository,
    IAgentService agentService,
    IInstructionRepository instructionRepository,
    IProjectRepository projectRepository,
    ITicketRepository ticketRepository,
    IGitService gitService,
    ILlmConnector llmConnector,
    IProjectAccessGuard projectAccessGuard,
    IUnitOfWork unitOfWork,
    IValidator<SendChatMessageRequest> sendValidator) : ILiveAgentChatService
{
    private const int MaxToolRoundtrips = 6;
    private const int MaxToolResultChars = 8000;

    public async Task<IReadOnlyList<ChatMessageDto>> GetHistoryAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var conversation = await conversationRepository.GetByProjectIdAsync(projectId, cancellationToken);
        return conversation is null ? [] : conversation.Messages.Select(ToDto).ToList();
    }

    public async Task<ChatMessageDto> SendMessageAsync(Guid projectId, SendChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        await sendValidator.EnsureValidAsync(request, cancellationToken);
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), projectId);

        await agentService.EnsureLiveAgentAsync(projectId, cancellationToken);
        var liveAgent = await agentRepository.GetByProjectAndRoleAsync(projectId, AgentRole.LiveAgent, cancellationToken)
            ?? throw new InvalidOperationException($"Project '{projectId}' has no active Live Agent.");

        var conversation = await conversationRepository.GetByProjectIdAsync(projectId, cancellationToken);
        if (conversation is null)
        {
            conversation = Conversation.Create(projectId, liveAgent.Id);
            await conversationRepository.AddAsync(conversation, cancellationToken);
        }

        conversation.AddMessage(ChatMessageRole.User, request.Content);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var instructions = await AgentInstructionsFormatter.GetInstructionsBlockAsync(instructionRepository, liveAgent.Id, cancellationToken);
        var system = AgentInstructionsFormatter.FormatInstructions(instructions) +
            $"Project: {project.Name}\nDescription: {project.Description}";

        var messages = conversation.Messages
            .Select(m => m.Role == ChatMessageRole.User ? LlmMessage.User(m.Content) : LlmMessage.Assistant([new LlmTextBlock(m.Content)]))
            .ToList();

        var tools = BuildTools();

        string? proposedTitle = null;
        string? proposedDescription = null;
        var finalText = "I couldn't finish that within my available steps - could you narrow down the question?";

        for (var round = 0; round < MaxToolRoundtrips; round++)
        {
            var response = await llmConnector.SendConversationAsync(
                new LlmConversationRequest(messages, System: system, Tools: tools),
                cancellationToken);

            if (response.StopReason != "tool_use")
            {
                finalText = response.TextContent;
                break;
            }

            messages.Add(new LlmMessage("assistant", response.Content));

            var toolResults = new List<LlmContentBlock>();
            foreach (var toolUse in response.Content.OfType<LlmToolUseBlock>())
            {
                var (resultText, isError, proposal) = await ExecuteToolAsync(toolUse, project, projectId, cancellationToken);
                if (proposal is not null)
                {
                    (proposedTitle, proposedDescription) = proposal.Value;
                }

                toolResults.Add(new LlmToolResultBlock(toolUse.Id, Truncate(resultText), isError));
            }

            messages.Add(new LlmMessage("user", toolResults));
        }

        conversation.AddMessage(ChatMessageRole.Assistant, finalText, proposedTitle, proposedDescription);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(conversation.Messages.Last());
    }

    private async Task<(string ResultText, bool IsError, (string Title, string Description)? Proposal)> ExecuteToolAsync(
        LlmToolUseBlock toolUse,
        Project project,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        JsonDocument input;
        try
        {
            input = JsonDocument.Parse(string.IsNullOrWhiteSpace(toolUse.InputJson) ? "{}" : toolUse.InputJson);
        }
        catch (JsonException)
        {
            return ("Invalid tool input.", true, null);
        }

        using (input)
        {
            try
            {
                switch (toolUse.Name)
                {
                    case "list_files":
                    {
                        var path = TryGetString(input, "path");
                        var entries = await gitService.ListFilesAsync(project.RepositoryPath, path, cancellationToken);
                        return (entries.Count == 0 ? "(empty)" : string.Join("\n", entries), false, null);
                    }

                    case "read_file":
                    {
                        var path = TryGetString(input, "path") ?? string.Empty;
                        var result = await gitService.ReadFileAsync(project.RepositoryPath, path, cancellationToken);
                        if (!result.Found)
                        {
                            return ("File not found or not accessible.", true, null);
                        }

                        var text = result.Truncated ? $"{result.Content}\n\n[truncated]" : result.Content!;
                        return (text, false, null);
                    }

                    case "list_tickets":
                    {
                        TicketStatus? status = null;
                        var statusText = TryGetString(input, "status");
                        if (statusText is not null && Enum.TryParse<TicketStatus>(statusText, ignoreCase: true, out var parsed))
                        {
                            status = parsed;
                        }

                        var tickets = await ticketRepository.ListAsync(projectId, status, cancellationToken);
                        var text = tickets.Count == 0
                            ? "(no tickets)"
                            : string.Join("\n", tickets.Select(t => $"{t.Id} | {t.Title} | {t.Status}"));
                        return (text, false, null);
                    }

                    case "get_ticket":
                    {
                        var idText = TryGetString(input, "id");
                        if (!Guid.TryParse(idText, out var ticketId))
                        {
                            return ("A valid ticket id is required.", true, null);
                        }

                        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken);
                        if (ticket is null || ticket.ProjectId != projectId)
                        {
                            return ("Ticket not found.", true, null);
                        }

                        var details = $"Title: {ticket.Title}\nStatus: {ticket.Status}\nDescription: {ticket.Description}";
                        if (!string.IsNullOrWhiteSpace(ticket.BranchName))
                        {
                            details += $"\nBranch: {ticket.BranchName}";
                        }

                        if (!string.IsNullOrWhiteSpace(ticket.CancellationReason))
                        {
                            details += $"\nCancellation reason: {ticket.CancellationReason}";
                        }

                        return (details, false, null);
                    }

                    case "propose_ticket":
                    {
                        var title = TryGetString(input, "title");
                        var description = TryGetString(input, "description") ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(title))
                        {
                            return ("A ticket title is required.", true, null);
                        }

                        return ("Drafted for the user to review and approve in the chat.", false, (title, description));
                    }

                    default:
                        return ($"Unknown tool '{toolUse.Name}'.", true, null);
                }
            }
            catch (GitOperationException ex)
            {
                return (ex.Message, true, null);
            }
        }
    }

    private static string? TryGetString(JsonDocument input, string propertyName) =>
        input.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string text) =>
        text.Length > MaxToolResultChars ? text[..MaxToolResultChars] + "\n\n[truncated]" : text;

    private static IReadOnlyList<LlmToolDefinition> BuildTools() =>
    [
        new LlmToolDefinition(
            "list_files",
            "List files and folders directly inside a path within this project's repository (read-only, sandboxed). " +
            "Only call this for a path relevant to the user's current question - do not explore unrelated parts of the repo.",
            """{"type":"object","properties":{"path":{"type":"string","description":"Relative directory path within the repository. Omit or leave empty to list the repository root."}}}"""),
        new LlmToolDefinition(
            "read_file",
            "Read the contents of one file within this project's repository (read-only, sandboxed). " +
            "Only read files that are directly relevant to answering the user's current question.",
            """{"type":"object","properties":{"path":{"type":"string","description":"Relative file path within the repository to read."}},"required":["path"]}"""),
        new LlmToolDefinition(
            "list_tickets",
            "List this project's tickets (id, title, status only), optionally filtered by status, to see what exists " +
            "before answering questions about existing work or drafting a new ticket. Use get_ticket to read the full " +
            "description of any ticket that looks relevant.",
            """{"type":"object","properties":{"status":{"type":"string","enum":["ToDo","InProgress","ForReview","Done","Cancelled"],"description":"Optional status filter."}}}"""),
        new LlmToolDefinition(
            "get_ticket",
            "Get one ticket's full details (title, status, description, branch, cancellation reason) by id. Call this " +
            "after list_tickets to ground an answer in a specific ticket's actual content, or to check a candidate " +
            "ticket for duplicate/related work before drafting a new one.",
            """{"type":"object","properties":{"id":{"type":"string","description":"The ticket's id, as returned by list_tickets."}},"required":["id"]}"""),
        new LlmToolDefinition(
            "propose_ticket",
            "Draft a new ticket for the user to review. Call this ONLY when the user has explicitly asked you to create, " +
            "log, or file a ticket - never propose one on your own initiative from a general question. Before drafting, " +
            "check list_tickets/get_ticket for related or duplicate existing tickets and reference them by id in the " +
            "description when relevant. This does not create the ticket; the user must approve it themselves in the chat.",
            """{"type":"object","properties":{"title":{"type":"string","description":"Short ticket title."},"description":{"type":"string","description":"Ticket description."}},"required":["title","description"]}"""),
    ];

    private static ChatMessageDto ToDto(ChatMessage message) => new(
        message.Id,
        message.Role,
        message.Content,
        message.ProposedTicketTitle,
        message.ProposedTicketDescription,
        message.CreatedAtUtc);
}
