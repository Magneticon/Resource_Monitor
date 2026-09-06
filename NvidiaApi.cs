using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LegacyGpuMonitor
{
    public sealed class NvidiaApi : IDisposable
    {
        private const uint OK = 0;
        private const uint MAX_GPUS = 64;

        private const uint ID_Initialize = 0x0150E828;
        private const uint ID_EnumPhysicalGPUs = 0xE5AC921F;
        private const uint ID_GetUsages = 0x189A1FDF;
        private const uint ID_GetThermalSettings = 0xE3640A56;
        private const uint ID_GetFullName = 0xCEEE8E9F;
        private const uint ID_GetMemoryInfo = 0x774AA982;
        private const uint ID_GetPhysicalFrameBufferSize = 0x46FBEB03;
        private const uint ID_EnumNvidiaDisplayHandle = 0x9ABDD40D;
        private const uint ID_GetDynamicPstatesInfoEx = 0x60DED2ED;
        private const uint ID_GetAllClockFrequencies = 0xDCB616C3;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr QueryDelegate(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint InitializeDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint EnumPhysicalGPUsDelegate([Out] IntPtr[] handles, out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint EnumNvidiaDisplayHandleDelegate(uint index, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GetUsagesDelegate(IntPtr handle, IntPtr usages);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GetThermalSettingsDelegate(IntPtr handle, uint sensorIndex, IntPtr settings);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GetFullNameDelegate(IntPtr handle, StringBuilder name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GetMemoryInfoDelegate(IntPtr handle, IntPtr memoryInfo);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GetPhysicalFrameBufferSizeDelegate(IntPtr handle, out uint sizeKb);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GetDynamicPstatesInfoExDelegate(IntPtr handle, IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GetAllClockFrequenciesDelegate(IntPtr handle, IntPtr frequencies);

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct ThermalSensor
        {
            public int controller;
            public uint defaultMinTemp;
            public uint defaultMaxTemp;
            public int currentTemp;
            public uint target;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct ThermalSettings
        {
            public uint version;
            public uint count;
            public ThermalSensor sensor0;
            public ThermalSensor sensor1;
            public ThermalSensor sensor2;
        }

        private IntPtr module;
        private QueryDelegate query;
        private InitializeDelegate initialize;
        private EnumPhysicalGPUsDelegate enumPhysicalGPUs;
        private EnumNvidiaDisplayHandleDelegate enumNvidiaDisplayHandle;
        private GetUsagesDelegate getUsages;
        private GetThermalSettingsDelegate getThermalSettings;
        private GetFullNameDelegate getFullName;
        private GetMemoryInfoDelegate getMemoryInfo;
        private GetPhysicalFrameBufferSizeDelegate getPhysicalFrameBufferSize;
        private GetDynamicPstatesInfoExDelegate getDynamicPstatesInfoEx;
        private GetAllClockFrequenciesDelegate getAllClockFrequencies;
        private IntPtr gpu;
        private IntPtr display;

        public bool IsAvailable { get; private set; }
        public string GpuName { get; private set; }
        public string ErrorText { get; private set; }

        public NvidiaApi()
        {
            try
            {
                module = NativeMethods.LoadLibrary("nvapi64.dll");
                if (module == IntPtr.Zero) { ErrorText = "nvapi64.dll was not found."; return; }

                IntPtr q = NativeMethods.GetProcAddress(module, "nvapi_QueryInterface");
                if (q == IntPtr.Zero) { ErrorText = "nvapi_QueryInterface was not found."; return; }

                query = (QueryDelegate)Marshal.GetDelegateForFunctionPointer(q, typeof(QueryDelegate));
                initialize = Get<InitializeDelegate>(ID_Initialize);
                enumPhysicalGPUs = Get<EnumPhysicalGPUsDelegate>(ID_EnumPhysicalGPUs);
                enumNvidiaDisplayHandle = Get<EnumNvidiaDisplayHandleDelegate>(ID_EnumNvidiaDisplayHandle);
                getUsages = Get<GetUsagesDelegate>(ID_GetUsages);
                getThermalSettings = Get<GetThermalSettingsDelegate>(ID_GetThermalSettings);
                getFullName = Get<GetFullNameDelegate>(ID_GetFullName);
                getMemoryInfo = Get<GetMemoryInfoDelegate>(ID_GetMemoryInfo);
                getPhysicalFrameBufferSize = Get<GetPhysicalFrameBufferSizeDelegate>(ID_GetPhysicalFrameBufferSize);
                getDynamicPstatesInfoEx = Get<GetDynamicPstatesInfoExDelegate>(ID_GetDynamicPstatesInfoEx);
                getAllClockFrequencies = Get<GetAllClockFrequenciesDelegate>(ID_GetAllClockFrequencies);

                if (initialize == null || enumPhysicalGPUs == null || getUsages == null ||
                    getThermalSettings == null)
                {
                    ErrorText = "Required NVAPI interfaces are unavailable.";
                    return;
                }

                if (initialize() != OK) { ErrorText = "NvAPI_Initialize failed."; return; }

                IntPtr[] handles = new IntPtr[MAX_GPUS];
                uint count;
                if (enumPhysicalGPUs(handles, out count) != OK || count == 0)
                {
                    ErrorText = "No physical NVIDIA GPU was returned.";
                    return;
                }

                gpu = handles[0];
                display = IntPtr.Zero;
                if (enumNvidiaDisplayHandle != null)
                {
                    IntPtr d;
                    if (enumNvidiaDisplayHandle(0, out d) == OK) display = d;
                }
                GpuName = "NVIDIA GPU";
                if (getFullName != null)
                {
                    StringBuilder n = new StringBuilder(256);
                    if (getFullName(gpu, n) == OK && n.Length > 0) GpuName = n.ToString();
                }
                IsAvailable = true;
            }
            catch (Exception ex) { ErrorText = ex.Message; }
        }

        private T Get<T>(uint id) where T : class
        {
            IntPtr p = query(id);
            if (p == IntPtr.Zero) return null;
            return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        public Sample Read()
        {
            Sample s = new Sample();
            if (!IsAvailable) return s;

            try
            {
                // NV_USAGES_INFO_V1: version is sizeof(struct) | 0x10000,
                // usages[0] starts at offset 4 and the GPU percentage is
                // usages[0].percentage at offset 12.
                int size = 4 + (4 * 33);
                IntPtr p = Marshal.AllocHGlobal(size);
                try
                {
                    for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
                    Marshal.WriteInt32(p, 0, size | 0x10000);
                    if (getUsages(gpu, p) == OK)
                    {
                        int usage = Marshal.ReadInt32(p, 12);
                        s.GpuUtilization = Clamp(usage);
                        // Legacy NVAPI's first utilization domain is the graphics
                        // engine. On these drivers this is the closest distinct
                        // 3D-engine value exposed by NvAPI_GPU_GetUsages.
                        s.ThreeDUtilization = Clamp(usage);
                    }
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { }

            try
            {
                // On older WDDM drivers the legacy GetMemoryInfo interface can
                // behave differently depending on handle type. Try the active
                // NVIDIA display handle first, then the physical GPU handle.
                double usedMb, totalMb;
                bool gotMemory = TryReadMemoryInfo(display, out usedMb, out totalMb);
                if (!gotMemory) gotMemory = TryReadMemoryInfo(gpu, out usedMb, out totalMb);

                if (gotMemory)
                {
                    s.VramUsedMb = usedMb;
                    s.VramTotalMb = totalMb;
                    s.VramUtilization = (float)(usedMb * 100.0 / totalMb);
                }
                else if (getPhysicalFrameBufferSize != null)
                {
                    uint totalKb;
                    if (getPhysicalFrameBufferSize(gpu, out totalKb) == OK && totalKb > 0)
                    {
                        s.VramTotalMb = totalKb / 1024.0;
                        s.VramUsedMb = 0;
                    }
                }
            }
            catch { }

            try
            {
                // NV_GPU_DYNAMIC_PSTATES_INFO_EX has four utilization domains:
                // GPU/graphics, frame buffer, video engine and bus. The FB domain
                // is the driver's actual framebuffer/VRAM activity percentage.
                if (getDynamicPstatesInfoEx != null)
                {
                    const int size = 40;
                    IntPtr p = Marshal.AllocHGlobal(size);
                    try
                    {
                        for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
                        Marshal.WriteInt32(p, 0, size | 0x10000);
                        if (getDynamicPstatesInfoEx(gpu, p) == OK)
                        {
                            int present = Marshal.ReadInt32(p, 16);
                            int fb = Marshal.ReadInt32(p, 20);
                            if ((present & 1) != 0) s.VramUtilization = Clamp(fb);
                        }
                    }
                    finally { Marshal.FreeHGlobal(p); }
                }
            }
            catch { }

            try
            {
                if (getAllClockFrequencies != null)
                {
                    // NV_GPU_CLOCK_FREQUENCIES_V2:
                    // version (DWORD), ClockType (DWORD), then 32 entries of
                    // { bitfield/present, frequency in kHz }.
                    const int domainCount = 32;
                    const int size = 8 + (domainCount * 8);
                    IntPtr p = Marshal.AllocHGlobal(size);
                    try
                    {
                        for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
                        Marshal.WriteInt32(p, 0, size | (2 << 16)); // V2
                        Marshal.WriteInt32(p, 4, 0); // CURRENT_FREQ

                        if (getAllClockFrequencies(gpu, p) == OK)
                        {
                            // Public clock domain 0 = graphics/core,
                            // domain 4 = memory.
                            int graphicsPresent = Marshal.ReadInt32(p, 8);
                            int graphicsKHz = Marshal.ReadInt32(p, 12);
                            int memoryPresent = Marshal.ReadInt32(p, 8 + (4 * 8));
                            int memoryKHz = Marshal.ReadInt32(p, 12 + (4 * 8));

                            if ((graphicsPresent & 1) != 0 && graphicsKHz > 0)
                                s.GpuCoreClockMHz = graphicsKHz / 1000.0;
                            if ((memoryPresent & 1) != 0 && memoryKHz > 0)
                                s.GpuMemoryClockMHz = memoryKHz / 1000.0;
                        }
                    }
                    finally { Marshal.FreeHGlobal(p); }
                }
            }
            catch { }

            try
            {
                int size = Marshal.SizeOf(typeof(ThermalSettings));
                IntPtr p = Marshal.AllocHGlobal(size);
                try
                {
                    for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
                    Marshal.WriteInt32(p, 0, size | 0x10000);
                    if (getThermalSettings(gpu, 0, p) == OK)
                    {
                        ThermalSettings t = (ThermalSettings)Marshal.PtrToStructure(
                            p, typeof(ThermalSettings));
                        ThermalSensor[] a = { t.sensor0, t.sensor1, t.sensor2 };
                        for (int i = 0; i < a.Length; i++)
                        {
                            if (a[i].target == 1 || a[i].target == 15)
                            {
                                s.TemperatureC = a[i].currentTemp;
                                break;
                            }
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { }

            return s;
        }

        private bool TryReadMemoryInfo(IntPtr handle, out double usedMb, out double totalMb)
        {
            usedMb = 0; totalMb = 0;
            if (handle == IntPtr.Zero || getMemoryInfo == null) return false;

            // V2: version + five DWORD fields. Current available is at offset 20.
            int sizeV2 = 24;
            IntPtr p2 = Marshal.AllocHGlobal(sizeV2);
            try
            {
                for (int i = 0; i < sizeV2; i++) Marshal.WriteByte(p2, i, 0);
                Marshal.WriteInt32(p2, 0, sizeV2 | 0x20000);
                if (getMemoryInfo(handle, p2) == OK)
                {
                    uint totalKb = unchecked((uint)Marshal.ReadInt32(p2, 4));
                    uint availableKb = unchecked((uint)Marshal.ReadInt32(p2, 20));
                    if (totalKb > 0 && availableKb <= totalKb)
                    {
                        totalMb = totalKb / 1024.0;
                        usedMb = (totalKb - availableKb) / 1024.0;
                        return true;
                    }
                }
            }
            finally { Marshal.FreeHGlobal(p2); }

            // V1: legacy four-memory-field structure.
            int sizeV1 = 20;
            IntPtr p1 = Marshal.AllocHGlobal(sizeV1);
            try
            {
                for (int i = 0; i < sizeV1; i++) Marshal.WriteByte(p1, i, 0);
                Marshal.WriteInt32(p1, 0, sizeV1 | 0x10000);
                if (getMemoryInfo(handle, p1) == OK)
                {
                    uint totalKb = unchecked((uint)Marshal.ReadInt32(p1, 4));
                    uint availableKb = unchecked((uint)Marshal.ReadInt32(p1, 8));
                    if (totalKb > 0 && availableKb <= totalKb)
                    {
                        totalMb = totalKb / 1024.0;
                        usedMb = (totalKb - availableKb) / 1024.0;
                        return true;
                    }
                }
            }
            finally { Marshal.FreeHGlobal(p1); }
            return false;
        }

        private static float Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 100) return 100;
            return v;
        }

        public struct Sample
        {
            public float GpuUtilization;
            public float ThreeDUtilization;
            public float VramUtilization;
            public float TemperatureC;
            public double VramUsedMb;
            public double VramTotalMb;
            public double GpuCoreClockMHz;
            public double GpuMemoryClockMHz;
        }

        public void Dispose()
        {
            IsAvailable = false;
            if (module != IntPtr.Zero)
            {
                NativeMethods.FreeLibrary(module);
                module = IntPtr.Zero;
            }
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
            public static extern IntPtr LoadLibrary(string lpFileName);
            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool FreeLibrary(IntPtr hModule);
        }
    }
}
