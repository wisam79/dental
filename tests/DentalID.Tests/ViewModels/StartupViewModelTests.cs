using System;
using System.Collections.Generic;
using System.ComponentModel;
using DentalID.Desktop.ViewModels;
using FluentAssertions;
using Xunit;

namespace DentalID.Tests.ViewModels;

public class StartupViewModelTests
{
    // ── UpdateStatus ──────────────────────────────────────────

    [Fact]
    public void UpdateStatus_ShouldSetMessageAndProgress()
    {
        var vm = new StartupViewModel();

        vm.UpdateStatus("Loading...", 42);

        vm.StatusMessage.Should().Be("Loading...");
        vm.ProgressValue.Should().Be(42);
    }

    // ── Stage readiness thresholds ────────────────────────────

    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(19, false, false, false)]
    [InlineData(20, true, false, false)]
    [InlineData(50, true, true, false)]
    [InlineData(80, true, true, true)]
    [InlineData(100, true, true, true)]
    public void ReadinessFlags_ShouldReflectProgressThresholds(
        double progress, bool integrity, bool database, bool ai)
    {
        var vm = new StartupViewModel();
        vm.ProgressValue = progress;

        vm.IsIntegrityReady.Should().Be(integrity);
        vm.IsDatabaseReady.Should().Be(database);
        vm.IsAiReady.Should().Be(ai);
    }

    [Fact]
    public void Opacity_ShouldBe035_WhenNotReady_And1_WhenReady()
    {
        var vm = new StartupViewModel();
        vm.ProgressValue = 0;

        vm.IntegrityOpacity.Should().Be(0.35);
        vm.DatabaseOpacity.Should().Be(0.35);
        vm.AiOpacity.Should().Be(0.35);

        vm.ProgressValue = 80;

        vm.IntegrityOpacity.Should().Be(1.0);
        vm.DatabaseOpacity.Should().Be(1.0);
        vm.AiOpacity.Should().Be(1.0);
    }

    // ── Model state management ────────────────────────────────

    [Fact]
    public void ResetModelStates_ShouldSetAllToPendingAndZeroProgress()
    {
        var vm = new StartupViewModel();
        vm.UpdateModelStatus("teeth", "Ready", 100);
        vm.UpdateModelStatus("pathology", "Error", 50);

        vm.ResetModelStates();

        vm.TeethModelState.Should().Be(StartupViewModel.StatePending);
        vm.PathologyModelState.Should().Be(StartupViewModel.StatePending);
        vm.EncoderModelState.Should().Be(StartupViewModel.StatePending);
        vm.TeethModelProgress.Should().Be(0);
        vm.PathologyModelProgress.Should().Be(0);
        vm.EncoderModelProgress.Should().Be(0);
    }

    [Theory]
    [InlineData("teeth", "Ready", 100)]
    [InlineData("pathology", "Loading", 50)]
    [InlineData("encoder", "Verified", 75)]
    public void UpdateModelStatus_ShouldSetCorrectModelFields(
        string model, string state, double progress)
    {
        var vm = new StartupViewModel();

        vm.UpdateModelStatus(model, state, progress);

        switch (model)
        {
            case "teeth":
                vm.TeethModelState.Should().Be(state);
                vm.TeethModelProgress.Should().Be(progress);
                break;
            case "pathology":
                vm.PathologyModelState.Should().Be(state);
                vm.PathologyModelProgress.Should().Be(progress);
                break;
            case "encoder":
                vm.EncoderModelState.Should().Be(state);
                vm.EncoderModelProgress.Should().Be(progress);
                break;
        }
    }

    [Fact]
    public void UpdateModelStatus_ShouldThrow_ForUnknownKey()
    {
        var vm = new StartupViewModel();

        var act = () => vm.UpdateModelStatus("unknown_model", "Ready", 100);

        act.Should().Throw<ArgumentException>()
           .WithParameterName("modelKey");
    }

    [Fact]
    public void UpdateModelStatus_ShouldClampProgress()
    {
        var vm = new StartupViewModel();

        vm.UpdateModelStatus("teeth", "Loading", 150); // over max
        vm.TeethModelProgress.Should().Be(100);

        vm.UpdateModelStatus("teeth", "Loading", -10); // under min
        vm.TeethModelProgress.Should().Be(0);
    }

    [Fact]
    public void UpdateModelStatus_ShouldHandleCaseInsensitiveKeys()
    {
        var vm = new StartupViewModel();

        vm.UpdateModelStatus("TEETH", "Ready", 100);
        vm.TeethModelState.Should().Be("Ready");

        vm.UpdateModelStatus(" Pathology ", "Verified", 80);
        vm.PathologyModelState.Should().Be("Verified");
    }

    // ── Derived model UI properties ──────────────────────────

    [Fact]
    public void ReadyModelsCount_ShouldCountOnlyReadyModels()
    {
        var vm = new StartupViewModel();
        vm.ReadyModelsCount.Should().Be(0);

        vm.UpdateModelStatus("teeth", "Ready", 100);
        vm.ReadyModelsCount.Should().Be(1);

        vm.UpdateModelStatus("pathology", "Ready", 100);
        vm.UpdateModelStatus("encoder", "Ready", 100);
        vm.ReadyModelsCount.Should().Be(3);
    }

    [Fact]
    public void ModelsSummary_ShouldFormatCorrectly()
    {
        var vm = new StartupViewModel();
        vm.ModelsSummary.Should().Be("0/3 models ready");

        vm.UpdateModelStatus("teeth", "Ready", 100);
        vm.ModelsSummary.Should().Be("1/3 models ready");
    }

    [Fact]
    public void HasModelError_ShouldBeTrueWhenAnyModelIsError()
    {
        var vm = new StartupViewModel();
        vm.HasModelError.Should().BeFalse();

        vm.UpdateModelStatus("encoder", "Error", 0);
        vm.HasModelError.Should().BeTrue();
    }

    [Fact]
    public void ShowLoadingIcon_ShouldBeTrueForValidatingOrLoading()
    {
        var vm = new StartupViewModel();

        vm.UpdateModelStatus("teeth", "Validating", 25);
        vm.TeethShowLoadingIcon.Should().BeTrue();

        vm.UpdateModelStatus("teeth", "Loading", 50);
        vm.TeethShowLoadingIcon.Should().BeTrue();

        vm.UpdateModelStatus("teeth", "Verified", 75);
        vm.TeethShowLoadingIcon.Should().BeTrue();

        vm.UpdateModelStatus("teeth", "Ready", 100);
        vm.TeethShowLoadingIcon.Should().BeFalse();
    }

    [Fact]
    public void CardOpacity_ShouldBe045_WhenPending_And1_Otherwise()
    {
        var vm = new StartupViewModel();
        vm.TeethModelOpacity.Should().Be(0.45); // default = Pending

        vm.UpdateModelStatus("teeth", "Loading", 50);
        vm.TeethModelOpacity.Should().Be(1.0);
    }

    // ── PropertyChanged events ────────────────────────────────

    [Fact]
    public void ProgressChange_ShouldRaiseReadinessProperties()
    {
        var vm = new StartupViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changed.Add(e.PropertyName);
        };

        vm.ProgressValue = 50;

        changed.Should().Contain(nameof(StartupViewModel.IsIntegrityReady));
        changed.Should().Contain(nameof(StartupViewModel.IsDatabaseReady));
        changed.Should().Contain(nameof(StartupViewModel.IsAiReady));
    }

    [Fact]
    public void ModelStateChange_ShouldRaiseModelSummaryProperties()
    {
        var vm = new StartupViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changed.Add(e.PropertyName);
        };

        vm.UpdateModelStatus("teeth", "Ready", 100);

        changed.Should().Contain(nameof(StartupViewModel.ReadyModelsCount));
        changed.Should().Contain(nameof(StartupViewModel.ModelsSummary));
        changed.Should().Contain(nameof(StartupViewModel.HasModelError));
    }
}
