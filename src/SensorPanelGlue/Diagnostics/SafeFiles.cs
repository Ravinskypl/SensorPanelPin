using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static SensorPanelGlue.Interop.FileInterop;

namespace SensorPanelGlue.Diagnostics;

/// <summary>Raised when a write target is not the plain file it is supposed to be.</summary>
public sealed class UnsafePathException : IOException
{
    public UnsafePathException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// File access for a process that normally runs elevated but writes into the user's own profile.
///
/// Without these checks, ordinary unprivileged code could swap %APPDATA%\SensorPanelGlue\logs for a
/// junction (no privilege needed) or pre-create the log as a hardlink, and the elevated process
/// would then append attacker-chosen text - window titles are attacker-controlled - into a file the
/// attacker could never write themselves.
///
/// The defence has two halves. A directory is checked once for being a reparse point and its real
/// path is pinned at that moment. Every file opened afterwards is verified through its handle: it
/// must have exactly one link and must resolve to the pinned directory. Checking the handle rather
/// than the path closes the race of the directory being swapped between check and open.
/// </summary>
public sealed class SafeDirectory
{
    private readonly string _finalPath;

    private SafeDirectory(string path, string finalPath)
    {
        Path = path;
        _finalPath = finalPath;
    }

    public string Path { get; }

    /// <summary>
    /// Creates the directory if needed and pins it. <paramref name="levels"/> controls how many
    /// directories, counted from <paramref name="path"/> upwards, must not be reparse points: only
    /// the ones this application owns. Junctions further up, such as a redirected profile, are the
    /// user's own legitimate configuration and are deliberately left alone.
    /// </summary>
    public static SafeDirectory Open(string path, int levels = 1)
    {
        var full = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(full);

        var current = full;
        for (var i = 0; i < levels && !string.IsNullOrEmpty(current); i++)
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnsafePathException(
                    $"Katalog „{current}” jest dowiązaniem (junction/symlink) — odmawiam zapisu.");
            }

            current = System.IO.Path.GetDirectoryName(current);
        }

        using var handle = CreateFile(
            full,
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new IOException($"Nie można otworzyć katalogu „{full}”.", new Win32Exception(Marshal.GetLastWin32Error()));
        }

        return new SafeDirectory(full, FinalPath(handle));
    }

    /// <summary>Full path of <paramref name="fileName"/> inside this directory.</summary>
    public string Combine(string fileName) => System.IO.Path.Combine(Path, fileName);

    /// <summary>
    /// Opens a file in this directory and proves the handle points at a plain, single-link file
    /// that really lives here. Disposes the stream and throws if it does not.
    /// </summary>
    public FileStream OpenFile(string fileName, FileMode mode, FileAccess access, FileShare share)
    {
        if (fileName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
            || fileName.Contains(System.IO.Path.DirectorySeparatorChar))
        {
            throw new ArgumentException("Oczekiwano samej nazwy pliku.", nameof(fileName));
        }

        var stream = new FileStream(Combine(fileName), mode, access, share);
        try
        {
            Verify(stream.SafeFileHandle, fileName);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private void Verify(SafeFileHandle handle, string fileName)
    {
        if (!GetFileInformationByHandle(handle, out var info))
        {
            throw new IOException("GetFileInformationByHandle nie powiodło się.", new Win32Exception(Marshal.GetLastWin32Error()));
        }

        if (((FileAttributes)info.FileAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnsafePathException($"Plik „{fileName}” jest dowiązaniem — odmawiam zapisu.");
        }

        if (info.NumberOfLinks != 1)
        {
            throw new UnsafePathException(
                $"Plik „{fileName}” ma {info.NumberOfLinks} dowiązania twarde — odmawiam zapisu.");
        }

        var expected = _finalPath.TrimEnd('\\') + "\\" + fileName;
        var actual = FinalPath(handle);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsafePathException(
                $"Plik „{fileName}” prowadzi do „{actual}” zamiast „{expected}” — odmawiam zapisu.");
        }
    }

    private static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(1024);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
        if (length == 0)
        {
            throw new IOException("GetFinalPathNameByHandle nie powiodło się.", new Win32Exception(Marshal.GetLastWin32Error()));
        }

        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder((int)length + 1);
            length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
            if (length == 0 || length >= buffer.Capacity)
            {
                throw new IOException("GetFinalPathNameByHandle nie powiodło się.");
            }
        }

        var path = buffer.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
        {
            return @"\\" + path[8..];
        }

        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }
}
