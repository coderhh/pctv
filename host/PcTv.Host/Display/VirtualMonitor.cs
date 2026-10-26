using System.Runtime.InteropServices;

namespace PcTv.Host.Display;

/// <summary>An output attached to the desktop, with the DXGI indices ddagrab needs.</summary>
public sealed record MonitorInfo(string DeviceName, int X, int Y, int Width, int Height, bool Primary,
    int AdapterIndex, int OutputIndex, string AdapterName, string FriendlyName = "", string HardwareId = "")
{
    /// <summary>Known virtual display drivers (Parsec VDD, MikeTheTech VDD, IddSampleDriver family) or a "virtual" product name.</summary>
    public bool LooksVirtual =>
        new[] { "MTT1337", "PSCCDD", "MONITOR\\IDD", "IDDSAMPLE", "VIRTUAL" }
            .Any(k => HardwareId.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
        FriendlyName.Contains("virtual", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"{DeviceName} '{FriendlyName}' {Width}x{Height} at ({X},{Y}){(Primary ? " primary" : "")}{(LooksVirtual ? " virtual" : "")} [adapter {AdapterIndex} '{AdapterName}', output {OutputIndex}, {HardwareId}]";
}

public static class VirtualMonitor
{
    /// <summary>
    /// Chooses the TV monitor. With a configured name: that device (e.g. \\.\DISPLAY3).
    /// Otherwise a non-primary monitor of the wanted size that LooksVirtual, or null. Never guesses a physical screen.
    /// </summary>
    public static MonitorInfo? Pick(IReadOnlyList<MonitorInfo> monitors, string? configuredName, int width, int height)
    {
        if (!string.IsNullOrWhiteSpace(configuredName))
            return monitors.FirstOrDefault(m => string.Equals(m.DeviceName, configuredName, StringComparison.OrdinalIgnoreCase));
        return monitors.FirstOrDefault(m => !m.Primary && m.LooksVirtual && m.Width == width && m.Height == height);
    }

    /// <summary>Enumerates every desktop-attached output on every adapter via DXGI.</summary>
    public static List<MonitorInfo> Enumerate()
    {
        var result = new List<MonitorInfo>();
        var iid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref iid, out var factoryObj) != 0) return result;
        var factory = (IDXGIFactory1)factoryObj;
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter) == 0; a++)
        {
            adapter.GetDesc1(out var adesc);
            for (uint o = 0; adapter.EnumOutputs(o, out var output) == 0; o++)
            {
                output.GetDesc(out var d);
                if (d.AttachedToDesktop == 0) continue;
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                bool primary = GetMonitorInfo(d.Monitor, ref mi) && (mi.dwFlags & 1) != 0;
                var r = d.DesktopCoordinates;
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                bool ok = EnumDisplayDevices(d.DeviceName, 0, ref dd, 0);
                result.Add(new MonitorInfo(d.DeviceName, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, primary,
                    (int)a, (int)o, adesc.Description, ok ? dd.DeviceString : "", ok ? dd.DeviceID : ""));
                Marshal.ReleaseComObject(output);
            }
            Marshal.ReleaseComObject(adapter);
        }
        Marshal.ReleaseComObject(factory);
        return result;
    }

    [DllImport("dxgi.dll")]
    static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE dd, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    // Vtable slots are positional: inherited methods are declared too, unused ones as placeholders.
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIFactory1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        void EnumAdapters(); void MakeWindowAssociation(); void GetWindowAssociation(); void CreateSwapChain(); void CreateSoftwareAdapter();
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
        void IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIAdapter1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        [PreserveSig] int EnumOutputs(uint index, out IDXGIOutput output);
        void GetDesc(); void CheckInterfaceSupport();
        void GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }

    [ComImport, Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIOutput
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        void GetDesc(out DXGI_OUTPUT_DESC desc);
    }
}
