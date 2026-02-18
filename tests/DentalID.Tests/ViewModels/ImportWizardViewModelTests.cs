using Xunit;
using Moq;
using DentalID.Desktop.ViewModels;
using DentalID.Core.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System;
using DentalID.Core.Entities;

namespace DentalID.Tests.ViewModels;

public class ImportWizardViewModelTests
{
    private readonly Mock<IDataImportService> _mockImportService;
    private readonly ImportWizardViewModel _viewModel;

    public ImportWizardViewModelTests()
    {
        _mockImportService = new Mock<IDataImportService>();
        _viewModel = new ImportWizardViewModel(_mockImportService.Object);
    }

    [Fact]
    public void Constructor_ShouldSetInitialStatus()
    {
        Assert.Equal("Select a CSV file to begin.", _viewModel.StatusMessage);
        Assert.Equal(1, _viewModel.CurrentStep);
    }

    [Fact]
    public async Task ParseFile_ShouldPopulateMappings_WhenFileIsValid()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Full Name,Gender,DOB\nJohn Doe,M,1990-01-01");
        _viewModel.SelectedFilePath = tempFile;

        var mockRecords = new List<Dictionary<string, string>>
        {
            new() { { "Full Name", "John Doe" }, { "Gender", "M" }, { "DOB", "1990-01-01" } }
        };

        _mockImportService.Setup(s => s.ParseCsvAsync(It.IsAny<Stream>()))
            .ReturnsAsync(mockRecords);

        // Act
        // We can't easily call private ParseFile directly, but in the ViewModel it is called by SelectFile
        // However, SelectFile uses UI dialogs.
        // We might need to expose ParseFile as public or internal for testing, or simulate the command if possible.
        // Refactoring ImportWizardViewModel to make ParseFile public or internal would be best.
        // For now, let's use Reflection to invoke it or assume we refactor it.
        // Let's refactor ParseFile to be public ParseFileAsync for now.
        
        // Wait, I can't refactor inside this Write call. I will assume I will refactor it next.
        // Or I can test the AutoMap logic which is public on ColumnMappingViewModel?
        // Let's try to invoke the command or method if accessible.
        
        // Actually, let's stick to testing what is public.
        // The ViewModel is hard to test because logic is in private ParseFile.
        // I will REFLECT to invoke ParseFile for now to avoid breaking changes if not needed.
        
        var method = typeof(ImportWizardViewModel).GetMethod("ParseFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var task = (Task)method!.Invoke(_viewModel, null)!;
        await task;

        // Assert
        Assert.Equal(2, _viewModel.CurrentStep);
        Assert.NotEmpty(_viewModel.Mappings);
        Assert.Contains(_viewModel.Mappings, m => m.TargetProperty == "FullName" && m.SelectedSourceColumn == "Full Name");
        
        // Cleanup
        if (File.Exists(tempFile)) File.Delete(tempFile);
    }
    
    [Fact]
    public async Task ExecuteImport_ShouldCallImportService()
    {
        // Arrange: Simulate state after parsing
        var mapping = new ColumnMappingViewModel("Full Name", "FullName", new List<string> { "Name" });
        mapping.SelectedSourceColumn = "Name";
        
        _viewModel.Mappings = new System.Collections.ObjectModel.ObservableCollection<ColumnMappingViewModel> { mapping };
        
        // Need to inject data into _allRecords private field
        var records = new List<Dictionary<string, string>>
        {
            new() { { "Name", "Alice" } }
        };
        var field = typeof(ImportWizardViewModel).GetField("_allRecords", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(_viewModel, records);

        _mockImportService.Setup(s => s.ImportSubjectsAsync(It.IsAny<List<Subject>>()))
            .ReturnsAsync(new ImportResult { SuccessCount = 1 });

        // Act
        await _viewModel.ExecuteImportCommand.ExecuteAsync(null);

        // Assert
        _mockImportService.Verify(s => s.ImportSubjectsAsync(It.Is<List<Subject>>(l => l.Count == 1 && l[0].FullName == "Alice")), Times.Once);
        Assert.Equal(3, _viewModel.CurrentStep);
    }
}
