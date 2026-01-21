using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WidgetDesktop.Models;
using WidgetDesktop.Services;

namespace WidgetDesktop;

public partial class MainWindow : Window
{
    private const string WidgetId = "w_1";
    private readonly WidgetApiClient _api = new("http://localhost:5055");

    private ObservableCollection<ItemDto> _items = new();

    // ✅ Notion status option 순서(정렬 우선순위)
    private Dictionary<string, int> _statusRank = new();

    // ✅ StatusId -> Notion color
    private Dictionary<string, string> _statusIdToColor = new();

    // Drag state
    private ItemDto? _draggingItem;

    public MainWindow()
    {
        InitializeComponent();

        // ✅ XAML 렌더링/바인딩 자체가 되는지 확인용 기본 항목(로드 후 덮어씀)
        var initial = new ObservableCollection<ItemDto>
        {
            new() { Id = "local_loading", Title = "(loading...)", Status = "", StatusId = "opt_a", IsChecked = false }
        };
        TodoItems.ItemsSource = initial;

        // ✅ DEBUG: 생성자에서 컨트롤 참조/바인딩이 실제로 동작하는지 화면에 표시
        ErrorText.IsVisible = true;
        ErrorText.Text = $"DEBUG: ctor bound items = {initial.Count}";

        Opened += async (_, __) =>
        {
            try
            {
                ErrorText.IsVisible = false;
                ErrorText.Text = "";

                var data = await _api.QueryItemsAsync(WidgetId);

                // 1) status 순서(옵션 배열 순서 그대로)
                _statusRank = data.StatusOptions
                    .Select((opt, idx) => (opt.Id, idx))
                    .ToDictionary(x => x.Id, x => x.idx);

                // 2) statusId -> color
                _statusIdToColor = data.StatusOptions
                    .Where(o => !string.IsNullOrWhiteSpace(o.Id))
                    .ToDictionary(o => o.Id, o => (o.Color ?? "gray"));

                // 3) 아이템 로드 + StatusColor 채우기 + UI 순서 초기화
                int order = 0;
                _items.Clear();
                foreach (var it in data.Items)
                {
                    it.UiOrder = order++;
                    it.StatusColor = ResolveColor(it.StatusId);
                    _items.Add(it);
                }

                // 정렬 없이 바로 바인딩(ObservableCollection)
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TodoItems.ItemsSource = null;
                    TodoItems.ItemsSource = _items;
                });

                ErrorText.IsVisible = true;
                ErrorText.Text =
                    $"DEBUG: loaded items = {_items.Count}\n" +
                    $"DEBUG: TodoItems type = {TodoItems.GetType().FullName}\n" +
                    $"DEBUG: ItemsSource type = {TodoItems.ItemsSource?.GetType().FullName ?? "(null)"}";
            }
            catch (Exception ex)
            {
                // ✅ 실패해도 화면에 원인을 표시
                ErrorText.IsVisible = true;
                ErrorText.Text = $"Failed to load items.\n{ex.GetType().Name}: {ex.Message}";

                // 로딩 표시 유지
                _items.Clear();
                TodoItems.ItemsSource = new List<ItemDto>
                {
                    new() { Id = "local_error", Title = "(no items)", Status = "", StatusId = "opt_a", IsChecked = false }
                };
            }
        };
    }

    /* -----------------------
       Window Drag (버튼 클릭이면 드래그 금지)
       ----------------------- */
    private void WindowDragArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Control c)
        {
            Control? cur = c;
            while (cur is not null)
            {
                if (cur is Button) return;
                cur = cur.Parent as Control;
            }
        }

        BeginMoveDrag(e);
    }

    /* -----------------------
       Status LEFT CLICK: next
       ----------------------- */
    private async void StatusButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.DataContext is not ItemDto item) return;

        var updated = await _api.StatusNextAsync(WidgetId, item.Id);

        item.Status = updated.Status;
        item.StatusId = updated.StatusId;
        item.LastEditedTime = updated.LastEditedTime;

        // ✅ 색상도 즉시 갱신
        item.StatusColor = ResolveColor(item.StatusId);

        // (선택) status 바뀌면 해당 그룹에서 최상단으로 올리기
        item.UiOrder = 0;
        ReindexUiOrder(item.StatusId);

        SortAndBind();
        e.Handled = true;
    }

    /* -----------------------
       Status RIGHT CLICK: menu
       (여기는 기존 메뉴 로직 붙이면 됨)
       ----------------------- */
    private void StatusButton_RightPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        // 메뉴 로직(이전 버전) 넣어도 되고,
        // 지금은 크래시 해결 + 색상 적용이 핵심이니 생략 가능
        e.Handled = true;
    }

    /* -----------------------
       Drag reorder (같은 status 그룹 안에서만)
       ----------------------- */
    private void Row_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (sender is Border bd && bd.DataContext is ItemDto item)
            _draggingItem = item;
    }

    private void Row_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingItem == null) return;
        if (sender is not Border bd || bd.DataContext is not ItemDto target) return;
        if (target == _draggingItem) return;

        // status는 바꾸지 않고, 같은 status 안에서만 순서 변경
        if (target.StatusId != _draggingItem.StatusId) return;

        (target.UiOrder, _draggingItem.UiOrder) = (_draggingItem.UiOrder, target.UiOrder);
        SortAndBind();
    }

    private void Row_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggingItem = null;
    }

    /* -----------------------
       Sorting / Binding
       ----------------------- */
    private void SortAndBind()
    {
        var sorted = _items
            .OrderBy(i => GetRank(i.StatusId))
            .ThenBy(i => i.UiOrder)
            .ThenByDescending(i => ParseEdited(i.LastEditedTime)) // 같은 UiOrder라도 최신 우선(안전)
            .ToList();

        TodoItems.ItemsSource = sorted;
    }

    private int GetRank(string? statusId)
        => statusId != null && _statusRank.TryGetValue(statusId, out var r) ? r : int.MaxValue;

    private string ResolveColor(string? statusId)
        => statusId != null && _statusIdToColor.TryGetValue(statusId, out var c) ? c : "gray";

    private void ReindexUiOrder(string? statusId)
    {
        if (statusId == null) return;

        var group = _items
            .Where(i => i.StatusId == statusId)
            .OrderBy(i => i.UiOrder)
            .ToList();

        for (int i = 0; i < group.Count; i++)
            group[i].UiOrder = i;
    }

    private static DateTimeOffset ParseEdited(string? iso)
        => DateTimeOffset.TryParse(iso, out var dto) ? dto : DateTimeOffset.MinValue;
}
