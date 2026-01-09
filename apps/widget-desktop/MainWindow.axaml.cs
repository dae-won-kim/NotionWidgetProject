using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using WidgetDesktop.Models;
using WidgetDesktop.Services;

namespace WidgetDesktop;

public partial class MainWindow : Window
{
    private readonly WidgetApiClient _api = new("http://localhost:5055");
    private List<StatusOptionDto> _statusOptions = new();
    private const string WidgetId = "w_1";

    public MainWindow()
    {
        InitializeComponent();

        this.Opened += async (_, __) =>
        {
            var data = await _api.QueryItemsAsync(WidgetId);
            _statusOptions = data.StatusOptions;
            TodoList.ItemsSource = data.Items;
        };

        TodoList.PointerPressed += TodoList_PointerPressed;
    }

    private async void TodoList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TodoList.SelectedItem is not ItemDto item) return;

        var pt = e.GetCurrentPoint(TodoList);

        if (pt.Properties.IsRightButtonPressed)
        {
            await ShowStatusMenuAsync(item);
            e.Handled = true;
            return;
        }

        if (pt.Properties.IsLeftButtonPressed)
        {
            var updated = await _api.StatusNextAsync(WidgetId, item.Id);
            item.Status = updated.Status;
            item.StatusId = updated.StatusId;
            item.LastEditedTime = updated.LastEditedTime;

            // 임시 갱신(간단)
            var current = TodoList.ItemsSource;
            TodoList.ItemsSource = null;
            TodoList.ItemsSource = current;

            e.Handled = true;
        }
    }

    private async Task ShowStatusMenuAsync(ItemDto item)
    {
        var menu = new ContextMenu();
        var menuItems = new List<MenuItem>();

        foreach (var opt in _statusOptions)
        {
            var mi = new MenuItem { Header = opt.Name };
            mi.Click += async (_, __) =>
            {
                var updated = await _api.StatusSetAsync(WidgetId, item.Id, opt.Id);
                item.Status = updated.Status;
                item.StatusId = updated.StatusId;
                item.LastEditedTime = updated.LastEditedTime;

                var current = TodoList.ItemsSource;
                TodoList.ItemsSource = null;
                TodoList.ItemsSource = current;
            };
            menuItems.Add(mi);
        }

        menu.ItemsSource = menuItems;
        menu.Open(TodoList);
        await Task.CompletedTask;
    }
}
