using System.Security.AccessControl;
using System.Security.Principal;

namespace SensorPanelGlue.Config;

/// <summary>
/// Decides whether a program may be started with administrator rights at every logon.
///
/// The scheduled task runs with RunLevel=HighestAvailable and no UAC prompt. If anyone other than
/// administrators can change what sits at the registered path, then ordinary unprivileged code can
/// swap the executable - or drop a DLL beside it - and receive full administrator rights at the next
/// logon. That happened for real here: the task pointed at a build folder on D:\, where inherited
/// permissions give every authenticated user Modify.
/// </summary>
public static class ExecutableTrust
{
    // SYSTEM, BUILTIN\Administrators, NT SERVICE\TrustedInstaller.
    private static readonly HashSet<string> TrustedSids = new(StringComparer.OrdinalIgnoreCase)
    {
        "S-1-5-18",
        "S-1-5-32-544",
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",
    };

    private const FileSystemRights GenericAll = (FileSystemRights)0x10000000;
    private const FileSystemRights GenericWrite = (FileSystemRights)0x40000000;

    /// <summary>Rights on the executable itself that let someone change the code that runs.</summary>
    private const FileSystemRights FileTampering =
        FileSystemRights.WriteData
        | FileSystemRights.AppendData
        | FileSystemRights.Delete
        | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership
        | GenericWrite
        | GenericAll;

    /// <summary>
    /// Rights on the program's own directory: creating a file there is enough, because a DLL placed
    /// next to the executable is loaded before the system copy.
    /// </summary>
    private const FileSystemRights DirectoryTampering =
        FileSystemRights.CreateFiles
        | FileSystemRights.CreateDirectories
        | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership
        | GenericWrite
        | GenericAll;

    /// <summary>
    /// Rights on directories higher up: creating unrelated new folders is harmless (the root of C:
    /// allows that to everyone), but being able to rename or re-permission a directory on the path
    /// lets someone substitute the whole tree.
    /// </summary>
    private const FileSystemRights AncestorTampering =
        FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership
        | GenericAll;

    /// <summary>
    /// True when only administrators can change the program at <paramref name="executablePath"/>,
    /// the files beside it, and the directories leading to it. Otherwise <paramref name="reason"/>
    /// names the first problem found.
    /// </summary>
    public static bool IsProtected(string executablePath, out string reason)
    {
        try
        {
            var exe = new FileInfo(Path.GetFullPath(executablePath));
            if (!exe.Exists)
            {
                reason = $"Plik „{exe.FullName}” nie istnieje.";
                return false;
            }

            var directory = exe.Directory!;

            foreach (var file in directory.EnumerateFiles())
            {
                if (!Check(file.FullName, file.GetAccessControl(), FileTampering, out reason))
                {
                    return false;
                }
            }

            if (!Check(directory.FullName, directory.GetAccessControl(), DirectoryTampering, out reason))
            {
                return false;
            }

            for (var ancestor = directory.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (!Check(ancestor.FullName, ancestor.GetAccessControl(), AncestorTampering, out reason))
                {
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            // Unreadable permissions are not evidence of safety.
            reason = "Nie udało się odczytać uprawnień: " + ex.Message;
            return false;
        }
    }

    private static bool Check(string path, FileSystemSecurity security, FileSystemRights dangerous, out string reason)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;

        // An owner can always rewrite the permissions, whatever the ACL says.
        if (owner is not null && !TrustedSids.Contains(owner.Value))
        {
            reason = $"„{path}” należy do {Describe(owner)}, a nie do administratorów.";
            return false;
        }

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            // Inherit-only entries describe future children, not this object.
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0)
            {
                continue;
            }

            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (TrustedSids.Contains(sid.Value))
            {
                continue;
            }

            if ((rule.FileSystemRights & dangerous) != 0)
            {
                reason = $"„{path}” może modyfikować {Describe(sid)} ({rule.FileSystemRights}).";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static string Describe(SecurityIdentifier sid)
    {
        try
        {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid.Value;
        }
        catch (SystemException)
        {
            return sid.Value;
        }
    }
}
