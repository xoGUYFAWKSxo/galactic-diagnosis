using System.Windows.Controls;
using System.Windows.Input;

namespace GalacticDiagnosis.Gui.Views;

public partial class NewCheckView : UserControl
{
    public NewCheckView()
    {
        InitializeComponent();
        Loaded += (_, _) => Keyboard.Focus(CustomerBox);
    }
}
