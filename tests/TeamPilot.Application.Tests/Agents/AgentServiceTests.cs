using Moq;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Agents.Dtos;
using TeamPilot.Application.Agents.Validators;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Projects;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
using Xunit;

namespace TeamPilot.Application.Tests.Agents;

public class AgentServiceTests
{
    private readonly Mock<IAgentRepository> _agentRepository = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly AgentService _sut;
    private readonly Project _project = Project.Create("TeamPilot", "desc", "C:/repos/teampilot");

    public AgentServiceTests()
    {
        _projectRepository.Setup(r => r.GetByIdAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);

        _sut = new AgentService(
            _agentRepository.Object,
            _projectRepository.Object,
            _projectAccessGuard.Object,
            _unitOfWork.Object,
            new CreateAgentRequestValidator(),
            new UpdateAgentConfigurationRequestValidator());
    }

    [Fact]
    public async Task CreateAsync_WhenRoleIsOrchestratorAndNoneExists_CreatesAgent()
    {
        _agentRepository.Setup(r => r.HasOrchestratorAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _sut.CreateAsync(_project.Id, new CreateAgentRequest("Orchestrator", AgentRole.Orchestrator, null));

        Assert.Equal(AgentRole.Orchestrator, result.Role);
        _agentRepository.Verify(r => r.AddAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenRoleIsOrchestratorAndOneAlreadyExists_ThrowsProjectAlreadyHasOrchestratorException()
    {
        _agentRepository.Setup(r => r.HasOrchestratorAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ProjectAlreadyHasOrchestratorException>(
            () => _sut.CreateAsync(_project.Id, new CreateAgentRequest("Second Orchestrator", AgentRole.Orchestrator, null)));

        _agentRepository.Verify(r => r.AddAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenRoleIsCoding_DoesNotCheckForExistingOrchestrator()
    {
        var result = await _sut.CreateAsync(_project.Id, new CreateAgentRequest("Coder", AgentRole.Coding, null));

        Assert.Equal(AgentRole.Coding, result.Role);
        _agentRepository.Verify(r => r.HasOrchestratorAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
