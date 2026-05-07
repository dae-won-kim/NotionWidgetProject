using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WidgetDesktop.Models;
using WidgetDesktop.Services;
using WidgetDesktop.Styles;

namespace WidgetDesktop;

public partial class MainWindow : Window
{
    // ── Constants ─────────────────────────────────────────────────────
    private const string WidgetId = "w_1";

    private static readonly string ApiBaseUrl =
        Environment.GetEnvironmentVariable("WIDGET_API_BASE_URL") ?? "http://localhost:5183";

    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "NotionWidget", "window.json");

    // ── Services ──────────────────────────────────────────────────────
    private readonly WidgetApiClient _api = new(ApiBaseUrl);

    // ── State ─────────────────────────────────────────────────────────
    private readonly ObservableCollection<ItemDto> _items = new();
    private List<ItemDto>              _allItems        = new();
    private Dictionary<string, string> _statusIdToColor = new();
    private List<StatusOptionDto>      _statusOptions   = new();
    private string                     _selectedDay     = "";

    // Drag-drop state
    private ItemDto? _draggingItem;
    private bool     _hasManualOrder;
    private bool     _isMoving;

    // Tray/close state
    private bool _allowClose;

    // Theme swipe state
    private double _themeSwipeStartX;

    // ── Filter button map (evaluated after InitializeComponent) ───────
    private IEnumerable<(Button Btn, string Tag)> FilterButtons => new[]
    {
        (FilterAll, ""),       (FilterMon, "월요일"),
        (FilterTue, "화요일"), (FilterWed, "수요일"),
        (FilterThu, "목요일"), (FilterFri, "금요일"),
        (FilterSat, "토요일"), (FilterSun, "일요일"),
    };

    // ── Constructor ───────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
        UpdateThemeBtnStates();
        LoadWindowState();

        TodoItems.ItemsSource = _items;
        _items.Add(new ItemDto { Id = "loading", Title = "(불러오는 중...)", Status = "" });
        ErrorText.IsVisible = false;

        _selectedDay = GetTodayFilter();
        UpdateDateLabel();

        Opened += async (_, _) => await LoadItemsAsync();
    }

    // ── Window lifecycle ──────────────────────────────────────────────

    public void AllowClose() => _allowClose = true;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose) { e.Cancel = true; SaveWindowState(); Hide(); }
        base.OnClosing(e);
    }

    public void SaveWindowState()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new { x = Position.X, y = Position.Y, w = (int)Width, h = (int)Height });
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    private void LoadWindowState()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var r = JsonDocument.Parse(File.ReadAllText(SettingsPath)).RootElement;
            Position = new PixelPoint(r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32());
            Width    = r.GetProperty("w").GetDouble();
            Height   = r.GetProperty("h").GetDouble();
        }
        catch { }
    }

    // ── Data loading ──────────────────────────────────────────────────

    private async Task LoadItemsAsync()
    {
        _hasManualOrder     = false;
        ErrorText.IsVisible = false;
        ErrorText.Text      = "";

        try
        {
            var data         = await _api.QueryItemsAsync(WidgetId);
            _statusOptions   = data.StatusOptions.ToList();
            _statusIdToColor = BuildColorMap(data.StatusOptions);

            _allItems = data.Items
                .Select(it => { it.StatusColor = ResolveColor(it.StatusId); return it; })
                .ToList();

            SortAllItemsByStatusIfNeeded();
            RefreshDisplay();
        }
        catch (Exception ex)
        {
            ErrorText.IsVisible = true;
            ErrorText.Text      = $"로드 실패: {ex.Message}";
            _allItems.Clear();
            _items.Clear();
            _items.Add(new ItemDto { Id = "err", Title = "(항목 없음)", Status = "" });
        }
    }

    // ── Title-bar / window drag ───────────────────────────────────────

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (IsDescendantOf<Button>(e.Source as Control)) return;
        BeginMoveDrag(e);
    }

    // ── Resize handles ────────────────────────────────────────────────

    private void ResizeBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (sender is not Control c) return;
        var edge = ParseWindowEdge(c.Tag?.ToString());
        if (edge.HasValue) BeginResizeDrag(edge.Value, e);
    }

    // ── Title-bar buttons ─────────────────────────────────────────────

    private async void Refresh_Click(object? sender, RoutedEventArgs e)  => await LoadItemsAsync();
    private void       Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Pin_Click(object? sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        SetClass(sender as Button, "active", Topmost);
    }

    private void HideToTray_Click(object? sender, RoutedEventArgs e) { SaveWindowState(); Hide(); }

    private void ThemeLightBtn_Click(object? sender, RoutedEventArgs e) => SetTheme(ThemeVariant.Light);
    private void ThemeDarkBtn_Click(object? sender, RoutedEventArgs e)  => SetTheme(ThemeVariant.Dark);

    private void ThemeSwipe_PointerPressed(object? sender, PointerPressedEventArgs e)
        => _themeSwipeStartX = e.GetPosition(this).X;

    private void ThemeSwipe_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        double dx = e.GetPosition(this).X - _themeSwipeStartX;
        if (Math.Abs(dx) < 15) return;
        SetTheme(dx > 0 ? ThemeVariant.Light : ThemeVariant.Dark);
    }

    private void SetTheme(ThemeVariant variant)
    {
        Application.Current!.RequestedThemeVariant = variant;
        UpdateThemeBtnStates();
    }

    private void UpdateThemeBtnStates()
    {
        bool isDark = Application.Current!.RequestedThemeVariant == ThemeVariant.Dark;
        SetClass(ThemeLightBtn, "active", !isDark);
        SetClass(ThemeDarkBtn,  "active",  isDark);
    }

    // ── Day filter ────────────────────────────────────────────────────

    private void DayFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        _selectedDay = b.Tag?.ToString() ?? "";
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        ApplyDayFilter();
        UpdateFilterButtons();
    }

    private void ApplyDayFilter()
    {
        var source = string.IsNullOrEmpty(_selectedDay)
            ? _allItems
            : _allItems.Where(it => it.Days.Contains(_selectedDay)).ToList();

        _items.Clear();
        foreach (var it in source) _items.Add(it);
    }

    private void UpdateFilterButtons()
    {
        foreach (var (btn, tag) in FilterButtons)
            SetClass(btn, "active", tag == _selectedDay);
    }

    private static string GetTodayFilter() => DateTime.Today.DayOfWeek switch
    {
        DayOfWeek.Monday    => "월요일",
        DayOfWeek.Tuesday   => "화요일",
        DayOfWeek.Wednesday => "수요일",
        DayOfWeek.Thursday  => "목요일",
        DayOfWeek.Friday    => "금요일",
        DayOfWeek.Saturday  => "토요일",
        DayOfWeek.Sunday    => "일요일",
        _                   => ""
    };

    private void UpdateDateLabel()
    {
        var t = DateTime.Today;
        DateLabel.Text = $"{t.Year}년 {t.Month}월 {t.Day}일 · {GetTodayFilter()}";
    }

    // ── Status buttons ────────────────────────────────────────────────

    // 오른쪽 체크 버튼: 클릭마다 다음 상태로 순환
    private async void StatusButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.DataContext is not ItemDto item) return;
        ApplyStatusUpdate(item, await _api.StatusNextAsync(WidgetId, item.Id));
        e.Handled = true;
    }

    // 왼쪽 상태 배지: 클릭하면 다른 상태 선택 팝업 표시
    private void StatusBadge_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.DataContext is not ItemDto item) return;
        ShowStatusFlyout(b, item);
        e.Handled = true;
    }

    private void StatusButton_PointerPressed(object? sender, PointerPressedEventArgs e)
        => (sender as Button)?.Classes.Add("pressed");

    private void StatusButton_PointerReleased(object? sender, PointerReleasedEventArgs e)
        => (sender as Button)?.Classes.Remove("pressed");

    private void StatusButton_PointerEntered(object? sender, PointerEventArgs e)
        => (sender as Button)?.Classes.Add("hover");

    private void StatusButton_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Button b) { b.Classes.Remove("hover"); b.Classes.Remove("pressed"); }
    }

    private void ShowStatusFlyout(Button anchor, ItemDto item)
    {
        var others = _statusOptions
            .Where(o => !string.Equals(o.Name?.Trim(), item.Status?.Trim(),
                        StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (others.Count == 0) return;

        var stack = new StackPanel { Spacing = 5 };
        Flyout? flyout = null;

        foreach (var opt in others)
        {
            var optCapture = opt;
            var btn = new Button
            {
                Content                    = opt.Name,
                Background                 = StatusColorToBrush(opt.Color),
                Foreground                 = new SolidColorBrush(WidgetTheme.GetStatusFg(opt.Color)),
                BorderThickness            = new Thickness(0),
                CornerRadius               = new CornerRadius(6),
                Padding                    = new Thickness(14, 7),
                FontSize                   = 12,
                FontWeight                 = FontWeight.Bold,
                Cursor                     = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment        = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                MinWidth                   = 90,
            };
            btn.Click += async (_, _) =>
            {
                flyout?.Hide();
                var updated = await _api.StatusSetAsync(WidgetId, item.Id, optCapture.Id);
                ApplyStatusUpdate(item, updated);
            };
            stack.Children.Add(btn);
        }

        flyout = new Flyout
        {
            Content   = stack,
            Placement = PlacementMode.Bottom,
        };
        flyout.ShowAt(anchor);
    }

    private static SolidColorBrush StatusColorToBrush(string? colorName)
        => new SolidColorBrush(colorName?.ToLowerInvariant() switch
        {
            "blue"   => WidgetTheme.StatusBlue,
            "green"  => WidgetTheme.StatusGreen,
            "yellow" => WidgetTheme.StatusYellow,
            "red"    => WidgetTheme.StatusRed,
            "gray"   => WidgetTheme.StatusGray,
            _        => WidgetTheme.StatusDefault
        });

    private void ApplyStatusUpdate(ItemDto item, StatusUpdateResponseDto updated)
    {
        item.Status         = updated.Status;
        item.StatusId       = updated.StatusId;
        item.LastEditedTime = updated.LastEditedTime;
        item.StatusColor    = ResolveColor(item.StatusId);
        SortAllItemsByStatus();
        RefreshDisplay();
        ClearStatusButtonEffects();
    }

    // ── Drag-drop reorder ─────────────────────────────────────────────

    private void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (sender is Control c && c.DataContext is ItemDto item)
        {
            _draggingItem = item;
            if (FindItemContainer(c).Container is ListBoxItem lbi)
                lbi.Classes.Add("dragging");
            e.Handled = true;
        }
    }

    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingItem == null || _isMoving) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _draggingItem = null; return; }

        var hit = TodoItems.InputHitTest(e.GetPosition(TodoItems)) as Avalonia.Visual;
        if (hit == null) return;

        var (targetItem, targetContainer) = FindItemContainer(hit);
        if (targetItem == null || targetItem == _draggingItem || targetContainer == null) return;

        int from = _items.IndexOf(_draggingItem);
        int to   = _items.IndexOf(targetItem);
        if (from < 0 || to < 0) return;

        double prog = e.GetPosition(targetContainer).Y / targetContainer.Bounds.Height;
        if (from < to ? prog > 0.5 : prog < 0.5) { _isMoving = true; ApplyFlipAnimation(from, to); }
    }

    private void ApplyFlipAnimation(int fromIndex, int toIndex)
    {
        var snapshot = CaptureBoundsSnapshot();

        _items.Move(fromIndex, toIndex);
        _hasManualOrder = true;
        SyncMoveToAllItems(toIndex);
        ReindexUiOrderAll();

        Dispatcher.UIThread.Post(() =>
        {
            var toAnimate = ComputeTranslations(snapshot);
            if (toAnimate.Count > 0) CommitAnimation(toAnimate);
            else                     _isMoving = false;
            MarkDraggingItem();
        }, DispatcherPriority.Input);
    }

    private Dictionary<ItemDto, double> CaptureBoundsSnapshot()
    {
        var snap = new Dictionary<ItemDto, double>();
        foreach (var item in _items)
        {
            var c = TodoItems.ContainerFromItem(item);
            if (c != null) snap[item] = c.Bounds.Y;
        }
        return snap;
    }

    private List<(ListBoxItem Lbi, bool IsDragging)> ComputeTranslations(Dictionary<ItemDto, double> snapshot)
    {
        var result = new List<(ListBoxItem, bool)>();
        foreach (var item in _items)
        {
            if (TodoItems.ContainerFromItem(item) is not ListBoxItem lbi) continue;
            if (!snapshot.TryGetValue(item, out double oldY)) continue;
            double delta = oldY - lbi.Bounds.Y;
            if (Math.Abs(delta) <= 0.5) continue;

            lbi.Classes.Remove("animating");
            lbi.RenderTransform = item == _draggingItem
                ? TransformOperations.Parse($"translate(0px,{delta}px) scale(1.03)")
                : TransformOperations.Parse($"translate(0px,{delta}px)");
            result.Add((lbi, item == _draggingItem));
        }
        return result;
    }

    private void CommitAnimation(List<(ListBoxItem Lbi, bool IsDragging)> toAnimate)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var scaleUp     = TransformOperations.Parse("scale(1.03)");
            var noTransform = TransformOperations.Parse("none");

            foreach (var (lbi, isDragging) in toAnimate)
            {
                lbi.Classes.Add("animating");
                lbi.RenderTransform = isDragging ? scaleUp : noTransform;
            }

            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(16);
                _isMoving = false;
                await Task.Delay(350);
                foreach (var (lbi, _) in toAnimate)
                {
                    lbi.Classes.Remove("animating");
                    lbi.RenderTransform = noTransform;
                }
            }, DispatcherPriority.Background);

        }, DispatcherPriority.Render);
    }

    private void MarkDraggingItem()
    {
        if (_draggingItem != null &&
            TodoItems.ContainerFromItem(_draggingItem) is ListBoxItem drag)
            drag.Classes.Add("dragging");
    }

    private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e) => EndDrag();
    private void List_PointerReleased(object? sender, PointerReleasedEventArgs e)    => EndDrag();

    private void EndDrag()
    {
        ClearDraggingClasses();
        _draggingItem = null;
    }

    private void ClearDraggingClasses()
    {
        var none = TransformOperations.Parse("none");
        foreach (var lbi in TodoItems.GetVisualDescendants().OfType<ListBoxItem>())
        {
            lbi.Classes.Remove("dragging");
            lbi.RenderTransform = none;
        }
    }

    // ── Data helpers ──────────────────────────────────────────────────

    private string ResolveColor(string? statusId)
        => statusId != null && _statusIdToColor.TryGetValue(statusId, out var c) ? c : "gray";

    private static Dictionary<string, string> BuildColorMap(IEnumerable<StatusOptionDto> options)
        => options.Where(o => !string.IsNullOrWhiteSpace(o.Id))
                  .ToDictionary(o => o.Id, o => o.Color ?? "gray");

    private void SortAllItemsByStatus()
    {
        _allItems = _allItems
            .Select((it, i) => (it, i, pri: GetStatusPriority(it.Status)))
            .OrderBy(x => x.pri).ThenBy(x => x.i)
            .Select(x => x.it)
            .ToList();
    }

    private void SortAllItemsByStatusIfNeeded()
    {
        if (!_hasManualOrder) SortAllItemsByStatus();
    }

    private void SyncMoveToAllItems(int toIndex)
    {
        if (string.IsNullOrEmpty(_selectedDay))
        {
            _allItems = _items.ToList();
            return;
        }

        // Filtered view: relocate moved item in _allItems relative to its new filtered neighbours
        var moved = _items[toIndex];
        _allItems.Remove(moved);

        if (toIndex == 0)
        {
            var anchor  = _items.Count > 1 ? _items[1] : null;
            int insertAt = anchor is not null ? _allItems.IndexOf(anchor) : 0;
            _allItems.Insert(Math.Max(0, insertAt), moved);
        }
        else
        {
            int insertAfter = _allItems.IndexOf(_items[toIndex - 1]);
            _allItems.Insert(insertAfter + 1, moved);
        }
    }

    private void ReindexUiOrderAll()
    {
        for (int i = 0; i < _items.Count; i++) _items[i].UiOrder = i;
    }

    private static int GetStatusPriority(string? status) => status?.Trim() switch
    {
        "시작 전" => 0,
        "진행 중" => 1,
        "완료"   => 2,
        _        => 99
    };

    private void ClearStatusButtonEffects()
    {
        foreach (var b in TodoItems.GetVisualDescendants().OfType<Button>()
                                   .Where(b => b.Classes.Contains("status")))
        {
            b.Classes.Remove("pressed");
            if (!b.IsPointerOver) b.Classes.Remove("hover");
        }
    }

    // ── UI helpers ────────────────────────────────────────────────────

    private (ItemDto? Item, Avalonia.Visual? Container) FindItemContainer(Avalonia.Visual? v)
    {
        while (v != null)
        {
            if (v is ListBoxItem lbi && lbi.DataContext is ItemDto item)
                return (item, lbi);
            if (v is Control c && c.DataContext is ItemDto it &&
                v.GetVisualParent() is ListBoxItem parent)
                return (it, parent);
            v = v.GetVisualParent() as Avalonia.Visual;
        }
        return (null, null);
    }

    private static bool IsDescendantOf<T>(Control? c) where T : Control
    {
        while (c is not null)
        {
            if (c is T) return true;
            c = c.Parent as Control;
        }
        return false;
    }

    private static void SetClass(Button? btn, string cls, bool on)
    {
        if (btn is null) return;
        if (on) btn.Classes.Add(cls);
        else    btn.Classes.Remove(cls);
    }

    private static WindowEdge? ParseWindowEdge(string? tag) => tag switch
    {
        "North"     => WindowEdge.North,
        "South"     => WindowEdge.South,
        "East"      => WindowEdge.East,
        "West"      => WindowEdge.West,
        "NorthEast" => WindowEdge.NorthEast,
        "NorthWest" => WindowEdge.NorthWest,
        "SouthEast" => WindowEdge.SouthEast,
        "SouthWest" => WindowEdge.SouthWest,
        _           => null
    };
}
