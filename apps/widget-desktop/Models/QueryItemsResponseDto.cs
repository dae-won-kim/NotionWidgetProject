using System.Collections.Generic;

namespace WidgetDesktop.Models;

public class QueryItemsResponseDto
{
    public List<ItemDto> Items { get; set; } = new();
    public List<StatusOptionDto> StatusOptions { get; set; } = new();
}
