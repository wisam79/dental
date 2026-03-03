using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;

namespace DentalID.Desktop.ViewModels;

public partial class OdontogramViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private ToothViewModel? _selectedTooth;

    public ObservableCollection<ToothViewModel> Teeth { get; } = new();

    public ObservableCollection<ToothViewModel> Quadrant1 { get; } = new(); // 18-11
    public ObservableCollection<ToothViewModel> Quadrant2 { get; } = new(); // 21-28
    public ObservableCollection<ToothViewModel> Quadrant3 { get; } = new(); // 31-38
    public ObservableCollection<ToothViewModel> Quadrant4 { get; } = new(); // 48-41

    public ObservableCollection<TreatmentItem> Treatments { get; } = new()
    {
        new TreatmentItem("Amalgam", "#A9A9A9", "Restoration"),
        new TreatmentItem("Resin", "#FFFFE0", "Restoration"),
        new TreatmentItem("Crown", "#FFD700", "Prosthetic"),
        new TreatmentItem("Extraction", "#FF0000", "Surgery"),
        new TreatmentItem("Implant", "#4682B4", "Surgery"),
        new TreatmentItem("Root Canal", "#8B4513", "Endodontic")
    };

    private readonly Dictionary<int, ToothViewModel> _teethMap = new();

    public OdontogramViewModel()
    {
        InitializeTeeth();
    }

    [RelayCommand]
    private void Reset()
    {
        foreach (var tooth in Teeth)
        {
            tooth.Reset();
        }
    }

    [RelayCommand]
    private void ApplyTreatment(object? args)
    {
        // Args will be tuple or specific wrapper in real Avalonia DnD
        // For MVVM simplicity without heavy DnD framework, we might bind the dropped data directly
        // But Avalonia DnD is event based in View code-behind usually.
        // We will expose a method that the View code-behind can call, or use behavior.
        // Let's assume View calls this.
    }

    public void SelectTooth(int fdiNumber)
    {
        if (!_teethMap.TryGetValue(fdiNumber, out var tooth)) return;

        // Toggle selection
        if (SelectedTooth?.FdiNumber == fdiNumber)
        {
            tooth.IsSelected = false;
            SelectedTooth = null;
        }
        else
        {
            if (SelectedTooth != null) SelectedTooth.IsSelected = false;
            tooth.IsSelected = true;
            SelectedTooth = tooth;
        }
    }

    public void OnTreatmentDropped(int toothFdi, string treatmentName)
    {
        if (_teethMap.TryGetValue(toothFdi, out var tooth))
        {
            // Simple logic for now: Change color based on treatment
            var treatment = Treatments.FirstOrDefault(t => t.Name == treatmentName);
            if (treatment != null)
            {
                tooth.MarkTreatment(treatment);
            }
        }
    }

    private void InitializeTeeth()
    {
        // Q1: 18 -> 11
        for (int i = 18; i >= 11; i--) 
        {
            var t = new ToothViewModel(i);
            Teeth.Add(t);
            Quadrant1.Add(t);
            _teethMap[i] = t;
        }

        // Q2: 21 -> 28
        for (int i = 21; i <= 28; i++) 
        {
            var t = new ToothViewModel(i);
            Teeth.Add(t);
            Quadrant2.Add(t);
            _teethMap[i] = t;
        }

        // Q3: 31 -> 38
        for (int i = 31; i <= 38; i++) 
        {
            var t = new ToothViewModel(i);
            Teeth.Add(t);
            Quadrant3.Add(t);
            _teethMap[i] = t;
        }

        // Q4: 48 -> 41
        for (int i = 48; i >= 41; i--) 
        {
            var t = new ToothViewModel(i);
            Teeth.Add(t);
            Quadrant4.Add(t);
            _teethMap[i] = t;
        }
    }

    public void Update(AnalysisResult result)
    {
        // 1. Reset all
        foreach (var tooth in Teeth) tooth.Reset();

        // 2. Map detected teeth
        var pathologyGroups = result.Pathologies
            .GroupBy(p => p.ToothNumber ?? 0)
            .ToDictionary(g => g.Key, g => g.ToList());
        
        foreach (var detected in result.Teeth)
        {
            if (_teethMap.TryGetValue(detected.FdiNumber, out var vm))
            {
                if (pathologyGroups.TryGetValue(detected.FdiNumber, out var pathologies) && pathologies.Count > 0)
                {
                    foreach (var p in pathologies)
                    {
                        vm.MarkPathology(p.ClassName);
                    }
                }
                else
                {
                    vm.MarkHealthy();
                }
            }
        }
    }
    
    public void Clear()
    {
        foreach (var tooth in Teeth) tooth.Reset();
    }

    public void Dispose()
    {
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}

public class TreatmentItem
{
    public string Name { get; }
    public string Color { get; }
    public string Category { get; }

    public TreatmentItem(string name, string color, string category)
    {
        Name = name;
        Color = color;
        Category = category;
    }
}



