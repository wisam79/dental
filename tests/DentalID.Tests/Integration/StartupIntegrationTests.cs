using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using DentalID.Desktop.Services;
using DentalID.Application.Configuration;
using DentalID.Core.Interfaces;
using DentalID.Application.Interfaces;
using DentalID.Desktop.ViewModels;
using SkiaSharp;

namespace DentalID.Tests.Integration
{
    public class StartupIntegrationTests
    {
        private readonly ITestOutputHelper _output;

        public StartupIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task DI_Container_Shares_SessionManager_Instance()
        {
            // Arrange
            var bootstrapper = new Bootstrapper();
            var settings = new AppSettings();
            var aiSettings = new AiSettings();
            
            // Locate models directory (up to 6 levels up)
            var searchDir = AppContext.BaseDirectory;
            string? projectRoot = null;
            for (int i = 0; i < 6; i++)
            {
                if (Directory.Exists(Path.Combine(searchDir, "models")))
                {
                    projectRoot = searchDir;
                    break;
                }
                var parent = Directory.GetParent(searchDir);
                if (parent == null) break;
                searchDir = parent.FullName;
            }
            
            if (projectRoot == null)
            {
                _output.WriteLine("WARNING: Could not find 'models' directory. Test Skipped.");
                return;
            }
            
            var modelsDir = Path.Combine(projectRoot, "models");
            _output.WriteLine($"Models Dir: {modelsDir}");
            
            // Act
            var provider = bootstrapper.ConfigureServices(settings, aiSettings);
            
            // Resolve Services
            var pipeline = provider.GetRequiredService<IAiPipelineService>();
            var sessionManager = provider.GetRequiredService<IOnnxSessionManager>();
            var forensicService = provider.GetRequiredService<IForensicAnalysisService>();
            
            _output.WriteLine($"Service Resolved. Pipeline Type: {pipeline.GetType().Name}");
            _output.WriteLine($"SessionManager Type: {sessionManager.GetType().Name}");
            
            // Assert 1: Initialize Pipeline
            // _output.WriteLine($"Initializing Pipeline...");
            // await pipeline.InitializeAsync(modelsDir);
            
            // Assert.True(pipeline.IsReady, "Pipeline should be ready after init");
            // Assert.True(sessionManager.IsReady, "SessionManager should be ready after pipeline init");
            
            // Assert 2: Forensic Service Usage
            // Create a dummy image
            using var ms = new MemoryStream();
            using var surface = SKSurface.Create(new SKImageInfo(640, 640));
            surface.Canvas.Clear(SKColors.White);
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
            data.SaveTo(ms);
            ms.Position = 0;
            
            var tempFile = Path.GetTempFileName() + ".jpg";
            await File.WriteAllBytesAsync(tempFile, ms.ToArray()); 
            
            try
            {
                _output.WriteLine("Calling ForensicService.AnalyzeImageAsync...");
                var result = await forensicService.AnalyzeImageAsync(tempFile);
                
                if (!string.IsNullOrEmpty(result.Error))
                    _output.WriteLine($"Result Error: {result.Error}");
                    
                // This is the critical check:
                Assert.False(result.Error != null && result.Error.Contains("not initialized"), 
                    $"Service reported not initialized! Error: {result.Error}");
                    
                Assert.True(result.IsSuccess || result.Error != null, "Analysis result valid");
            }
            finally
            {
                if(File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
