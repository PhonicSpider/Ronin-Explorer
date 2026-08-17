using System.Security.Principal;

namespace RoninExplorer.Core.Services;

/// <summary>
/// Whether the process is running elevated. Ronin Explorer's manifest defaults
/// to asInvoker (unlike Disk Manager's unconditional requireAdministrator) —
/// a daily-driver explorer can't UAC-prompt on every launch — so
/// VolumeIndexManager checks this before attempting to build the MFT index,
/// which needs GENERIC_READ on the raw volume handle.
/// </summary>
public static class ElevationService
{
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
