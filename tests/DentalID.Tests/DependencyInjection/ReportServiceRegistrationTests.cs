using Xunit;
using Microsoft.Extensions.DependencyInjection;
using DentalID.Core.Interfaces;
using DentalID.Desktop.Services;
using DentalID.Infrastructure.Services;
using DentalID.Application.Configuration;

namespace DentalID.Tests.DependencyInjection;

public class ReportServiceRegistrationTests
{
    [Fact]
    public void ReportService_ShouldBeRegistered()
    {
        // Arrange
        var bootstrapper = new Bootstrapper();
        
        // Act
        var provider = bootstrapper.ConfigureServices(new AppSettings(), new AiSettings());
        var reportService = provider.GetService<IReportService>();

        // Assert
        Assert.NotNull(reportService);
        Assert.IsType<PdfReportService>(reportService);
    }
}
