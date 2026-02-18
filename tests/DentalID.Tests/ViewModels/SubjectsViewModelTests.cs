using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Desktop.Messages;
using DentalID.Desktop.ViewModels;
using Moq;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace DentalID.Tests.ViewModels;

public class SubjectsViewModelTests
{
    private readonly Mock<ISubjectRepository> _mockRepo;
    private readonly SubjectsViewModel _viewModel;

    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;

    public SubjectsViewModelTests()
    {
        _mockRepo = new Mock<ISubjectRepository>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockLogger = new Mock<ILoggerService>();
        
        // Setup scope factory
        var scope = new Mock<IServiceScope>();
        var serviceProvider = new Mock<IServiceProvider>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        serviceProvider.Setup(p => p.GetService(typeof(ISubjectRepository))).Returns(_mockRepo.Object);
        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        
        _viewModel = new SubjectsViewModel(_mockRepo.Object, _mockScopeFactory.Object, mockLogger.Object);
        // Reset messenger to avoid side effects from other tests if sharing instance (though strictly unit tests should be isolated)
        WeakReferenceMessenger.Default.Reset(); 
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    [Fact]
    public async Task LoadAsync_ShouldLoadSubjects()
    {
        // Arrange
        var subjects = new List<Subject> { new Subject { FullName = "Test" } };
        _mockRepo.Setup(r => r.GetAllAsync(It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync(subjects);
        _mockRepo.Setup(r => r.GetCountAsync())
                 .ReturnsAsync(1);

        // Act
        // Access private command via public method or command execution
        await _viewModel.InitializeCommand.ExecuteAsync(null);

        // Assert
        Assert.Single(_viewModel.Subjects);
        Assert.Equal("Test", _viewModel.Subjects[0].FullName);
    }

    [Fact]
    public async Task SaveSubject_ShouldFail_WhenNameIsEmpty()
    {
        // Arrange
        _viewModel.OpenAddDialogCommand.Execute(null);
        _viewModel.FormFullName = ""; // Invalid

        bool toastReceived = false;
        WeakReferenceMessenger.Default.Register<ShowToastMessage>(this, (r, m) =>
        {
            if (m.Value.Type == DentalID.Desktop.Services.ToastType.Warning)
                toastReceived = true;
        });

        // Act
        await _viewModel.SaveSubjectCommand.ExecuteAsync(null);

        // Assert
        Assert.True(_viewModel.HasErrors); // Should have validation errors
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<Subject>()), Times.Never);
        // Toast check depends on WeakReferenceMessenger implementation and test isolation
        Assert.True(toastReceived, "Should receive validation warning toast");
    }

    [Fact]
    public async Task SaveSubject_ShouldSucceed_WhenDataIsValid()
    {
        // Arrange
        _viewModel.OpenAddDialogCommand.Execute(null);
        _viewModel.FormFullName = "Valid Name";
        
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<Subject>()))
                 .ReturnsAsync(new Subject());
        _mockRepo.Setup(r => r.GetAllAsync(It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync(new List<Subject>());

        // Act
        await _viewModel.SaveSubjectCommand.ExecuteAsync(null);

        // Assert
        Assert.False(_viewModel.HasErrors);
        _mockRepo.Verify(r => r.AddAsync(It.Is<Subject>(s => s.FullName == "Valid Name")), Times.Once);
    }

    [Fact]
    public async Task SaveSubject_ShouldHandleErrors_Gracefully()
    {
        // Arrange
        _viewModel.OpenAddDialogCommand.Execute(null);
        _viewModel.FormFullName = "Error Prone";
        _viewModel.FormNationalId = "TEST-NATIONAL-ID"; // Set national ID so the check is triggered

        // Mock GetByNationalIdAsync to return null (no existing subject) so the code proceeds to AddAsync
        _mockRepo.Setup(r => r.GetByNationalIdAsync(It.IsAny<string>()))
                 .ReturnsAsync((Subject?)null);
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<Subject>()))
                 .ThrowsAsync(new Exception("Database failed"));

        ShowToastMessage? receivedMessage = null;
        WeakReferenceMessenger.Default.Register<ShowToastMessage>(this, (r, m) => receivedMessage = m);

        // Act
        await _viewModel.SaveSubjectCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(DentalID.Desktop.Services.ToastType.Error, receivedMessage.Value.Type);
        // The error message could be either the original exception message or a generic error
        Assert.True(
            receivedMessage.Value.Message.Contains("Database failed") || 
            receivedMessage.Value.Message.Contains("Failed to save subject") ||
            receivedMessage.Value.Message.Contains("Error"),
            $"Expected error message but got: {receivedMessage.Value.Message}");
        Assert.False(_viewModel.IsBusy); 

        _mockRepo.Verify(r => r.AddAsync(It.IsAny<Subject>()), Times.Once);
        _mockRepo.Verify(r => r.UpdateAsync(It.IsAny<Subject>()), Times.Never);
    }
    [Fact]
    public async Task DeleteSubject_ShouldDelete_WhenExecuted()
    {
        // Arrange
        var subject = new Subject { Id = 1, FullName = "To Delete" };
        _viewModel.Subjects.Add(subject);
        _viewModel.SelectedSubject = subject;

        _mockRepo.Setup(r => r.DeleteAsync(It.IsAny<int>()))
                 .Returns(Task.CompletedTask);
        _mockRepo.Setup(r => r.GetAllAsync(It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync(new List<Subject>()); // Empty list after reload

        // Act
        await _viewModel.DeleteSubjectCommand.ExecuteAsync(null);

        // Assert
        _mockRepo.Verify(r => r.DeleteAsync(1), Times.Once);
        Assert.Null(_viewModel.SelectedSubject);
    }

    [Fact]
    public async Task Search_ShouldFilter_WhenQueryProvided()
    {
        // Arrange
        _viewModel.SearchQuery = "Doe";
        var results = new List<Subject> { new Subject { FullName = "John Doe" } };

        _mockRepo.Setup(r => r.SearchAsync(It.Is<string>(q => q == "Doe"), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync(results);
        _mockRepo.Setup(r => r.GetCountAsync())
                 .ReturnsAsync(1);

        // Act
        await _viewModel.SearchCommand.ExecuteAsync(null);

        // Assert
        Assert.Single(_viewModel.Subjects);
        Assert.Equal("John Doe", _viewModel.Subjects[0].FullName);
        Assert.Equal(1, _viewModel.CurrentPage); // Should reset to page 1
        _mockRepo.Verify(r => r.SearchAsync("Doe", It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }
}

