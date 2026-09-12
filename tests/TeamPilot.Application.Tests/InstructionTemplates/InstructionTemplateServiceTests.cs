using Moq;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.InstructionTemplates;
using TeamPilot.Application.InstructionTemplates.Dtos;
using TeamPilot.Application.InstructionTemplates.Validators;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.InstructionTemplates;

public class InstructionTemplateServiceTests
{
    private readonly Mock<IInstructionTemplateRepository> _repository = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly InstructionTemplateService _sut;

    public InstructionTemplateServiceTests()
    {
        _sut = new InstructionTemplateService(
            _repository.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new CreateInstructionTemplateRequestValidator(),
            new UpdateInstructionTemplateRequestValidator());
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_AddsTemplateAndSavesChanges()
    {
        var request = new CreateInstructionTemplateRequest("Strict TDD", AgentRole.Coding, InstructionType.Guideline, "Write the test first.");

        var result = await _sut.CreateAsync(request);

        Assert.Equal("Strict TDD", result.Name);
        Assert.Equal(AgentRole.Coding, result.Role);
        _repository.Verify(r => r.AddAsync(It.IsAny<InstructionTemplate>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyName_ThrowsValidationException()
    {
        var request = new CreateInstructionTemplateRequest(string.Empty, AgentRole.Coding, InstructionType.Guideline, "content");

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task ListAsync_PassesRoleAndTypeFiltersThrough()
    {
        _repository
            .Setup(r => r.ListAsync(AgentRole.Testing, InstructionType.Requirement, It.IsAny<CancellationToken>()))
            .ReturnsAsync([InstructionTemplate.Create("Security checklist", AgentRole.Testing, InstructionType.Requirement, "content")]);

        var result = await _sut.ListAsync(AgentRole.Testing, InstructionType.Requirement);

        Assert.Single(result);
        Assert.Equal("Security checklist", result[0].Name);
    }

    [Fact]
    public async Task UpdateAsync_WhenTemplateExists_UpdatesNameAndContent()
    {
        var template = InstructionTemplate.Create("Strict TDD", AgentRole.Coding, InstructionType.Guideline, "old content");
        _repository.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var result = await _sut.UpdateAsync(template.Id, new UpdateInstructionTemplateRequest("Strict TDD v2", "new content"));

        Assert.Equal("Strict TDD v2", result.Name);
        Assert.Equal("new content", result.Content);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenTemplateDoesNotExist_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((InstructionTemplate?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.UpdateAsync(Guid.NewGuid(), new UpdateInstructionTemplateRequest("name", "content")));
    }

    [Fact]
    public async Task DeleteAsync_WhenTemplateExists_DeletesAndSavesChanges()
    {
        var template = InstructionTemplate.Create("Strict TDD", AgentRole.Coding, InstructionType.Guideline, "content");
        _repository.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        await _sut.DeleteAsync(template.Id);

        _repository.Verify(r => r.DeleteAsync(template, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenTemplateDoesNotExist_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((InstructionTemplate?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteAsync(Guid.NewGuid()));
    }
}
