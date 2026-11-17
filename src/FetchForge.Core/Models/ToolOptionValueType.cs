namespace FetchForge.Core.Models;

/// <summary>
/// Describes the data type expected by a registered tool option.
/// These types are independent of any UI framework.
/// </summary>
public enum ToolOptionValueType
{
    Boolean,
    Integer,
    Decimal,
    String,
    Enumeration,
    StringList
}
