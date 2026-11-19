using System.Windows;
using Microsoft.Win32;
using MarketMakerSPBStandalone.TInvest;

namespace MarketMakerSPBStandalone.Gui;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _stop;
    private Task? _host;

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => _stop?.Cancel();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*" };
        if (dialog.ShowDialog() == true) ConfigPath.Text = dialog.FileName;
    }

    private void StartReadOnly_Click(object sender, RoutedEventArgs e) => Start(live: false);
    private void StartLive_Click(object sender, RoutedEventArgs e) => Start(live: true);

    private void Start(bool live)
    {
        if (_stop != null) return;
        LiveBotSettings config;
        try { config = LiveBotSettings.Load(ConfigPath.Text); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка конфигурации", MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
        if (live)
        {
            if (!config.LiveAuthorized(cliLiveSwitch: true))
            {
                MessageBox.Show("Торговля запрещена: не выполнены дополнительные подтверждения в конфигурации и переменной окружения.",
                    "Безопасность", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var answer = MessageBox.Show(
                "Реальная торговля: будут отправляться заявки на биржу с риском потери денег. " +
                "Прерывание связи может оставить активные заявки. Продолжить?",
                "Подтверждение реальной торговли", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }
        _stop = new CancellationTokenSource();
        ReadOnlyStart.IsEnabled = false;
        LiveStart.IsEnabled = false;
        StopButton.IsEnabled = true;
        Status.Text = live ? "РАБОТАЕТ: реальная торговля" : "Работает: только чтение";
        Append("Запуск: " + (live ? "LIVE" : "READ ONLY"));
        var host = new TInvestLiveHost(config, live, Append);
        var token = _stop.Token;
        _host = Task.Run(async () =>
        {
            try { await host.RunAsync(token); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Append("Ошибка: " + error); }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StopButton.IsEnabled = false;
                    ReadOnlyStart.IsEnabled = true;
                    LiveStart.IsEnabled = true;
                    Status.Text = "Остановлен. Проверьте открытые заявки на счёте.";
                    _stop?.Dispose();
                    _stop = null;
                    _host = null;
                });
            }
        });
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        Status.Text = "Остановка; ожидается обработка заявок...";
        _stop?.Cancel();
    }

    private void Append(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (LogBox.Text.Length > 120_000)
                LogBox.Text = LogBox.Text[^80_000..];
            LogBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + message +
                Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }
}
