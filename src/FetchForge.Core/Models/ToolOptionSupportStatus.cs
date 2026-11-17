namespace FetchForge.Core.Models;

public enum ToolOptionSupportStatus
{
    /// <summary>The installed tool advertises the option.</summary>
    Supported,

    /// <summary>The installed tool does not advertise the option.</summary>
    Unsupported,

    /// <summary>Support could not be checked (tool or help missing).</summary>
    Unverified,

    /// <summary>
    /// Application-level option without a command-line switch.
    /// </summary>
    NotApplicable
}
