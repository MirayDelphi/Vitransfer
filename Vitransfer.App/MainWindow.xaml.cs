using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Diagnostics;
using Vitransfer.Core;

namespace Vitransfer.App;

public partial class MainWindow : Window {
    private IDeviceConnection? left, right;
    private CancellationTokenSource? cancellation;
    private bool busy;
    private AppSettings settings = new();
    private readonly string settingsPath = Path.Combine(AppPaths.Root, "settings.json");
    public MainWindow() {
        InitializeComponent();
        try {
            if (File.Exists(settingsPath)) {
                settings = AppSettings.Parse(File.ReadAllText(settingsPath));
                var values = settings.Addresses;
                if (values?.Length == 2) { LeftAddress.Text = values[0]; RightAddress.Text = values[1]; }
            }
        } catch { /* Defaults are usable if settings are corrupt. */ }
        RefreshRecentAddresses();
        HideApps.IsChecked = settings.HideApps;
        SaveSettings();
    }
    private void SetBusy(bool value) {
        busy = value;
        LeftGo.IsEnabled = RightGo.IsEnabled = LeftAddress.IsEnabled = RightAddress.IsEnabled = IncludeBase.IsEnabled = !value;
        LeftGames.IsEnabled = RightGames.IsEnabled = !value;
        HideApps.IsEnabled = !value;
        Cancel.IsEnabled = value;
        LeftCalculate.IsEnabled = !value && left?.IsConnected == true;
        RightCalculate.IsEnabled = !value && right?.IsConnected == true;
        UpdateButtons();
    }
    private void RefreshRecentAddresses() {
        // Replacing ItemsSource can change an editable ComboBox's Text.
        string leftText = LeftAddress.Text, rightText = RightAddress.Text;
        LeftAddress.ItemsSource = settings.RecentAddresses;
        RightAddress.ItemsSource = settings.RecentAddresses;
        LeftAddress.Text = leftText; RightAddress.Text = rightText;
    }
    private void UpdateButtons() {
        if (CopyRight == null || CopyLeft == null) return;
        bool ready = !busy && left?.IsConnected == true && right?.IsConnected == true && left.DeviceKey != right.DeviceKey;
        CopyRight.IsEnabled = ready && LeftGames.SelectedItems.Count > 0;
        CopyLeft.IsEnabled = ready && RightGames.SelectedItems.Count > 0;
    }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void GameItem_Loaded(object sender, RoutedEventArgs e) {
        if (sender is not ListBoxItem item || item.ContextMenu != null) return;
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open in explorer" };
        var delete = new MenuItem { Header = "Delete" };
        var dlc = new MenuItem { Header = "Show DLC" };
        dlc.Click += ShowDlc_Click;
        open.Click += OpenGameFolder_Click;
        delete.Click += DeleteGame_Click;
        menu.Items.Add(open); menu.Items.Add(dlc); menu.Items.Add(delete);
        menu.Opened += GameMenu_Opened;
        item.ContextMenu = menu;
    }
    private void GameRightClick(object sender, MouseButtonEventArgs e) {
        if (busy) { e.Handled = true; return; }
        if (sender is ListBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) is ListBox list) {
            list.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }
    private (IDeviceConnection Connection, ListBox List, Game Game)? MenuTarget(ContextMenu? menu) {
        if (menu?.PlacementTarget is not ListBoxItem item || item.DataContext is not Game game ||
            ItemsControl.ItemsControlFromItemContainer(item) is not ListBox list) return null;
        var connection = list == LeftGames ? left : list == RightGames ? right : null;
        return connection?.IsConnected == true ? (connection, list, game) : null;
    }
    private void GameMenu_Opened(object sender, RoutedEventArgs e) {
        if (sender is ContextMenu menu) {
            var target = MenuTarget(menu);
            foreach (MenuItem item in menu.Items) {
                item.IsEnabled = !busy && target != null;
                if (Equals(item.Header, "Show DLC")) item.Visibility = target?.Game.HasDlc == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
    private void ShowDlc_Click(object sender, RoutedEventArgs e) {
        var target = MenuTarget((sender as MenuItem)?.Parent as ContextMenu);
        if (busy || target == null) return;
        var (connection, _, game) = target.Value;
        new DlcDialog(connection, game) { Owner = this }.ShowDialog();
    }
    private async void OpenGameFolder_Click(object sender, RoutedEventArgs e) {
        var target = MenuTarget((sender as MenuItem)?.Parent as ContextMenu);
        if (busy || target == null) return;
        var (connection, _, game) = target.Value;
        SetBusy(true); cancellation = new CancellationTokenSource();
        try {
            if (!await connection.Exists(connection.AppRoot + "/" + game.Id, cancellation.Token)) throw new IOException("Game folder no longer exists. Click Go to refresh.");
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add(connection.ExplorerFolder(game.Id));
            Process.Start(start);
        } catch (OperationCanceledException) { Status.Text = "Opening folder cancelled."; }
        catch (Exception ex) { Status.Text = "Cannot open folder: " + ex.Message; }
        finally { cancellation.Dispose(); cancellation = null; SetBusy(false); }
    }
    private async void DeleteGame_Click(object sender, RoutedEventArgs e) {
        var target = MenuTarget((sender as MenuItem)?.Parent as ContextMenu);
        if (busy || target == null) return;
        var (connection, _, game) = target.Value;
        var dialog = new DeleteGameDialog(game) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        SetBusy(true); cancellation = new CancellationTokenSource(); Progress.IsIndeterminate = true;
        string result;
        try {
            await GameDeletion.Delete(connection, game.Id, dialog.KeepSaves, new Progress<string>(message => Status.Text = message), cancellation.Token);
            result = dialog.KeepSaves ? "Game and related content deleted. Saves kept." : "Game and all related content deleted, including saves.";
        } catch (OperationCanceledException) { result = "Deletion cancelled. Some folders may already have been deleted."; }
        catch (Exception ex) { result = "Deletion failed: " + ex.Message; }
        try {
            // Refresh both columns if they point to this same address or root.
            foreach (var side in new[] { (Connection: left, List: LeftGames, Storage: LeftStorage), (Connection: right, List: RightGames, Storage: RightStorage) }) {
                if (side.Connection?.DeviceKey != connection.DeviceKey) continue;
                try {
                    ShowGames(side.List, await side.Connection.Scan(new Progress<string>(message => Status.Text = message), CancellationToken.None));
                    side.Storage.Text = DeviceStorageSummary.Display(side.Connection.CachedMetadata.Storage, side.List.ItemsSource!.Cast<Game>());
                } catch {
                    side.List.ItemsSource = null;
                    side.Storage.Text = "Total calculated size: N/A";
                    result += " Click Go to refresh the connection.";
                }
            }
            Status.Text = result;
        } finally {
            Progress.IsIndeterminate = false;
            cancellation.Dispose(); cancellation = null; SetBusy(false);
        }
    }
    private void HideApps_Changed(object sender, RoutedEventArgs e) {
        settings.HideApps = HideApps.IsChecked == true;
        foreach (var list in new[] { LeftGames, RightGames }) {
            if (list?.ItemsSource != null) CollectionViewSource.GetDefaultView(list.ItemsSource).Refresh();
        }
        SaveSettings();
        UpdateButtons();
    }
    private void ShowGames(ListBox list, IEnumerable<Game> games) {
        list.ItemsSource = games.ToList();
        CollectionViewSource.GetDefaultView(list.ItemsSource).Filter = item => settings.IsGameVisible(((Game)item).Id);
    }
    private async void LeftGo_Click(object sender, RoutedEventArgs e) => await Load(true);
    private async void RightGo_Click(object sender, RoutedEventArgs e) => await Load(false);
    private async Task Load(bool isLeft) {
        SetBusy(true);
        cancellation = new CancellationTokenSource();
        var list = isLeft ? LeftGames : RightGames;
        var address = isLeft ? LeftAddress.Text : RightAddress.Text;
        var storage = isLeft ? LeftStorage : RightStorage;
        storage.Text = "Total calculated size: N/A";
        if (isLeft) { left?.Dispose(); left = null; } else { right?.Dispose(); right = null; }
        list.ItemsSource = null;
        IDeviceConnection? connection = null;
        Progress.IsIndeterminate = true;
        try {
            Status.Text = "Connecting…";
            connection = DeviceConnection.Create(address.Trim(), IconImageConverter.IsReadable);
            await connection.Connect(cancellation.Token);
            ShowGames(list, await connection.Scan(new Progress<string>(s => Status.Text = s), cancellation.Token));
            storage.Text = DeviceStorageSummary.Display(connection.CachedMetadata.Storage, list.ItemsSource!.Cast<Game>());
            if (isLeft) left = connection; else right = connection;
            connection = null;
            settings.RememberSuccessfulAddress(address);
            RefreshRecentAddresses();
            Status.Text = $"Address {(isLeft ? 1 : 2)} connected · {list.Items.Count} games found.";
            SaveSettings();
        } catch (OperationCanceledException) { Status.Text = "Connection cancelled."; storage.Text = "Total calculated size: N/A"; }
        catch (Exception ex) { Status.Text = "Connection failed: " + ex.Message; storage.Text = "Total calculated size: N/A"; }
        finally { connection?.Dispose(); Progress.IsIndeterminate = false; cancellation.Dispose(); cancellation = null; SetBusy(false); }
    }
    private async void CopyRight_Click(object sender, RoutedEventArgs e) => await Copy(true);
    private async void LeftCalculate_Click(object sender, RoutedEventArgs e) => await Calculate(true);
    private async void RightCalculate_Click(object sender, RoutedEventArgs e) => await Calculate(false);
    private async Task Calculate(bool isLeft) {
        var connection = isLeft ? left : right;
        var list = isLeft ? LeftGames : RightGames;
        var storage = isLeft ? LeftStorage : RightStorage;
        if (busy || connection?.IsConnected != true) return;
        var games = list.ItemsSource!.Cast<Game>().ToArray();
        var selected = list.SelectedItems.Cast<Game>().Select(g => g.Id).ToHashSet();
        SetBusy(true);
        cancellation = new CancellationTokenSource();
        Progress.IsIndeterminate = true;
        try {
            await connection.CalculateSizes(games, new Progress<string>(s => Status.Text = s), cancellation.Token);
            Status.Text = "Sizes refreshed and saved for this address or folder.";
        } catch (OperationCanceledException) { Status.Text = "Calculation cancelled. Completed sizes remain cached."; }
        catch (Exception ex) { Status.Text = "Size calculation failed: " + ex.Message; }
        finally {
            var metadata = connection.CachedMetadata;
            ShowGames(list, games.Select(metadata.Apply));
            foreach (Game game in list.Items) if (selected.Contains(game.Id)) list.SelectedItems.Add(game);
            storage.Text = DeviceStorageSummary.Display(metadata.Storage, list.ItemsSource!.Cast<Game>());
            Progress.IsIndeterminate = false;
            cancellation.Dispose(); cancellation = null;
            SetBusy(false);
        }
    }
    private async void CopyLeft_Click(object sender, RoutedEventArgs e) => await Copy(false);
    private async Task Copy(bool toRight) {
        var source = toRight ? left : right;
        var destination = toRight ? right : left;
        var selected = (toRight ? LeftGames : RightGames).SelectedItems.Cast<Game>().ToArray();
        if (source == null || destination == null || selected.Length == 0) return;
        bool includeBase = IncludeBase.IsChecked == true;
        var answer = MessageBox.Show(this, $"Copy {(includeBase ? "base games, DLC, updates, patches and saves" : "saves only")} for {selected.Length} selected game(s) to Address {(toRight ? 2 : 1)}?\n\nIf a destination folder exists:\nYes — replace it after making a local backup.\nNo — skip it.\nCancel — do not transfer.\n\nRaw saves may require import/resigning on the destination. Base games may require a LiveArea refresh. Close the selected games on both devices.", "Transfer options", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Cancel) return;
        SetBusy(true);
        cancellation = new CancellationTokenSource();
        Progress.Value = 0;
        try {
            string summary = await new TransferEngine().Transfer(source, destination, selected, includeBase, answer == MessageBoxResult.Yes ? ConflictPolicy.ReplaceWithBackup : ConflictPolicy.Skip,
                new Progress<TransferUpdate>(u => { Status.Text = u.Message; Progress.Value = Math.Clamp(u.Percent, 0, 100); }), cancellation.Token);
            Status.Text = "Refreshing destination…";
            ShowGames(toRight ? RightGames : LeftGames, await destination.Scan(new Progress<string>(s => Status.Text = s), cancellation.Token));
            (toRight ? RightStorage : LeftStorage).Text = DeviceStorageSummary.Display(destination.CachedMetadata.Storage,
                (toRight ? RightGames : LeftGames).ItemsSource!.Cast<Game>());
            Status.Text = summary;
            Progress.Value = 100;
        } catch (Exception ex) { Status.Text = ex.Message; }
        finally { cancellation.Dispose(); cancellation = null; SetBusy(false); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { cancellation?.Cancel(); Cancel.IsEnabled = false; Status.Text = "Cancelling…"; }
    private void SaveSettings() {
        try {
            settings.Addresses = [AppSettings.AddressForStorage(LeftAddress.Text), AppSettings.AddressForStorage(RightAddress.Text)];
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(settingsPath, settings.ToJson());
        } catch { }
    }
    private void Window_Closing(object? sender, CancelEventArgs e) {
        if (busy) { e.Cancel = true; Status.Text = "Cancel the current operation before closing."; return; }
        left?.Dispose(); right?.Dispose();
    }
}
