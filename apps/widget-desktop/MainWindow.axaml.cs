using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WidgetDesktop.Models;
using WidgetDesktop.Services;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace WidgetDesktop;

public partial class MainWindow : Window
{
    private const string WidgetId = "w_1";
    private static readonly string ApiBaseUrl =
        Environment.GetEnvironmentVariable("WIDGET_API_BASE_URL") ?? "http://localhost:5183";
    private readonly WidgetApiClient _api = new(ApiBaseUrl);

    private ObservableCollection<ItemDto> _items = new();

    private Dictionary<string, string> _statusIdToColor = new();
    private List<StatusOptionDto> _statusOptions = new();

    // Drag state
    private ItemDto? _draggingItem;
    private bool _hasManualOrder;
    private bool _isMoving; // 중복 이동 방지 플래그

    public MainWindow()
    {
        InitializeComponent();

        _items.Add(new ItemDto
        {
            Id = "local_loading",
            Title = "(loading...)",
            Status = "",
            StatusId = "opt_a",
            IsChecked = false
        });
        TodoItems.ItemsSource = _items;

        ErrorText.IsVisible = false;
        ErrorText.Text = "";

        Opened += async (_, __) =>
        {
            try
            {
                ErrorText.IsVisible = false;
                ErrorText.Text = "";

                var data = await _api.QueryItemsAsync(WidgetId);
                _statusOptions = data.StatusOptions.ToList();

                _statusIdToColor = data.StatusOptions
                    .Where(o => !string.IsNullOrWhiteSpace(o.Id))
                    .ToDictionary(o => o.Id, o => (o.Color ?? "gray"));

                _items.Clear();
                foreach (var it in data.Items)
                {
                    it.StatusColor = ResolveColor(it.StatusId);
                    _items.Add(it);
                }

                ApplyDefaultStatusSortIfNeeded();

                ErrorText.IsVisible = false;
                ErrorText.Text = "";
            }
            catch (Exception ex)
            {
                ErrorText.IsVisible = true;
                ErrorText.Text = $"Failed to load items.\n{ex.GetType().Name}: {ex.Message}";

                _items.Clear();
                _items.Add(new ItemDto
                {
                    Id = "local_error",
                    Title = "(no items)",
                    Status = "",
                    StatusId = "opt_a",
                    IsChecked = false
                });
            }
        };
    }

