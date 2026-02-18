using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;

namespace DentalID.Desktop.ViewModels;

public partial class OdontogramViewModel : ViewModelBase
{
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

    public void OnTreatmentDropped(int toothFdi, string treatmentName)
    {
        var tooth = Teeth.FirstOrDefault(t => t.FdiNumber == toothFdi);
        if (tooth != null)
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
        }

        // Q2: 21 -> 28
        for (int i = 21; i <= 28; i++) 
        {
            var t = new ToothViewModel(i);
            Teeth.Add(t);
            Quadrant2.Add(t);
        }

        // Q3: 31 -> 38
        for (int i = 31; i <= 38; i++) 
        {
            var t = new ToothViewModel(i);
            Teeth.Add(t);
            Quadrant3.Add(t);
        }

        // Q4: 48 -> 41
        for (int i = 48; i >= 41; i--) 
        {
            var t = new ToothViewModel(i);
            Teeth.Add(t);
            Quadrant4.Add(t);
        }
    }

    public void Update(AnalysisResult result)
    {
        // 1. Reset all
        foreach (var tooth in Teeth) tooth.Reset();

        // 2. Map detected teeth
        // Note: The AI might detect a tooth that isn't in our 1-32 range (e.g. baby teeth 51-85). 
        // We'll focus on permanent for now.
        
        foreach (var detected in result.Teeth)
        {
            var vm = Teeth.FirstOrDefault(t => t.FdiNumber == detected.FdiNumber);
            if (vm != null)
            {
                // Verify if it has linked pathologies (Smart Fusion)
                var pathologies = result.Pathologies
                    .Where(p => p.ToothNumber == detected.FdiNumber)
                    .ToList();

                if (pathologies.Any())
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
