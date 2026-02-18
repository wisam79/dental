using Xunit;
using DentalID.Application.Services;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;
using Moq;
using Moq.Protected;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Text.Json;
using System.Collections.Generic;

namespace DentalID.Tests.Services;

public class AiChatServiceTests
{
    private readonly Mock<ILoggerService> _mockLogger;
    private readonly Mock<IAiConfiguration> _mockConfig;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly HttpClient _httpClient;

    public AiChatServiceTests()
    {
        _mockLogger = new Mock<ILoggerService>();
        _mockConfig = new Mock<IAiConfiguration>();
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpHandler.Object);
    }

    [Fact]
    public async Task GetResponseAsync_UsesFallback_WhenProviderIsRulesBased()
    {
        // Arrange
        _mockConfig.Setup(x => x.LlmProvider).Returns("rulesbased");
        _mockConfig.Setup(x => x.EnableRulesBasedFallback).Returns(true);
        var service = new AiChatService(_mockLogger.Object, _mockConfig.Object, _httpClient);
        var context = new AnalysisContext { TeethCount = 28 };

        // Act
        var response = await service.GetResponseAsync("summary", context);

        // Assert
        Assert.NotNull(response.Content);
        Assert.Contains("Teeth Detected: 28", response.Content);
        Assert.Equal("High", response.Confidence);
    }

    [Fact]
    public async Task GetResponseAsync_CallsOpenAI_WhenProviderIsOpenAI()
    {
        // Arrange
        _mockConfig.Setup(x => x.LlmProvider).Returns("openai");
        _mockConfig.Setup(x => x.LlmApiKey).Returns("sk-test");
        
        var mockResponse = new 
        { 
            choices = new[] { new { message = new { content = "AI Response" } } } 
        };
        
        SetupMockHttp(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));
        
        var service = new AiChatService(_mockLogger.Object, _mockConfig.Object, _httpClient);
        var context = new AnalysisContext();

        // Act
        var response = await service.GetResponseAsync("hello", context);

        // Assert
        Assert.Equal("AI Response", response.Content);
        _mockHttpHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.Method == HttpMethod.Post && 
                req.RequestUri != null &&
                req.RequestUri.ToString().Contains("api.openai.com")),
            ItExpr.IsAny<CancellationToken>()
        );
    }
    
    [Fact]
    public async Task GetResponseAsync_FallsBackToRules_OnApiFailure()
    {
         // Arrange
        _mockConfig.Setup(x => x.LlmProvider).Returns("openai");
        _mockConfig.Setup(x => x.LlmApiKey).Returns("sk-test");
        _mockConfig.Setup(x => x.EnableRulesBasedFallback).Returns(true);
        
        SetupMockHttp(HttpStatusCode.InternalServerError, "Error"); // Simulate API Error
        
        var service = new AiChatService(_mockLogger.Object, _mockConfig.Object, _httpClient);
        var context = new AnalysisContext { TeethCount = 32 };

        // Act
        var response = await service.GetResponseAsync("summary", context);

        // Assert
        Assert.Contains("Teeth Detected: 32", response.Content); // Fallback content
        Assert.True(response.Metadata.ContainsKey("fallback"));
    }

    private void SetupMockHttp(HttpStatusCode code, string content)
    {
        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = code,
                Content = new StringContent(content)
            });
    }
}
