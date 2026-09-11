using Moq;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Projects.Dtos;
using TeamPilot.Application.Projects.Validators;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Projects;

public class ProjectServiceTests
{
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly ProjectService _sut;

    public ProjectServiceTests()
    {
        _sut = new ProjectService(
            _projectRepository.Object,
            _userRepository.Object,
            _currentUser.Object,
            _projectAccessGuard.Object,
            _unitOfWork.Object,
            new CreateProjectRequestValidator(),
            new UpdateProjectRequestValidator());
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_AddsProjectAndSavesChanges()
    {
        var request = new CreateProjectRequest("TeamPilot", "AI ticketing system", "C:/repos/teampilot");

        var result = await _sut.CreateAsync(request);

        Assert.Equal("TeamPilot", result.Name);
        Assert.Equal("C:/repos/teampilot", result.RepositoryPath);
        _projectRepository.Verify(r => r.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyRepositoryPath_ThrowsValidationException()
    {
        var request = new CreateProjectRequest("TeamPilot", "desc", string.Empty);

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task GetByIdAsync_WhenProjectDoesNotExist_ThrowsNotFoundException()
    {
        _projectRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Project?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_WhenProjectExists_UpdatesDetails()
    {
        var project = Project.Create("TeamPilot", "desc", "C:/repos/teampilot");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var request = new UpdateProjectRequest("TeamPilot Renamed", "New description", "C:/repos/teampilot-v2");
        var result = await _sut.UpdateAsync(project.Id, request);

        Assert.Equal("TeamPilot Renamed", result.Name);
        Assert.Equal("C:/repos/teampilot-v2", result.RepositoryPath);
    }

    [Fact]
    public async Task ListAsync_WhenCallerIsAdmin_ReturnsAllProjects()
    {
        var projectA = Project.Create("A", "desc", "C:/repos/a");
        var projectB = Project.Create("B", "desc", "C:/repos/b");
        _projectRepository.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([projectA, projectB]);
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(true);

        var result = await _sut.ListAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ListAsync_WhenCallerIsNotAdmin_ReturnsOnlyAssignedProjects()
    {
        var projectA = Project.Create("A", "desc", "C:/repos/a");
        var projectB = Project.Create("B", "desc", "C:/repos/b");
        _projectRepository.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([projectA, projectB]);
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(false);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _userRepository
            .Setup(r => r.GetAssignedProjectIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([projectA.Id]);

        var result = await _sut.ListAsync();

        Assert.Single(result);
        Assert.Equal(projectA.Id, result[0].Id);
    }
}
