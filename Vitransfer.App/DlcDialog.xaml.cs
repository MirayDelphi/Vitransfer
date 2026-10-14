using System.ComponentModel;
using System.Windows;
using Vitransfer.Core;

namespace Vitransfer.App;

public partial class DlcDialog : Window {
    private readonly IDeviceConnection connection;
    private readonly Game game;
    private readonly CancellationTokenSource cancellation = new();
    private bool closed;
    private bool reading = true;
    public DlcDialog(IDeviceConnection connection, Game game) {
        InitializeComponent(); this.connection = connection; this.game = game;
        GameName.Text = $"{game.Name} ({game.Id})";
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e) {
        try {
            var entries = await connection.ReadDlcs(game.Id, cancellation.Token);
            if (closed) return;
            Dlcs.ItemsSource = entries;
            Status.Text = entries.Count == 0 ? "No DLC folders found." : $"{entries.Count} DLC folders found.";
        } catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed) Status.Text = "Cannot read DLC: " + ex.Message; }
        finally {
            reading = false;
            if (closed) Close();
        }
    }
    private void Window_Closing(object? sender, CancelEventArgs e) {
        closed = true;
        if (reading) { e.Cancel = true; cancellation.Cancel(); Status.Text = "Cancelling…"; }
        else cancellation.Dispose();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
