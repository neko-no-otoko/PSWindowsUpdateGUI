using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PSWindowsUpdateGui.Services;

internal static class AuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static void VerifyOrThrow(string path)
    {
        var filePath = Marshal.StringToCoTaskMemUni(path);
        var fileInfoPointer = IntPtr.Zero;
        var trustDataPointer = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
                FilePath = filePath
            };
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustFileInfo)));
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

            var trustData = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfo = fileInfoPointer,
                StateAction = 0,
                ProviderFlags = 0x00001110,
                UiContext = 0
            };
            trustDataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustData)));
            Marshal.StructureToPtr(trustData, trustDataPointer, false);

            var result = unchecked((int)WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, trustDataPointer));
            if (result != 0) throw new Win32Exception(result, $"Authenticode verification failed for {path} (0x{result:X8}).");
        }
        finally
        {
            if (trustDataPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(trustDataPointer);
            if (fileInfoPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfoPointer);
            Marshal.FreeCoTaskMem(filePath);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern uint WinVerifyTrust(
        IntPtr window,
        [MarshalAs(UnmanagedType.LPStruct)] Guid action,
        IntPtr trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
