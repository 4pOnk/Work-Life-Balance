$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WlbPackageIdentity {
    [DllImport("ntdll.dll")] static extern int NtQueryKey(IntPtr key, int infoClass, IntPtr buffer, int length, out int needed);
    public static string RegistryName(IntPtr key) {
        int needed;
        NtQueryKey(key, 3, IntPtr.Zero, 0, out needed);
        if (needed <= 4 || needed > 65536) return "unavailable";
        var buffer = Marshal.AllocHGlobal(needed);
        try {
            var result = NtQueryKey(key, 3, buffer, needed, out needed);
            return result == 0 ? Marshal.PtrToStringUni(IntPtr.Add(buffer, 4), Marshal.ReadInt32(buffer) / 2) : "error: " + result;
        } finally { Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder name);
    public static string Read(int pid) {
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) return "unavailable: " + Marshal.GetLastWin32Error();
        try {
            uint length = 0;
            var result = GetPackageFullName(handle, ref length, null);
            if (result == 15700) return "unpackaged";
            if (result != 122) return "error: " + result;
            var name = new StringBuilder((int)length);
            result = GetPackageFullName(handle, ref length, name);
            return result == 0 ? name.ToString() : "error: " + result;
        } finally { CloseHandle(handle); }
    }
}
'@
[pscustomobject]@{ Process = 'Diagnostic shell'; PID = $PID; Package = [WlbPackageIdentity]::Read($PID) }
Get-Process firefox -ErrorAction SilentlyContinue | ForEach-Object {
    [pscustomobject]@{ Process = 'Firefox'; PID = $_.Id; Package = [WlbPackageIdentity]::Read($_.Id) }
}
$key = 'HKCU:\Software\Mozilla\NativeMessagingHosts\com.worklifebalance.tracker'
if (Test-Path -LiteralPath $key) {
    Write-Output ('Native manifest: ' + (Get-Item -LiteralPath $key).GetValue(''))
    $registryKey = Get-Item -LiteralPath $key
    try { Write-Output ('Physical registry key: ' + [WlbPackageIdentity]::RegistryName($registryKey.Handle.DangerousGetHandle())) }
    finally { $registryKey.Dispose() }
} else { Write-Output 'Native manifest: NOT REGISTERED in this process registry view' }