    private void WindowDragArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Control c)
        {
            // 드래그 핸들이나 버튼, 혹은 그 내부 요소들을 클릭한 경우 윈도우 드래그 방지
            Control? cur = c;
            while (cur is not null)
            {
                if (cur is Button || (cur is Border b && b.Classes.Contains("drag-handle"))) 
                {
                    return; 
                }
                cur = cur.Parent as Control;
            }
        }

        BeginMoveDrag(e);
    }

    private async void StatusButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.DataContext is not ItemDto item) return;

        var updated = await _api.StatusNextAsync(WidgetId, item.Id);

        item.Status = updated.Status;
        item.StatusId = updated.StatusId;
        item.LastEditedTime = updated.LastEditedTime;
        item.StatusColor = ResolveColor(item.StatusId);
        ApplyDefaultStatusSort();
        ClearStatusButtonEffects();
        e.Handled = true;
    }

    private void StatusButton_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Button b || b.DataContext is not ItemDto item) return;
        b.Classes.Add("pressed");
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        if (_statusOptions.Count == 0) return;

        var menuItems = _statusOptions.Select(opt =>
        {
            var mi = new MenuItem { Header = opt.Name };
            mi.Click += async (_, __) =>
            {
                var updated = await _api.StatusSetAsync(WidgetId, item.Id, opt.Id);
                item.Status = updated.Status;
                item.StatusId = updated.StatusId;
                item.LastEditedTime = updated.LastEditedTime;
                item.StatusColor = ResolveColor(item.StatusId);
                ApplyDefaultStatusSort();
                ClearStatusButtonEffects();
            };
            return mi;
        }).ToList();

        var menu = new ContextMenu
        {
            ItemsSource = menuItems
        };

        menu.Open(b);
        e.Handled = true;
    }

    private void StatusButton_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Button b)
            b.Classes.Remove("pressed");
    }

    private void StatusButton_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Button b)
            b.Classes.Add("hover");
    }

    private void StatusButton_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Button b)
        {
            b.Classes.Remove("hover");
            b.Classes.Remove("pressed");
        }
    }

    private void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        
        if (sender is Control c && c.DataContext is ItemDto item)
        {
            _draggingItem = item;
            var result = FindItemContainer(c);
            if (result.Container is ListBoxItem lbi)
            {
                lbi.Classes.Add("dragging");
            }
            // 캡처를 제거하여 Window_PointerMoved가 이벤트를 받을 수 있게 함
            e.Handled = true;
        }
    }

    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingItem == null || _isMoving) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _draggingItem = null;
            return;
        }

        var point = e.GetPosition(TodoItems);
        var hitControl = TodoItems.InputHitTest(point) as Visual;
        if (hitControl is null) return;

        var targetResult = FindItemContainer(hitControl);
        if (targetResult.Item == null || targetResult.Item == _draggingItem || targetResult.Container == null) return;

        var fromIndex = _items.IndexOf(_draggingItem);
        var toIndex = _items.IndexOf(targetResult.Item);
        if (fromIndex < 0 || toIndex < 0) return;

        var relativePoint = e.GetPosition(targetResult.Container);
        double progress = relativePoint.Y / targetResult.Container.Bounds.Height;

        // 임계값을 50%로 설정하여 더 즉각적인 반응을 유도
        bool shouldMove = (fromIndex < toIndex) ? (progress > 0.5) : (progress < 0.5);

        if (shouldMove)
        {
            _isMoving = true;
            ApplyFlipAnimation(fromIndex, toIndex);
        }
    }

    private void ApplyFlipAnimation(int fromIndex, int toIndex)
    {
        // 1. First: 모든 가시적 아이템의 현재 Y 위치 캡처 (Bounds.Y가 훨씬 빠름)
        var positionMap = new Dictionary<object, double>();
        foreach (var item in _items)
        {
            var container = TodoItems.ContainerFromItem(item);
            if (container != null)
                positionMap[item] = container.Bounds.Y;
        }

        // 2. Move: 데이터 이동
        _items.Move(fromIndex, toIndex);
        _hasManualOrder = true;
        ReindexUiOrderAll();

        // 3. Last, Invert & Play
        Dispatcher.UIThread.Post(() =>
        {
            var animatableItems = new List<ListBoxItem>();
            var scaleTransform = TransformOperations.Parse("scale(1.05)");
            var noneTransform = TransformOperations.Parse("none");

            foreach (var item in _items)
            {
                var container = TodoItems.ContainerFromItem(item) as ListBoxItem;
                if (container != null && positionMap.TryGetValue(item, out double oldY))
                {
                    double deltaY = oldY - container.Bounds.Y;
                    
                    if (Math.Abs(deltaY) > 0.5)
                    {
                        container.Classes.Remove("animating");
                        
                        if (item == _draggingItem)
                            container.RenderTransform = TransformOperations.Parse($"translate(0px, {deltaY}px) scale(1.05)");
                        else
                            container.RenderTransform = TransformOperations.Parse($"translate(0px, {deltaY}px)");

                        animatableItems.Add(container);
                    }
                }
            }

            if (animatableItems.Any())
            {
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var lbi in animatableItems)
                    {
                        lbi.Classes.Add("animating");
                        lbi.RenderTransform = (lbi.DataContext == _draggingItem) ? scaleTransform : noneTransform;
                    }
                    
                    // 새 이동을 즉시 다음 프레임(16ms)에 허용하여 마우스 속도를 따라잡음
                    Dispatcher.UIThread.Post(async () => {
                         await System.Threading.Tasks.Task.Delay(16); 
                         _isMoving = false; 
                         
                         await System.Threading.Tasks.Task.Delay(350);
                         foreach (var lbi in animatableItems)
                             lbi.Classes.Remove("animating");
                    }, DispatcherPriority.Background);

                }, DispatcherPriority.Render);
            }
            else
            {
                _isMoving = false;
            }

            if (_draggingItem != null)
            {
                var draggingLbi = TodoItems.ContainerFromItem(_draggingItem) as ListBoxItem;
                draggingLbi?.Classes.Add("dragging");
            }

        }, DispatcherPriority.Input);
    }

    private (ItemDto? Item, Visual? Container) FindItemContainer(Visual? visual)
    {
        while (visual != null)
        {
            if (visual is ListBoxItem lbi && lbi.DataContext is ItemDto item)
            {
                return (item, lbi);
            }
            // ListBoxItem이 아닐 수도 있으므로 (DataTemplate 내부) 계속 탐색
            if (visual is Control c && c.DataContext is ItemDto it && visual.GetVisualParent() is ListBoxItem parentLbi)
            {
                 return (it, parentLbi);
            }
            visual = visual.GetVisualParent() as Visual;
        }
        return (null, null);
    }

    private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ClearDraggingClasses();
        _draggingItem = null;
    }

    private void List_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ClearDraggingClasses();
        _draggingItem = null;
    }

    private void ClearDraggingClasses()
    {
        foreach (var item in TodoItems.GetVisualDescendants().OfType<ListBoxItem>())
        {
            item.Classes.Remove("dragging");
        }
    }

    private string ResolveColor(string? statusId)
        => statusId != null && _statusIdToColor.TryGetValue(statusId, out var c) ? c : "gray";

    private void ReindexUiOrderAll()
    {
        for (int i = 0; i < _items.Count; i++)
            _items[i].UiOrder = i;
    }

    private void ApplyDefaultStatusSortIfNeeded()
    {
        if (_hasManualOrder) return;
        ApplyDefaultStatusSort();
    }

    private void ApplyDefaultStatusSort()
    {
        var ordered = _items
            .Select((item, index) => new
            {
                item,
                index,
                priority = GetStatusPriority(item.Status)
            })
            .OrderBy(x => x.priority)
            .ThenBy(x => x.index)
            .Select(x => x.item)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            var currentIndex = _items.IndexOf(item);
            if (currentIndex != i)
                _items.Move(currentIndex, i);
        }

        ReindexUiOrderAll();
    }

    private static int GetStatusPriority(string? status)
    {
        var key = NormalizeStatusKey(status);
        return key switch
        {
            "todo" => 0,
            "inprogress" => 1,
            "done" => 2,
            _ => 99
        };
    }

    private static string NormalizeStatusKey(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "";
        var chars = status.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
        return new string(chars);
    }

    private void ClearStatusButtonEffects()
    {
        foreach (var button in TodoItems.GetVisualDescendants().OfType<Button>())
        {
            if (!button.Classes.Contains("status")) continue;
            button.Classes.Remove("pressed");
            if (!button.IsPointerOver)
                button.Classes.Remove("hover");
        }
    }

}
