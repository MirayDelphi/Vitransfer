using System.Windows;
using Vitransfer.Core;

namespace Vitransfer.App;

public partial class DeleteGameDialog : Window {
    public bool KeepSaves { get; private set; }
    public DeleteGameDialog(Game game) {
        InitializeComponent();
        GameName.Text = game.Name + " (" + game.Id + ")";
        Loaded += (_, _) => CancelButton.Focus();
    }
    private void KeepSaves_Click(object sender, RoutedEventArgs e) { KeepSaves = true; DialogResult = true; }
    private void Delete_Click(object sender, RoutedEventArgs e) { KeepSaves = false; DialogResult = true; }
}
