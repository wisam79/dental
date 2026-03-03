using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using DentalID.Application.Configuration;
using DentalID.Core.Interfaces;
using DentalID.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalID.Tests.DependencyInjection;

public class BootstrapperSecurityTests
{

    [Fact]
    public async Task InitializeDatabaseAsync_ShouldNotCreateInitialCredentialsFile()
    {
        var bootstrapper = new Bootstrapper();
        var provider = bootstrapper.ConfigureServices(new AppSettings(), new AiSettings());

        var credentialsPath = Path.Combine(AppContext.BaseDirectory, "data", "initial_credentials.txt");
        if (File.Exists(credentialsPath))
        {
            File.Delete(credentialsPath);
        }

        var logger = provider.GetRequiredService<ILoggerService>();
        await InvokePrivateAsync(bootstrapper, "InitializeDatabaseAsync", logger, provider);

        Assert.False(File.Exists(credentialsPath));
    }

    [Fact]
    public async Task VerifyIntegrityAsync_ShouldCreateBaseline_WhenManifestMissingAndAllowed()
    {
        var bootstrapper = new Bootstrapper();
        var aiSettings = new AiSettings();
        var provider = bootstrapper.ConfigureServices(new AppSettings(), aiSettings);

        var modelPreparation = EnsureRequiredModelFilesExist();
        var manifestPath = Path.Combine(Path.GetTempPath(), $"model_integrity_{Guid.NewGuid():N}.json");

        aiSettings.EnableModelIntegrity = true;
        aiSettings.AllowIntegrityBaselineCreation = true;
        aiSettings.ModelIntegrityManifestPath = manifestPath;

        try
        {
            var logger = provider.GetRequiredService<ILoggerService>();
            await InvokePrivateAsync(bootstrapper, "VerifyIntegrityAsync", logger, provider);

            Assert.True(File.Exists(manifestPath));
            var json = await File.ReadAllTextAsync(manifestPath);
            Assert.Contains("teeth_detect.onnx", json);
            Assert.Contains("encoder.onnx", json);
        }
        finally
        {
            SafeDelete(manifestPath);
            CleanupCreatedModelFiles(modelPreparation.CreatedFiles);
        }
    }

    [Fact]
    public async Task VerifyIntegrityAsync_ShouldFail_WhenManifestHashMismatch()
    {
        var bootstrapper = new Bootstrapper();
        var aiSettings = new AiSettings();
        var provider = bootstrapper.ConfigureServices(new AppSettings(), aiSettings);

        var modelPreparation = EnsureRequiredModelFilesExist();
        var manifestPath = Path.Combine(Path.GetTempPath(), $"model_integrity_{Guid.NewGuid():N}.json");

        var modelsDir = modelPreparation.ModelsDirectory;
        var manifest = new
        {
            version = 1,
            createdUtc = DateTime.UtcNow.ToString("O"),
            models = new Dictionary<string, string>
            {
                ["teeth_detect.onnx"] = ComputeSha256(Path.Combine(modelsDir, "teeth_detect.onnx")),
                ["pathology_detect.onnx"] = ComputeSha256(Path.Combine(modelsDir, "pathology_detect.onnx")),
                ["encoder.onnx"] = "BAD_HASH_VALUE"
            }
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        aiSettings.EnableModelIntegrity = true;
        aiSettings.AllowIntegrityBaselineCreation = false;
        aiSettings.ModelIntegrityManifestPath = manifestPath;

        try
        {
            var logger = provider.GetRequiredService<ILoggerService>();
            await Assert.ThrowsAsync<Exception>(() => InvokePrivateAsync(bootstrapper, "VerifyIntegrityAsync", logger, provider));
        }
        finally
        {
            SafeDelete(manifestPath);
            CleanupCreatedModelFiles(modelPreparation.CreatedFiles);
        }
    }

    [Fact]
    public async Task VerifyIntegrityAsync_ShouldFail_WhenOptionalModelExistsButManifestEntryMissing()
    {
        var bootstrapper = new Bootstrapper();
        var aiSettings = new AiSettings();
        var provider = bootstrapper.ConfigureServices(new AppSettings(), aiSettings);

        var modelPreparation = EnsureRequiredModelFilesExist();
        var optionalModel = EnsureModelFileExists(modelPreparation.ModelsDirectory, "genderage.onnx");
        var manifestPath = Path.Combine(Path.GetTempPath(), $"model_integrity_{Guid.NewGuid():N}.json");

        var modelsDir = modelPreparation.ModelsDirectory;
        var manifest = new
        {
            version = 1,
            createdUtc = DateTime.UtcNow.ToString("O"),
            models = new Dictionary<string, string>
            {
                ["teeth_detect.onnx"] = ComputeSha256(Path.Combine(modelsDir, "teeth_detect.onnx")),
                ["pathology_detect.onnx"] = ComputeSha256(Path.Combine(modelsDir, "pathology_detect.onnx")),
                ["encoder.onnx"] = ComputeSha256(Path.Combine(modelsDir, "encoder.onnx"))
            }
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        aiSettings.EnableModelIntegrity = true;
        aiSettings.AllowIntegrityBaselineCreation = false;
        aiSettings.ModelIntegrityManifestPath = manifestPath;

        try
        {
            var logger = provider.GetRequiredService<ILoggerService>();
            var ex = await Assert.ThrowsAsync<Exception>(() => InvokePrivateAsync(bootstrapper, "VerifyIntegrityAsync", logger, provider));
            Assert.Contains("genderage.onnx", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDelete(manifestPath);
            CleanupCreatedModelFiles(modelPreparation.CreatedFiles);
            if (optionalModel.Created)
            {
                SafeDelete(optionalModel.FullPath);
            }
        }
    }

    private static async Task InvokePrivateAsync(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(instance, args);
        var task = Assert.IsAssignableFrom<Task>(result);
        await task;
    }

    private static (string ModelsDirectory, List<string> CreatedFiles) EnsureRequiredModelFilesExist()
    {
        var modelsDir = Path.Combine(AppContext.BaseDirectory, "models");
        Directory.CreateDirectory(modelsDir);

        var required = new[] { "teeth_detect.onnx", "pathology_detect.onnx", "encoder.onnx" };
        var createdFiles = new List<string>();

        foreach (var file in required)
        {
            var fullPath = Path.Combine(modelsDir, file);
            if (File.Exists(fullPath))
            {
                continue;
            }

            var buffer = new byte[512];
            RandomNumberGenerator.Fill(buffer);
            File.WriteAllBytes(fullPath, buffer);
            createdFiles.Add(fullPath);
        }

        return (modelsDir, createdFiles);
    }

    private static (string FullPath, bool Created) EnsureModelFileExists(string modelsDir, string fileName)
    {
        var fullPath = Path.Combine(modelsDir, fileName);
        if (File.Exists(fullPath))
        {
            return (fullPath, false);
        }

        var buffer = new byte[512];
        RandomNumberGenerator.Fill(buffer);
        File.WriteAllBytes(fullPath, buffer);
        return (fullPath, true);
    }

    private static void CleanupCreatedModelFiles(IEnumerable<string> createdFiles)
    {
        foreach (var path in createdFiles)
        {
            SafeDelete(path);
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Test cleanup should not fail the run.
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}
