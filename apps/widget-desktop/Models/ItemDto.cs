using System.Collections.Generic;
namespace WidgetDesktop.Models;

public sealed class ItemDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Status { get; set; }
    public string? StatusId { get; set; }
    public string? LastEditedTime { get; set; }
}

public sealed class StatusOptionDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class QueryItemsResponseDto
{
    public List<ItemDto> Items { get; set; } = new();
    public List<StatusOptionDto> StatusOptions { get; set; } = new();
}

public sealed class StatusUpdateResponseDto
{
    public string Id { get; set; } = "";
    public string? Status { get; set; }
    public string? StatusId { get; set; }
    public string? LastEditedTime { get; set; }
}
