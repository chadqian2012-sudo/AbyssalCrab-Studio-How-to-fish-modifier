#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace HowToFishTrainer.App;

internal static class FishingNative
{
    private const string ResourceName = "abyssalcrab_journal.dll";
    private const string OptIn = "ABYSSALCRAB_JOURNAL_RUST";
    private const int GuidBytes = 16;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Abi();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ValidateLinks([In] byte[] sessionIds, int sessionCount, [In] byte[] catchLinks, int catchCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OrderRows([In] long[] utcTicks, [In] byte[] sessionKeys, int count,
        [In] byte[]? selectedSession, [Out] int[] indices, int capacity);

    private sealed record Exports(ValidateLinks Validate, OrderRows Order);
    private static readonly Lazy<Exports?> Library = new(() =>
        Environment.GetEnvironmentVariable(OptIn) == "1" ? Load() : null);
    internal static bool Enabled => Library.Value != null;

    internal static bool TryValidateLinks(byte[] sessionIds, int sessionCount, byte[] catchLinks, int catchCount, out int status)
    {
        status = 0;
        var native = Library.Value;
        if (native == null || sessionCount < 0 || sessionCount > sessionIds.Length / GuidBytes
            || catchCount < 0 || catchCount > catchLinks.Length / (2 * GuidBytes))
            return false;

        status = native.Validate(sessionIds, sessionCount, catchLinks, catchCount);
        return status is >= 0 and <= 2;
    }

    internal static bool TryOrderRows(long[] utcTicks, byte[] sessionKeys, int count,
        byte[]? selectedSession, int[] indices, out int written)
    {
        written = 0;
        var native = Library.Value;
        if (native == null || count < 0 || count > utcTicks.Length || count > sessionKeys.Length / GuidBytes
            || count > indices.Length || (selectedSession != null && selectedSession.Length != GuidBytes))
            return false;

        int length = native.Order(utcTicks, sessionKeys, count, selectedSession, indices, indices.Length);
        if (length < 0 || length > count) return false;
        written = length;
        return true;
    }

    private static Exports? Load()
    {
        IntPtr module = IntPtr.Zero;
        try
        {
            var assembly = typeof(FishingNative).Assembly;
            using var resource = assembly.GetManifestResourceStream(ResourceName);
            if (resource == null) return null;
            var bytes = new byte[checked((int)resource.Length)];
            resource.ReadExactly(bytes);
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            string path = Path.Combine(Path.GetTempPath(), "AbyssalCrab Studio", "journal", hash, ResourceName);
            if (!Matches(path, hash))
            {
                try { AtomicLocalFile.WriteBytes(path, bytes); }
                catch (IOException) when (Matches(path, hash)) { }
            }

            using var libraryFile = File.OpenRead(path);
            if (Convert.ToHexString(SHA256.HashData(libraryFile)) != hash) return null;
            module = NativeLibrary.Load(path, assembly, DllImportSearchPath.SafeDirectories);
            var abi = Marshal.GetDelegateForFunctionPointer<Abi>(NativeLibrary.GetExport(module, "journal_abi"));
            if (abi() != 1) return null;
            var exports = new Exports(
                Marshal.GetDelegateForFunctionPointer<ValidateLinks>(NativeLibrary.GetExport(module, "journal_validate_links")),
                Marshal.GetDelegateForFunctionPointer<OrderRows>(NativeLibrary.GetExport(module, "journal_order_rows")));
            // The delegates stay callable for the lifetime of the process.
            module = IntPtr.Zero;
            return exports;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or NotSupportedException)
        {
            return null;
        }
        finally
        {
            if (module != IntPtr.Zero) NativeLibrary.Free(module);
        }
    }

    private static bool Matches(string path, string hash)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)) == hash;
    }
}
