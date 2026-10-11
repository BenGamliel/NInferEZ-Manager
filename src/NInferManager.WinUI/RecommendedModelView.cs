using System.ComponentModel;
using Microsoft.UI.Xaml;
using NInferManager.Contracts;

namespace NInferManager.WinUI;

public sealed class RecommendedModelView : INotifyPropertyChanged
{
    private ModelItemView _selectedVariant;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string FamilyId { get; }
    public string DisplayName { get; }
    public IReadOnlyList<ModelItemView> Variants { get; }
    public Visibility VariantSelectorVisibility => Variants.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    public ModelItemView SelectedVariant
    {
        get => _selectedVariant;
        set
        {
            if (value is null || !Variants.Contains(value) || ReferenceEquals(value, _selectedVariant)) return;
            _selectedVariant = value;
            PropertyChanged?.Invoke(this, new(nameof(SelectedVariant)));
        }
    }

    public RecommendedModelView(IReadOnlyList<ModelItemView> variants, string? previousFile)
    {
        Variants = variants;
        var choice = ModelRecommendations.Select(variants.Select(x => x.Source).ToArray(), previousFile);
        _selectedVariant = variants.Single(x => x.FileName == choice.FileName);
        FamilyId = choice.RecommendationKey;
        DisplayName = string.IsNullOrWhiteSpace(choice.FamilyDisplayName) ? choice.DisplayName : choice.FamilyDisplayName;
    }
}
