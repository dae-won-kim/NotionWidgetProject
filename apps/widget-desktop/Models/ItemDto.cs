using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WidgetDesktop.Models;

public class ItemDto : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    private string? _status;
    public string? Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    private string? _statusId;
    public string? StatusId
    {
        get => _statusId;
        set { _statusId = value; OnPropertyChanged(); }
    }

    private string? _lastEditedTime;
    public string? LastEditedTime
    {
        get => _lastEditedTime;
        set { _lastEditedTime = value; OnPropertyChanged(); }
    }

    // ✅ Notion status.color (예: "blue", "green", "gray"...)
    private string? _statusColor;
    public string? StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(); }
    }

    // (선택) 드래그 정렬용 UI 순서가 이미 있다면 유지
    private int _uiOrder;
    public int UiOrder
    {
        get => _uiOrder;
        set { _uiOrder = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
