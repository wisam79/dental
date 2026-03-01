using Xunit;
using Xunit.Abstractions;
using DentalID.Application.Services;
using DentalID.Application.Configuration;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;
using DentalID.Application.Interfaces;
using Moq;
using System.IO;
using SkiaSharp;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace DentalID.Tests.Integration;

[Trait("Category", "Integration")]
public class AiModelIntegrationTests
{
    private readonly string _modelsPath;
    private readonly ITestOutputHelper _output;

    public AiModelIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        // Adjust this path to point to the actual models directory in the project root
        // Assuming test execution is in bin/Debug/net8.0, and models are in e:/projects/dental/models
        _modelsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../models"));
        
        if (!Directory.Exists(_modelsPath))
        {
             // Fallback for different runner working directories
             _modelsPath = "e:/projects/dental/models";
        }
        _output.WriteLine($"Models Path resolved to: {_modelsPath}");
    }

    [Fact]
    public async Task AnalyzeImageAsync_ShouldRunSuccessfully_WithRealModels()
    {
        // 1. Verify Models Exist
        if (!Directory.Exists(_modelsPath))
        {
            Assert.Fail($"Models directory not found at {_modelsPath}. Integration test cannot run.");
        }

        var teethModel = Path.Combine(_modelsPath, "teeth_detect.onnx");
        if (!File.Exists(teethModel))
        {
            Assert.Fail($"Teeth detection model not found at {teethModel}.");
        }
        _output.WriteLine("Teeth model found.");

        // 2. Setup Service with Real Configuration
        var config = new AiConfiguration
        {
            Model = new ModelSettings 
            { 
                DetectionInputSize = 640,
                GenderAgeInputSize = 96
            },
            Thresholds = new ThresholdSettings(),
            FdiMapping = new FdiMappingSettings { ClassMap = new int[32] },
            EnableTTA = false
        };

        var aiSettings = new AiSettings 
        { 
            EnableGpu = false, // Force CPU for stability in tests
            ConfidenceThreshold = 0.2f 
        };

        var mockLogger = new Mock<ILoggerService>();
        mockLogger.Setup(x => x.LogInformation(It.IsAny<string>())).Callback<string>(s => _output.WriteLine($"[LOG] {s}"));
        mockLogger.Setup(x => x.LogError(It.IsAny<Exception>(), It.IsAny<string>())).Callback<Exception, string>((ex, s) => _output.WriteLine($"[ERROR] {s}: {ex}"));

        var mockBiometric = new Mock<IBiometricService>();
        var mockIntelligence = new Mock<IDentalIntelligenceService>();
        var mockCache = new Mock<ICacheService>();
        
        mockIntelligence.Setup(x => x.Analyze(It.IsAny<AnalysisResult>()));
        mockBiometric.Setup(x => x.GenerateFingerprint(It.IsAny<List<DetectedTooth>>(), It.IsAny<List<DetectedPathology>>())).Returns(new DentalFingerprint());

        var fdiService = new FdiSpatialService();
        var heuristicsService = new ForensicHeuristicsService();
        var yoloParser = new YoloDetectionParser(config, aiSettings, fdiService);

        var tensorPrep = new TensorPreparationService();
        var sessionManager = new OnnxSessionManager(config, aiSettings, mockLogger.Object);
        var teethSvc   = new TeethDetectionService(sessionManager, yoloParser, fdiService, heuristicsService, tensorPrep, config, aiSettings);
        var pathSvc    = new PathologyDetectionService(sessionManager, yoloParser, tensorPrep, config, aiSettings);
        var encoderSvc = new FeatureEncoderService(sessionManager, tensorPrep, config, mockLogger.Object);

        var service = new OnnxInferenceService(
            sessionManager,
            teethSvc,
            pathSvc,
            encoderSvc,
            yoloParser,
            heuristicsService,
            mockIntelligence.Object,
            mockBiometric.Object,
            mockCache.Object,
            mockLogger.Object
        );

        try
        {
            // 3. Initialize
            _output.WriteLine("Initializing Service...");
            await service.InitializeAsync(_modelsPath);
            Assert.True(service.IsReady, "Service should be initialized");
            _output.WriteLine("Service Initialized.");

            // 4. Create Dummy Image (1024x1024 to properly test Encoder resizing)
            using var bmp = new SKBitmap(1024, 1024);
            using (var canvas = new SKCanvas(bmp)) 
            { 
                canvas.Clear(SKColors.Gray); // Use gray to ensure some non-zero values
                
                // Draw a "tooth-like" rectangle to potentially trigger detections
                using var paint = new SKPaint { Color = SKColors.White };
                canvas.DrawRect(new SKRect(400, 400, 600, 600), paint);
            }
            
            using var ms = new MemoryStream();
            bmp.Encode(ms, SKEncodedImageFormat.Png, 100);
            ms.Position = 0;

            // 5. Run Analysis
            _output.WriteLine("Starting Analysis...");
            var result = await service.AnalyzeImageAsync(ms, "test_image.png");
            _output.WriteLine($"Analysis Finished. Time: {result.ProcessingTimeMs}ms");

            // 6. Assertions
            if (result.Error != null)
            {
                 _output.WriteLine($"Analysis Error: {result.Error}");
                 Assert.Null(result.Error);
            }
            
            // Check Feature Vector (Encoder)
            if (File.Exists(Path.Combine(_modelsPath, "encoder.onnx")))
            {
                 _output.WriteLine("Checking Encoder Result...");
                 Assert.NotNull(result.FeatureVector);
                 Assert.Equal(1024, result.FeatureVector.Length);
                 _output.WriteLine("Encoder Valid.");
            }

            // Check Age/Gender
             if (File.Exists(Path.Combine(_modelsPath, "genderage.onnx")))
            {
                _output.WriteLine($"Age: {result.EstimatedAge}, Gender: {result.EstimatedGender}");
                // We don't guarantee non-null for dummy image, but we guarantee no exceptions.
            }
        }
        finally
        {
            service.Dispose();
        }
    }
}
