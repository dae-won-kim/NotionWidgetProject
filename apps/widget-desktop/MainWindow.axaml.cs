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
            Control? cur = c;
            while (cur is not null)
            {
                if (cur is Button) return;
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
        ApplyDefaultStatusSortIfNeeded();
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
                ApplyDefaultStatusSortIfNeeded();
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
        if (sender is Border bd && bd.DataContext is ItemDto item)
        {
            _draggingItem = item;
            e.Handled = true;
        }
    }

    private void Row_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingItem == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _draggingItem = null;
            return;
        }
        if (sender is not Border bd || bd.DataContext is not ItemDto target) return;
        if (target == _draggingItem) return;

        var fromIndex = _items.IndexOf(_draggingItem);
        var toIndex = _items.IndexOf(target);
        if (fromIndex < 0 || toIndex < 0) return;

        _items.Move(fromIndex, toIndex);
        _hasManualOrder = true;
        ReindexUiOrderAll();
    }

    private void Row_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggingItem = null;
    }

    private void List_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggingItem = null;
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
