using Moq;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Agents.Validators;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Projects;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Agents;

public class AgentServiceTests
{
    private readonly Mock<IAgentRepository> _agentRepository = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly AgentService _sut;
    private readonly Project _project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

    public AgentServiceTests()
    {
        _sut = new AgentService(
            _agentRepository.Object,
            _projectAccessGuard.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new UpdateAgentConfigurationRequestValidator());
    }

    [Fact]
    public async Task EnsureDefaultAgentsAsync_WhenNoAgentsExist_CreatesOnePerPipelineRole()
    {
        _agentRepository
            .Setup(r => r.GetByProjectAndRoleAsync(_project.Id, It.IsAny<AgentRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        await _sut.EnsureDefaultAgentsAsync(_project.Id);

        _agentRepository.Verify(r => r.AddAsync(It.Is<Agent>(a => a.Role == AgentRole.Research), It.IsAny<CancellationToken>()), Times.Once);
        _agentRepository.Verify(r => r.AddAsync(It.Is<Agent>(a => a.Role == AgentRole.Design), It.IsAny<CancellationToken>()), Times.Once);
        _agentRepository.Verify(r => r.AddAsync(It.Is<Agent>(a => a.Role == AgentRole.Coding), It.IsAny<CancellationToken>()), Times.Once);
        _agentRepository.Verify(r => r.AddAsync(It.Is<Agent>(a => a.Role == AgentRole.Testing), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureDefaultAgentsAsync_WhenAllFourAlreadyExist_CreatesNothing()
    {
        _agentRepository
            .Setup(r => r.GetByProjectAndRoleAsync(_project.Id, It.IsAny<AgentRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid projectId, AgentRole role, CancellationToken _) => Agent.Create(projectId, $"{role} Agent", role));

        await _sut.EnsureDefaultAgentsAsync(_project.Id);

        _agentRepository.Verify(r => r.AddAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureLiveAgentAsync_WhenNoneExists_CreatesOne()
    {
        _agentRepository
            .Setup(r => r.GetByProjectAndRoleAsync(_project.Id, AgentRole.LiveAgent, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        await _sut.EnsureLiveAgentAsync(_project.Id);

        _agentRepository.Verify(r => r.AddAsync(It.Is<Agent>(a => a.Role == AgentRole.LiveAgent), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureLiveAgentAsync_WhenOneAlreadyExists_CreatesNothing()
    {
        _agentRepository
            .Setup(r => r.GetByProjectAndRoleAsync(_project.Id, AgentRole.LiveAgent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Agent.Create(_project.Id, "Live Agent", AgentRole.LiveAgent));

        await _sut.EnsureLiveAgentAsync(_project.Id);

        _agentRepository.Verify(r => r.AddAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
