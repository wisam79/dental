using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DentalID.Application.Configuration;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Desktop.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;
using System.IO;
using System.Linq;

namespace DentalID.Tests.ViewModels;

public class MatchingViewModelTests
{
    private readonly Mock<ISubjectRepository> _mockSubjectRepo;
    private readonly Mock<IMatchRepository> _mockMatchRepo;
    private readonly Mock<IDentalImageRepository> _mockImageRepo;
    private readonly Mock<ICaseRepository> _mockCaseRepo;
    private readonly Mock<IAiPipelineService> _mockPipeline;
    private readonly Mock<IMatchingService> _mockMatchingService;
    private readonly Mock<IReportService> _mockReportService;
    private readonly Mock<IFileService> _mockFileService;
    private readonly Mock<ILoggerService> _mockLogger;
    private readonly AiConfiguration _config;
    private readonly MatchingViewModel _viewModel;

    public MatchingViewModelTests()
    {
        _mockSubjectRepo = new Mock<ISubjectRepository>();
        _mockMatchRepo = new Mock<IMatchRepository>();
        _mockImageRepo = new Mock<IDentalImageRepository>();
        _mockCaseRepo = new Mock<ICaseRepository>();
        _mockPipeline = new Mock<IAiPipelineService>();
        _mockMatchingService = new Mock<IMatchingService>();
        _mockReportService = new Mock<IReportService>();
        _mockFileService = new Mock<IFileService>();
        _mockLogger = new Mock<ILoggerService>();
        _config = new AiConfiguration { Thresholds = new ThresholdSettings { MatchSimilarityThreshold = 0.8f } };

        _viewModel = new MatchingViewModel(
            _mockSubjectRepo.Object,
            _mockMatchRepo.Object,
            _mockImageRepo.Object,
            _mockCaseRepo.Object,
            _mockPipeline.Object,
            _mockMatchingService.Object,
            _mockReportService.Object,
            _mockFileService.Object,
            _mockLogger.Object,
            _config
        );
    }

    [Fact]
    public async Task ConfirmMatch_ShouldUpdateMatchRecord_AndLogAudit()
    {
        // Arrange
        var subject = new Subject { Id = 1, FullName = "John Doe", SubjectId = "SUB-001" };
        var matchRecord = new DentalID.Core.Entities.Match { Id = 100, QueryImageId = 50, MatchedSubjectId = 1, IsConfirmed = false };
        
        var candidate = new MatchCandidate 
        { 
            Subject = subject, 
            Score = 0.95,
            MatchId = 100 // Linked to existing match
        };

        _viewModel.Candidates.Add(candidate);
        _viewModel.SelectedCandidate = candidate;

        _mockMatchRepo.Setup(r => r.GetByIdAsync(100))
            .ReturnsAsync(matchRecord);

        // Act
        // Invoke ConfirmMatch Command
        if (_viewModel.ConfirmMatchCommand.CanExecute(null))
        {
             await _viewModel.ConfirmMatchCommand.ExecuteAsync(null);
        }

        // Assert
        matchRecord.IsConfirmed.Should().BeTrue();
        matchRecord.ConfirmedAt.Should().NotBeNull();
        matchRecord.Notes.Should().Contain("Confirmed by user");

        _mockMatchRepo.Verify(r => r.UpdateAsync(matchRecord), Times.Once);
        _mockLogger.Verify(l => l.LogAudit("MATCH_CONFIRMED", It.IsAny<string>(),It.IsAny<string>(), "100"), Times.Once);
    }

    [Fact]
    public async Task RunMatching_ShouldSaveQueryImage_AndCreateMatchRecords()
    {
        // Arrange
        var queryPath = "c:\\test\\query.jpg";
        var subject = new Subject { Id = 1, FullName = "Jane Doe" };
        var matches = new List<MatchCandidate>
        {
            new MatchCandidate { Subject = subject, Score = 0.9 }
        };

        _mockFileService.Setup(f => f.OpenRead(queryPath)).Returns(new MemoryStream());
        _mockPipeline.Setup(p => p.ExtractFeaturesAsync(It.IsAny<Stream>()))
            .ReturnsAsync((new float[128], null));
        _mockSubjectRepo.Setup(r => r.GetAllAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<Subject> { subject });
        _mockMatchingService.Setup(s => s.FindMatches(It.IsAny<DentalFingerprint>(), It.IsAny<List<Subject>>(), It.IsAny<MatchingCriteria>()))
            .Returns(matches); // Note: MatchingService is synchronous or async? Based on VM call: await Task.Run(() => _matchingService.FindMatches(...)) implies sync or async wrapped.
            // Wait, MatchingService interface usually async? Let's check. VM uses Task.Run wrapper around it.
            // Assuming FindMatches returns List<MatchCandidate>.

        _mockImageRepo.Setup(r => r.AddAsync(It.IsAny<DentalImage>()))
            .ReturnsAsync(new DentalImage { Id = 123 }); // Saved Query Image ID
            
        _mockSubjectRepo.Setup(r => r.AddAsync(It.IsAny<Subject>()))
            .ReturnsAsync(new Subject { Id = 99, FullName = "Query Subject" });

        _mockMatchRepo.Setup(r => r.AddAsync(It.IsAny<DentalID.Core.Entities.Match>()))
            .ReturnsAsync((DentalID.Core.Entities.Match m) => { m.Id = 999; return m; }); // Assign ID to new match

        // Load Query
        await _viewModel.LoadQueryImage(queryPath); 

        // Assert
        _viewModel.Candidates.Should().HaveCount(1);
        var candidate = _viewModel.Candidates.First();
        candidate.MatchId.Should().Be(999); 
        
        _mockSubjectRepo.Verify(r => r.AddAsync(It.Is<Subject>(s => s.FullName.StartsWith("Query") && s.SubjectId.StartsWith("QRY-"))), Times.Once);
        _mockImageRepo.Verify(r => r.AddAsync(It.Is<DentalImage>(i => i.ImagePath == queryPath && i.ImageType == DentalID.Core.Enums.ImageType.Other && i.SubjectId == 99)), Times.Once);
        _mockMatchRepo.Verify(r => r.AddAsync(It.Is<DentalID.Core.Entities.Match>(m => m.QueryImageId == 123 && m.MatchedSubjectId == subject.Id)), Times.Once);
    }
}
