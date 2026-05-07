using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WidgetDesktop.Models;

public class ItemDto : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    private bool _isChecked;
    // ✅ 체크리스트 체크 여부 (API: isChecked)
    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; OnPropertyChanged(); }
    }

    private string? _status;
    public string? Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusSymbol)); }
    }

    public string StatusSymbol => Status?.Trim() switch
    {
        "완료"   => "✓",
        "진행 중" => "~",
        _        => "□"
    };

    private List<string> _days = new();
    public List<string> Days
    {
        get => _days;
        set { _days = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDays)); }
    }
    public bool HasDays => Days.Count > 0;

    private bool _showDays = true;
    public bool ShowDays
    {
        get => _showDays;
        set { _showDays = value; OnPropertyChanged(); }
    }

    private string? _note;
    public string? Note
    {
        get => _note;
        set { _note = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNote)); }
    }
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

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
