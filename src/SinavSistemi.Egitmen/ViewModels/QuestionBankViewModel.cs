using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using SinavSistemi.Shared.Core;
using SinavSistemi.Egitmen.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Egitmen.Services;

namespace SinavSistemi.Egitmen.ViewModels;

public class QuestionBankViewModel : ObservableObject, IPageLifecycle
{
    public const string AllCategories = "Tüm kategoriler";
    public const string AllTypes = "Tüm türler";

    private string _searchText = "";
    private string _selectedCategory = AllCategories;
    private string _selectedType = AllTypes;
    private Question? _selected;

    public QuestionBankViewModel()
    {
        View = CollectionViewSource.GetDefaultView(Questions);
        View.Filter = Filter;

        AddCommand = new AsyncRelayCommand(AddAsync);
        EditCommand = new AsyncRelayCommand(p => EditAsync(p as Question ?? Selected), p => (p as Question ?? Selected) is not null);
        DeleteCommand = new AsyncRelayCommand(p => DeleteAsync(p as Question ?? Selected), p => (p as Question ?? Selected) is not null);

        _ = OnNavigatedToAsync();
    }

    public ObservableCollection<Question> Questions { get; } = new();
    public ICollectionView View { get; }
    public ObservableCollection<string> Categories { get; } = new();
    public string[] Types { get; } = { AllTypes, "Çoktan Seçmeli", "Klasik", "Kodlama (C)" };

    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) View.Refresh(); } }
    public string SelectedCategory { get => _selectedCategory; set { if (SetProperty(ref _selectedCategory, value ?? AllCategories)) View.Refresh(); } }
    public string SelectedType { get => _selectedType; set { if (SetProperty(ref _selectedType, value ?? AllTypes)) View.Refresh(); } }
    public Question? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public int TotalCount => Questions.Count;

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    public async Task OnNavigatedToAsync()
    {
        Questions.Clear();
        foreach (var q in await ServiceLocator.Questions.GetAllAsync()) Questions.Add(q);

        var current = SelectedCategory;
        Categories.Clear();
        Categories.Add(AllCategories);
        foreach (var c in await ServiceLocator.Questions.GetCategoriesAsync()) Categories.Add(c);
        SelectedCategory = Categories.Contains(current) ? current : AllCategories;

        OnPropertyChanged(nameof(TotalCount));
    }

    private bool Filter(object o)
    {
        if (o is not Question q) return false;
        if (SelectedCategory != AllCategories && q.Category != SelectedCategory) return false;
        if (SelectedType != AllTypes &&
            SinavSistemi.Shared.Converters.EnumDisplayConverter.ToDisplay(q.Type) != SelectedType) return false;
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !q.Text.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)) return false;
        return true;
    }

    private async Task AddAsync()
    {
        var q = QuestionDialog.Edit(null, Categories.Where(c => c != AllCategories));
        if (q is null) return;
        await ServiceLocator.Questions.AddAsync(q);
        await OnNavigatedToAsync();
    }

    private async Task EditAsync(Question? q)
    {
        if (q is null) return;
        var edited = QuestionDialog.Edit(q, Categories.Where(c => c != AllCategories));
        if (edited is null) return;
        await ServiceLocator.Questions.UpdateAsync(edited);
        await OnNavigatedToAsync();
    }

    private async Task DeleteAsync(Question? q)
    {
        if (q is null) return;
        if (!Dialogs.Confirm($"\"{Shorten(q.Text)}\" sorusu silinsin mi?\nBu soru eski sınavlarda kullanıldıysa sonuçlar etkilenmez.")) return;
        await ServiceLocator.Questions.DeleteAsync(q.Id);
        await OnNavigatedToAsync();
    }

    private static string Shorten(string s) => s.Length > 60 ? s[..60] + "…" : s;
}
