using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using WidgetDesktop.Services;

namespace WidgetDesktop;

public partial class LoginWindow : Window
{
    private static readonly string ApiBaseUrl =
        Environment.GetEnvironmentVariable("WIDGET_API_BASE_URL") ?? "http://localhost:5183";

    private readonly WidgetApiClient _api = new(ApiBaseUrl);

    public event Action? AuthCompleted;

    public LoginWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await CheckIfAlreadyConfiguredAsync();
    }

    private async Task CheckIfAlreadyConfiguredAsync()
    {
        if (await _api.GetAuthStatusAsync())
            AuthCompleted?.Invoke();
    }

    private async void Connect_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        HideError();
        SetStatus("브라우저에서 Notion 로그인을 시작합니다...");

        try
        {
            var (ok, authUrl, error) = await _api.StartNotionOAuthAsync();
            if (!ok || string.IsNullOrWhiteSpace(authUrl))
            {
                SetStatus("");
                ShowError(error ?? "Notion 로그인 URL을 만들 수 없습니다.");
                ConnectButton.IsEnabled = true;
                return;
            }

            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
            SetStatus("Notion 승인 후 위젯용 데이터베이스를 만드는 중...");

            for (var i = 0; i < 180; i++)
            {
                await Task.Delay(1000);
                if (!await _api.GetAuthStatusAsync()) continue;

                SetStatus("연결 성공!");
                await Task.Delay(600);
                AuthCompleted?.Invoke();
                return;
            }

            SetStatus("");
            ShowError("Notion 연결 대기 시간이 초과되었습니다. 다시 시도해주세요.");
            ConnectButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            SetStatus("");
            ShowError("API 서버 연결 실패: " + ex.Message);
            ConnectButton.IsEnabled = true;
        }
    }

    private void SetStatus(string text)
    {
        StatusText.Text      = text;
        StatusText.IsVisible = text.Length > 0;
    }

    private void ShowError(string message)
    {
        ErrorText.Text     = message;
        ErrorBox.IsVisible = true;
    }

    private void HideError()
    {
        ErrorBox.IsVisible = false;
        ErrorText.Text     = "";
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.Shutdown();
}
