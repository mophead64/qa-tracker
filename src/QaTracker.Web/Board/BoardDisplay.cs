namespace QaTracker.Web.Board;

/// <summary>Labels, IDs and colours for board items.</summary>
public static class BoardDisplay
{
    public static readonly IReadOnlyList<BoardColumn> Columns = Enum.GetValues<BoardColumn>();

    public static string Prefix(BoardItemType type) => type switch
    {
        BoardItemType.Story => "ST",
        BoardItemType.Bug => "BG",
        _ => "TK",
    };

    /// <summary>"ST-3", "BG-7", "TK-12".</summary>
    public static string Ref(BoardItemType type, int number) => $"{Prefix(type)}-{number}";

    public static string Ref(BoardItem item) => Ref(item.Type, item.Number);

    /// <summary>"ST-3 Saved cards at checkout".</summary>
    public static string Label(BoardItem item) => $"{Ref(item)} {item.Title}";

    public static BoardItemType? TypeFromPrefix(string prefix) => prefix.ToUpperInvariant() switch
    {
        "ST" => BoardItemType.Story,
        "BG" => BoardItemType.Bug,
        "TK" => BoardItemType.Task,
        _ => null,
    };

    public static string TypeLabel(BoardItemType type) => type switch
    {
        BoardItemType.Story => "Story",
        BoardItemType.Bug => "Bug",
        _ => "Task",
    };

    public static string TypeBadgeClass(BoardItemType type) => type switch
    {
        BoardItemType.Story => "badge badge-blue",
        BoardItemType.Bug => "badge badge-red",
        _ => "badge badge-gray",
    };

    public static string ColumnLabel(BoardColumn column) => column switch
    {
        BoardColumn.New => "New",
        BoardColumn.InProgress => "In progress",
        BoardColumn.Testing => "Testing",
        _ => "Closed",
    };

    public static string ColumnChipClass(BoardColumn column) => column switch
    {
        BoardColumn.New => "chip chip-gray",
        BoardColumn.InProgress => "chip chip-blue",
        BoardColumn.Testing => "chip chip-amber",
        _ => "chip chip-brand",
    };

    public static string ColumnBadgeClass(BoardColumn column) => column switch
    {
        BoardColumn.New => "badge badge-gray",
        BoardColumn.InProgress => "badge badge-blue",
        BoardColumn.Testing => "badge badge-amber",
        _ => "badge badge-brand",
    };

    /// <summary>Who a person is on a card or item page: their full name, else user name.</summary>
    public static string NameOf(Data.ApplicationUser? user, string fallback = "Unassigned") =>
        user is null ? fallback
        : user.DeletedUtc is not null ? Data.ApplicationUser.DeletedName
        : !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName!
        : user.UserName ?? fallback;

    /// <summary>A task's state: "Active" or "Complete".</summary>
    public static string TaskState(bool done) => done ? "Complete" : "Active";
}
