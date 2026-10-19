using System.Windows.Controls;
using System.Windows.Input;
using GalacticDiagnosis.Gui.ViewModels;

namespace GalacticDiagnosis.Gui.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    HistoryViewModel? Vm => DataContext as HistoryViewModel;

    // A ListBox's SelectedItems can't be bound, so the view hands the selection over.
    void List_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm?.SetSelection(List.SelectedItems.Cast<HistoryRow>());

    void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.OpenCommand.CanExecute(null) == true) Vm.OpenCommand.Execute(null);
    }
}
