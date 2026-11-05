using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Egitmen.ViewModels;
using SinavSistemi.Egitmen.Views;

namespace SinavSistemi.Egitmen.Core;

public static class QuestionDialog
{
    /// <summary>Soru ekleme/düzenleme penceresini açar. İptalde null döner.</summary>
    public static Question? Edit(Question? existing, IEnumerable<string> categories)
    {
        var vm = new QuestionEditorViewModel(existing, categories);
        var win = new QuestionEditorWindow { DataContext = vm, Owner = Dialogs.Owner };
        vm.CloseRequested += ok => win.DialogResult = ok;
        return win.ShowDialog() == true ? vm.Result : null;
    }
}
