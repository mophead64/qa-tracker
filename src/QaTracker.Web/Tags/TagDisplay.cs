namespace QaTracker.Web.Tags;

/// <summary>How tags look: their badge class and colour names, in picker order.</summary>
public static class TagDisplay
{
    public static readonly IReadOnlyList<TagColour> Colours = Enum.GetValues<TagColour>();

    public static string BadgeClass(TagColour colour) => colour switch
    {
        TagColour.Blue => "badge badge-blue",
        TagColour.Green => "badge badge-brand",
        TagColour.Amber => "badge badge-amber",
        TagColour.Orange => "badge badge-orange",
        TagColour.Red => "badge badge-red",
        TagColour.Purple => "badge badge-purple",
        _ => "badge badge-gray",
    };

    /// <summary>A small colour swatch for the colour picker.</summary>
    public static string SwatchClass(TagColour colour) => colour switch
    {
        TagColour.Blue => "bg-blue-500",
        TagColour.Green => "bg-brand-500",
        TagColour.Amber => "bg-amber-400",
        TagColour.Orange => "bg-orange-500",
        TagColour.Red => "bg-red-500",
        TagColour.Purple => "bg-purple-500",
        _ => "bg-gray-400",
    };

    public static string Label(TagColour colour) => colour.ToString();
}
