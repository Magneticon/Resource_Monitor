using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LegacyGpuMonitor
{
    // Minimal dynamic NVML loader for old NVIDIA drivers.  NVML's GPU
    // utilization counter is a kernel-execution counter, but it is not
    // CUDA-only: graphics kernels can also contribute.  To keep the CUDA
    // graph honest, we additionally query the legacy compute-process list.
    // If no CUDA/compute process exists, CUDA utilization is explicitly 0%.
    public sealed class NvmlApi : IDisposable
    {
        private const uint NVML_SUCCESS = 0;
        private const uint NVML_ERROR_INSUFFICIENT_SIZE = 7;
        private const uint NVML_TEMPERATURE_GPU = 0;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct Utilization { public uint gpu; public uint memory; }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct MemoryInfo { public ulong total; public ulong free; public ulong used; }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct ProcessInfoV1 { public uint pid; public ulong usedGpuMemory; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ShutdownDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceGetCountDelegate(out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceGetHandleByIndexDelegate(uint index, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceGetNameDelegate(IntPtr device, IntPtr name, uint length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceGetUtilizationRatesDelegate(IntPtr device, out Utilization utilization);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceGetMemoryInfoDelegate(IntPtr device, out MemoryInfo memory);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceGetTemperatureDelegate(IntPtr device, uint sensorType, out uint temperature);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceGetComputeRunningProcessesDelegate(IntPtr device, ref uint infoCount, IntPtr infos);

        private IntPtr module;
        private InitDelegate init;
        private ShutdownDelegate shutdown;
        private DeviceGetHandleByIndexDelegate getHandle;
        private DeviceGetNameDelegate getName;
        private DeviceGetUtilizationRatesDelegate getUtil;
        private DeviceGetMemoryInfoDelegate getMemory;
        private DeviceGetTemperatureDelegate getTemp;
        private DeviceGetComputeRunningProcessesDelegate getComputeProcesses;
        private IntPtr device;

        public bool IsAvailable { get; private set; }
        public string ErrorText { get; private set; }
        public string GpuName { get; private set; }

        public NvmlApi()
        {
            try
            {
                module = LoadNvml();
                if (module == IntPtr.Zero) { ErrorText = "nvml.dll was not found."; return; }

                init = Get<InitDelegate>("nvmlInit");
                shutdown = Get<ShutdownDelegate>("nvmlShutdown");
                DeviceGetCountDelegate count = Get<DeviceGetCountDelegate>("nvmlDeviceGetCount");
                getHandle = Get<DeviceGetHandleByIndexDelegate>("nvmlDeviceGetHandleByIndex");
                getName = Get<DeviceGetNameDelegate>("nvmlDeviceGetName");
                getUtil = Get<DeviceGetUtilizationRatesDelegate>("nvmlDeviceGetUtilizationRates");
                getMemory = Get<DeviceGetMemoryInfoDelegate>("nvmlDeviceGetMemoryInfo");
                getTemp = Get<DeviceGetTemperatureDelegate>("nvmlDeviceGetTemperature");
                // This function predates the 348 driver and specifically lists
                // processes with a compute context; graphics applications are not
                // included.  See NVIDIA's NVML history/documentation.
                getComputeProcesses = Get<DeviceGetComputeRunningProcessesDelegate>("nvmlDeviceGetComputeRunningProcesses");

                if (init == null || count == null || getHandle == null || getUtil == null)
                {
                    ErrorText = "Required NVML functions are unavailable.";
                    return;
                }

                if (init() != NVML_SUCCESS) { ErrorText = "nvmlInit failed."; return; }

                uint n;
                if (count(out n) != NVML_SUCCESS || n == 0)
                {
                    ErrorText = "NVML reports no NVIDIA devices.";
                    return;
                }

                if (getHandle(0, out device) != NVML_SUCCESS)
                {
                    ErrorText = "Could not obtain the first NVML device handle.";
                    return;
                }

                if (getName != null)
                {
                    IntPtr p = Marshal.AllocHGlobal(256);
                    try
                    {
                        for (int i = 0; i < 256; i++) Marshal.WriteByte(p, i, 0);
                        if (getName(device, p, 256) == NVML_SUCCESS)
                            GpuName = Marshal.PtrToStringAnsi(p);
                    }
                    finally { Marshal.FreeHGlobal(p); }
                }

                if (String.IsNullOrEmpty(GpuName)) GpuName = "NVIDIA GPU";
                IsAvailable = true;
            }
            catch (Exception ex) { ErrorText = ex.Message; }
        }

        private IntPtr LoadNvml()
        {
            string[] names = new string[]
            {
                "nvml.dll",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation\\NVSMI\\nvml.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "NVIDIA Corporation\\NVSMI\\nvml.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nvml.dll")
            };

            for (int i = 0; i < names.Length; i++)
            {
                if (String.IsNullOrEmpty(names[i])) continue;
                IntPtr h = NativeMethods.LoadLibrary(names[i]);
                if (h != IntPtr.Zero) return h;
            }
            return IntPtr.Zero;
        }

        private T Get<T>(string name) where T : class
        {
            IntPtr p = NativeMethods.GetProcAddress(module, name);
            if (p == IntPtr.Zero) return null;
            return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        public Sample Read()
        {
            Sample s = new Sample();
            if (!IsAvailable) return s;

            try
            {
                Utilization u;
                if (getUtil(device, out u) == NVML_SUCCESS)
                {
                    s.NvmlGpuUtilization = u.gpu;
                    s.NvmlMemoryActivity = u.memory;
                }
            }
            catch { }

            try
            {
                if (getMemory != null)
                {
                    MemoryInfo m;
                    if (getMemory(device, out m) == NVML_SUCCESS && m.total > 0)
                    {
                        s.VramUsedMb = m.used / 1048576.0;
                        s.VramTotalMb = m.total / 1048576.0;
                        s.VramUtilization = (float)(m.used * 100.0 / m.total);
                    }
                }
            }
            catch { }

            try
            {
                if (getTemp != null)
                {
                    uint t;
                    if (getTemp(device, NVML_TEMPERATURE_GPU, out t) == NVML_SUCCESS)
                        s.TemperatureC = t;
                }
            }
            catch { }

            try
            {
                if (getComputeProcesses != null)
                {
                    uint count = 0;
                    int rc = getComputeProcesses(device, ref count, IntPtr.Zero);
                    if (rc == NVML_SUCCESS)
                    {
                        s.ComputeProcessCount = 0;
                    }
                    else if (rc == NVML_ERROR_INSUFFICIENT_SIZE && count > 0)
                    {
                        int structSize = Marshal.SizeOf(typeof(ProcessInfoV1));
                        IntPtr p = Marshal.AllocHGlobal(structSize * (int)count);
                        try
                        {
                            uint capacity = count;
                            rc = getComputeProcesses(device, ref capacity, p);
                            if (rc == NVML_SUCCESS)
                                s.ComputeProcessCount = (int)capacity;
                        }
                        finally { Marshal.FreeHGlobal(p); }
                    }
                    s.CudaProcessActive = s.ComputeProcessCount > 0;
                    // Important: nvmlDeviceGetUtilizationRates().gpu is not
                    // CUDA-only. It is only used here when a compute context is
                    // actually present; otherwise CUDA is definitely idle.
                    s.CudaUtilization = s.CudaProcessActive ? s.NvmlGpuUtilization : 0;
                    s.CudaCounterReliable = true;
                }
            }
            catch
            {
                s.CudaCounterReliable = false;
            }

            return s;
        }

        public struct Sample
        {
            public float CudaUtilization;
            public float NvmlGpuUtilization;
            public bool CudaProcessActive;
            public bool CudaCounterReliable;
            public int ComputeProcessCount;
            public float VramUtilization;
            public float TemperatureC;
            public uint NvmlMemoryActivity;
            public double VramUsedMb;
            public double VramTotalMb;
        }

        public void Dispose()
        {
            try { if (shutdown != null && IsAvailable) shutdown(); } catch { }
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
