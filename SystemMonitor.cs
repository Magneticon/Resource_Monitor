using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LegacyGpuMonitor
{
    public sealed class SystemMonitor : IDisposable
    {
        //detect Win version

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;

            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
        private static extern int RtlGetVersion(ref OSVERSIONINFOEX versionInfo);

        private static Version GetRealWindowsVersion()
        {
            OSVERSIONINFOEX versionInfo = new OSVERSIONINFOEX();

            versionInfo.dwOSVersionInfoSize =
                Marshal.SizeOf(typeof(OSVERSIONINFOEX));

            int status = RtlGetVersion(ref versionInfo);

            if (status == 0)
            {
                return new Version(
                    versionInfo.dwMajorVersion,
                    versionInfo.dwMinorVersion,
                    versionInfo.dwBuildNumber);
            }

            // Fallback if RtlGetVersion unexpectedly fails.
            return Environment.OSVersion.Version;
        }

        private static bool IsWindows10OrLater()
        {
            Version v = GetRealWindowsVersion();

            return v.Major >= 10;
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(
            ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr SetThreadAffinityMask(
            IntPtr hThread,
            UIntPtr dwThreadAffinityMask);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(
            IntPtr hObject);

        private readonly PerformanceCounter totalCpu;
        private readonly PerformanceCounter[] coreCpu;
        private readonly int coreCount;
        private readonly DateTime bootTime;
        private readonly CpuTemperatureProvider cpuTemperature;

        public SystemMonitor()
        {
            coreCount = Environment.ProcessorCount;

            totalCpu = new PerformanceCounter(
                "Processor",
                "% Processor Time",
                "_Total",
                true);

            totalCpu.NextValue();

            coreCpu = new PerformanceCounter[coreCount];

            for (int i = 0; i < coreCount; i++)
            {
                coreCpu[i] = new PerformanceCounter(
                    "Processor",
                    "% Processor Time",
                    i.ToString(),
                    true);

                coreCpu[i].NextValue();
            }

            bootTime = ReadBootTime();

            cpuTemperature =
                new CpuTemperatureProvider(coreCount);
        }

        public int CoreCount
        {
            get { return coreCount; }
        }

        public ulong TotalRamBytes
        {
            get;
            private set;
        }

        public ulong AvailableRamBytes
        {
            get;
            private set;
        }

        public ulong UsedRamBytes
        {
            get
            {
                return TotalRamBytes >= AvailableRamBytes
                    ? TotalRamBytes - AvailableRamBytes
                    : 0;
            }
        }

        public double CurrentCpuClockMHz
        {
            get;
            private set;
        }

        public double MaxCpuClockMHz
        {
            get;
            private set;
        }

        public TimeSpan SystemUptime
        {
            get
            {
                TimeSpan t = DateTime.Now - bootTime;

                return t.TotalSeconds >= 0
                    ? t
                    : TimeSpan.Zero;
            }
        }

        private static DateTime ReadBootTime()
        {
            try
            {
                using (ManagementObjectSearcher q =
                    new ManagementObjectSearcher(
                        @"root\CIMV2",
                        "SELECT LastBootUpTime FROM Win32_OperatingSystem"))
                using (ManagementObjectCollection c = q.Get())
                {
                    foreach (ManagementObject o in c)
                    {
                        object v = o["LastBootUpTime"];

                        if (v != null)
                        {
                            return ManagementDateTimeConverter
                                .ToDateTime(v.ToString());
                        }
                    }
                }
            }
            catch
            {
            }

            uint milliseconds =
                unchecked((uint)Environment.TickCount);

            return DateTime.Now -
                TimeSpan.FromMilliseconds(milliseconds);
        }

        private void RefreshMemory()
        {
            MEMORYSTATUSEX m =
                new MEMORYSTATUSEX();

            m.dwLength =
                (uint)Marshal.SizeOf(
                    typeof(MEMORYSTATUSEX));

            if (GlobalMemoryStatusEx(ref m))
            {
                TotalRamBytes =
                    m.ullTotalPhys;

                AvailableRamBytes =
                    m.ullAvailPhys;
            }
        }

        private void RefreshCpuClock()
        {
            try
            {
                using (ManagementObjectSearcher q =
                    new ManagementObjectSearcher(
                        @"root\CIMV2",
                        "SELECT CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor"))
                using (ManagementObjectCollection c =
                    q.Get())
                {
                    double currentSum = 0;
                    double maxSum = 0;
                    int n = 0;

                    foreach (ManagementObject o in c)
                    {
                        currentSum += Convert.ToDouble(
                            o["CurrentClockSpeed"]);

                        maxSum += Convert.ToDouble(
                            o["MaxClockSpeed"]);

                        n++;
                    }

                    if (n > 0)
                    {
                        CurrentCpuClockMHz =
                            currentSum / n;

                        MaxCpuClockMHz =
                            maxSum / n;
                    }
                }
            }
            catch
            {
            }
        }

        public float CpuPercent
        {
            get
            {
                return Clamp(
                    totalCpu.NextValue());
            }
        }

        public float GetCorePercent(
            int index)
        {
            if (index < 0 ||
                index >= coreCpu.Length)
            {
                return 0;
            }

            try
            {
                return Clamp(
                    coreCpu[index].NextValue());
            }
            catch
            {
                return 0;
            }
        }

        public float RamPercent
        {
            get
            {
                RefreshMemory();

                return TotalRamBytes == 0
                    ? 0
                    : (float)(
                        UsedRamBytes * 100.0 /
                        TotalRamBytes);
            }
        }

        public void RefreshHardwareInfo()
        {
            RefreshMemory();
            RefreshCpuClock();
            cpuTemperature.Refresh();
        }

        public float CpuTemperatureC
        {
            get
            {
                return cpuTemperature.CurrentC;
            }
        }

        public string CpuTemperatureSource
        {
            get
            {
                return cpuTemperature.Source;
            }
        }

        private static float Clamp(
            float v)
        {
            if (float.IsNaN(v) ||
                float.IsInfinity(v))
            {
                return 0;
            }

            if (v < 0)
                return 0;

            if (v > 100)
                return 100;

            return v;
        }

        public void Dispose()
        {
            totalCpu.Dispose();

            for (int i = 0;
                 i < coreCpu.Length;
                 i++)
            {
                coreCpu[i].Dispose();
            }

            cpuTemperature.Dispose();
        }

        // ================================================================
        // CPU temperature provider
        //
        // Windows XP / XP x64 to Win 8:
        //     WinRing0 hardware access.
        //
        // Win 10 and newer:
        //     Windows ACPI thermal-zone WMI interface.
        //
        // WinRing0 is never loaded on Win 10 or newer systems.
        // ================================================================

        private sealed class CpuTemperatureProvider :
            IDisposable
        {
            private readonly int cpuCount;

            private float currentC =
                float.NaN;

            private string source =
                "Unavailable";

            // XP only.
            private WinRing0Reader ring0;

            public CpuTemperatureProvider(
                int count)
            {
                cpuCount = count;

                if (!IsWindows10OrLater())
                {
                    ring0 =
                        WinRing0Reader
                        .TryCreateAndInstallOnXp();
                }
            }

            public float CurrentC
            {
                get
                {
                    return currentC;
                }
            }

            public string Source
            {
                get
                {
                    return source;
                }
            }

            public void Refresh()
            {
                // ========================================================
                // Windows XP / XP x64 to Win 8
                // ========================================================

                if (!IsWindows10OrLater())
                {
                    RefreshXpTemperature();
                    return;
                }

                // ========================================================
                // Windows 10 and newer
                //
                // Best-effort ACPI thermal-zone query.
                //
                // Important:
                //
                // Windows does not guarantee that
                // MSAcpi_ThermalZoneTemperature represents the actual CPU
                // package temperature. On many modern systems it is absent
                // entirely.
                // ========================================================

                float acpiTemperature;
                string acpiSource;

                if (TryReadAcpiThermalZone(
                    out acpiTemperature,
                    out acpiSource))
                {
                    currentC =
                        acpiTemperature;

                    source =
                        acpiSource;

                    return;
                }

                currentC =
                    float.NaN;

                source =
                    String.IsNullOrEmpty(acpiSource)
                    ? "CPU temperature not exposed by firmware"
                    : acpiSource;
            }

            private void RefreshXpTemperature()
            {
                if (ring0 != null)
                {
                    float t =
                        ring0.ReadAverageTemperature(
                            cpuCount);

                    if (IsTemperature(t))
                    {
                        currentC =
                            t;

                        source =
                            "Intel DTS / WinRing0";

                        return;
                    }

                    currentC =
                        float.NaN;

                    source =
                        "DTS sensor unavailable";

                    return;
                }

                currentC =
                    float.NaN;

                source =
                    "WinRing0 unavailable";
            }

            private static bool IsTemperature(
                float t)
            {
                return
                    !float.IsNaN(t) &&
                    !float.IsInfinity(t) &&
                    t > 0.0f &&
                    t < 150.0f;
            }

            private static bool TryReadAcpiThermalZone(
                out float temperatureC,
                out string temperatureSource)
            {
                temperatureC =
                    float.NaN;

                temperatureSource =
                    null;

                ManagementScope scope =
                    null;

                try
                {
                    ConnectionOptions options =
                        new ConnectionOptions();

                    options.EnablePrivileges =
                        true;

                    scope =
                        new ManagementScope(
                            @"\\.\root\WMI",
                            options);

                    scope.Connect();
                }
                catch (ManagementException ex)
                {
                    temperatureSource =
                        GetAcpiManagementError(
                            ex);

                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    temperatureSource =
                        "ACPI WMI access denied";

                    return false;
                }
                catch
                {
                    temperatureSource =
                        "ACPI WMI unavailable";

                    return false;
                }

                try
                {
                    ObjectQuery query =
                        new ObjectQuery(
                            "SELECT * FROM " +
                            "MSAcpi_ThermalZoneTemperature");

                    using (
                        ManagementObjectSearcher searcher =
                            new ManagementObjectSearcher(
                                scope,
                                query))
                    {
                        using (
                            ManagementObjectCollection results =
                                searcher.Get())
                        {
                            if (results == null ||
                                results.Count == 0)
                            {
                                temperatureSource =
                                    "No ACPI thermal zones exposed";

                                return false;
                            }

                            float bestTemperature =
                                float.NaN;

                            string bestInstance =
                                null;

                            int validZones =
                                0;

                            foreach (
                                ManagementObject zone
                                in results)
                            {
                                if (zone == null)
                                    continue;

                                // ------------------------------------------------
                                // Some implementations expose Active.
                                //
                                // Do not require it to exist.
                                // ------------------------------------------------

                                bool activeKnown;
                                bool active;

                                if (TryGetBooleanProperty(
                                    zone,
                                    "Active",
                                    out activeKnown,
                                    out active))
                                {
                                    if (activeKnown &&
                                        !active)
                                    {
                                        continue;
                                    }
                                }

                                // ------------------------------------------------
                                // CurrentTemperature
                                // ------------------------------------------------

                                object rawObject;

                                if (!TryGetProperty(
                                    zone,
                                    "CurrentTemperature",
                                    out rawObject))
                                {
                                    continue;
                                }

                                if (rawObject == null)
                                    continue;

                                uint rawTemperature;

                                try
                                {
                                    rawTemperature =
                                        Convert.ToUInt32(
                                            rawObject);
                                }
                                catch
                                {
                                    continue;
                                }

                                // ------------------------------------------------
                                // ACPI reports tenths of Kelvin.
                                //
                                // Reject:
                                //
                                // 0
                                // <= 2732 (approximately 0 C)
                                //
                                // because unsupported firmware frequently
                                // returns zero or invalid baseline values.
                                // ------------------------------------------------

                                if (rawTemperature == 0 ||
                                    rawTemperature <= 2732)
                                {
                                    continue;
                                }

                                float celsius =
                                    (rawTemperature /
                                        10.0f) -
                                    273.15f;

                                if (!IsTemperature(
                                    celsius))
                                {
                                    continue;
                                }

                                validZones++;

                                string instanceName =
                                    GetStringProperty(
                                        zone,
                                        "InstanceName");

                                // ------------------------------------------------
                                // Multiple ACPI thermal zones may exist.
                                //
                                // Keep the hottest plausible reading.
                                //
                                // This remains a heuristic. Windows does not
                                // guarantee that an ACPI thermal zone maps to
                                // the actual CPU package.
                                // ------------------------------------------------

                                if (float.IsNaN(
                                        bestTemperature) ||
                                    celsius >
                                        bestTemperature)
                                {
                                    bestTemperature =
                                        celsius;

                                    bestInstance =
                                        instanceName;
                                }
                            }

                            if (IsTemperature(
                                bestTemperature))
                            {
                                temperatureC =
                                    bestTemperature;

                                if (String.IsNullOrEmpty(
                                    bestInstance))
                                {
                                    temperatureSource =
                                        "ACPI Thermal Zone";
                                }
                                else
                                {
                                    temperatureSource =
                                        "ACPI Thermal Zone (" +
                                        bestInstance +
                                        ")";
                                }

                                return true;
                            }

                            if (validZones == 0)
                            {
                                temperatureSource =
                                    "ACPI thermal zones contain no valid temperature";
                            }
                            else
                            {
                                temperatureSource =
                                    "ACPI temperature unavailable";
                            }

                            return false;
                        }
                    }
                }
                catch (ManagementException ex)
                {
                    temperatureSource =
                        GetAcpiManagementError(
                            ex);

                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    temperatureSource =
                        "ACPI WMI access denied";

                    return false;
                }
                catch
                {
                    temperatureSource =
                        "ACPI thermal-zone query failed";

                    return false;
                }
            }

            // ============================================================
            // Safely retrieve a WMI property.
            //
            // Different BIOS/firmware implementations expose different
            // properties. Direct indexing such as:
            //
            //     zone["Active"]
            //
            // can itself throw ManagementException when the property is not
            // available.
            // ============================================================

            private static bool TryGetProperty(
                ManagementBaseObject obj,
                string propertyName,
                out object value)
            {
                value = null;

                if (obj == null ||
                    String.IsNullOrEmpty(
                        propertyName))
                {
                    return false;
                }

                try
                {
                    PropertyData property =
                        obj.Properties[
                            propertyName];

                    if (property == null)
                        return false;

                    value =
                        property.Value;

                    return true;
                }
                catch (ManagementException)
                {
                    return false;
                }
                catch
                {
                    return false;
                }
            }

            private static bool TryGetBooleanProperty(
                ManagementBaseObject obj,
                string propertyName,
                out bool propertyExists,
                out bool value)
            {
                propertyExists =
                    false;

                value =
                    false;

                object rawValue;

                if (!TryGetProperty(
                    obj,
                    propertyName,
                    out rawValue))
                {
                    return false;
                }

                propertyExists =
                    true;

                if (rawValue == null)
                    return true;

                try
                {
                    value =
                        Convert.ToBoolean(
                            rawValue);
                }
                catch
                {
                    // If conversion fails, treat the property as existing
                    // but unknown. Do not reject the thermal zone.
                    propertyExists =
                        false;
                }

                return true;
            }

            private static string GetStringProperty(
                ManagementBaseObject obj,
                string propertyName)
            {
                object value;

                if (!TryGetProperty(
                    obj,
                    propertyName,
                    out value))
                {
                    return null;
                }

                if (value == null)
                    return null;

                try
                {
                    string s =
                        value.ToString();

                    return
                        String.IsNullOrEmpty(s)
                        ? null
                        : s;
                }
                catch
                {
                    return null;
                }
            }

            // ============================================================
            // Convert ManagementException into a useful user-facing status.
            //
            // This prevents the UI from simply displaying:
            //
            //     ManagementException
            //
            // while still preserving useful diagnostic information.
            // ============================================================

            private static string GetAcpiManagementError(
                ManagementException ex)
            {
                if (ex == null)
                    return "ACPI WMI unavailable";

                switch (ex.ErrorCode)
                {
                    case ManagementStatus.InvalidNamespace:

                        return
                            "ACPI WMI namespace unavailable";

                    case ManagementStatus.InvalidClass:

                        return
                            "ACPI thermal zone class not exposed";

                    case ManagementStatus.NotFound:

                        return
                            "ACPI thermal zone not found";

                    case ManagementStatus.AccessDenied:

                        return
                            "ACPI WMI access denied";

                    case ManagementStatus.InvalidQuery:

                        return
                            "ACPI WMI query unsupported";

                    case ManagementStatus.ProviderFailure:

                        return
                            "ACPI WMI provider failed";

                    default:

                        return
                            "ACPI thermal zone unavailable";
                }
            }

            public void Dispose()
            {
                if (ring0 != null)
                {
                    ring0.Dispose();

                    ring0 =
                        null;
                }
            }
        }

        // ================================================================
        // WinRing0/OpenLibSys 1.3.0 reader.
        //
        // The reader only exposes READ_MSR to this application.
        //
        // The application never uses the driver's write-MSR functionality.
        // ================================================================

        private sealed class WinRing0Reader :
            IDisposable
        {
            private const uint IOCTL_OLS_READ_MSR =
                0x9C402084;

            private const uint MSR_THERM_STATUS =
                0x19C;

            private const uint MSR_PACKAGE_THERM_STATUS =
                0x1B1;

            private const uint MSR_TEMPERATURE_TARGET =
                0x1A2;

            private const uint OPEN_EXISTING =
                3;

            private const uint GENERIC_READ =
                0x80000000;

            private const uint GENERIC_WRITE =
                0x40000000;

            private const uint SERVICE_KERNEL_DRIVER =
                0x00000001;

            private const uint SERVICE_DEMAND_START =
                0x00000003;

            private const uint SERVICE_ERROR_NORMAL =
                0x00000001;

            private const uint SERVICE_CONTROL_STOP =
                0x00000001;

            private const uint SC_MANAGER_CREATE_SERVICE =
                0x0002;

            private const uint DELETE =
                0x00010000;

            private static readonly
                IntPtr INVALID_HANDLE_VALUE =
                    new IntPtr(-1);

            private const string SERVICE_NAME =
                "LegacyGpuMonitorWinRing0";

            private IntPtr handle;

            private bool installedByUs;

            private string driverPath;

            [DllImport(
                "kernel32.dll",
                CharSet = CharSet.Ansi,
                SetLastError = true)]

            private static extern IntPtr CreateFile(
                string name,
                uint access,
                uint share,
                IntPtr sec,
                uint creation,
                uint flags,
                IntPtr template);

            [DllImport(
                "kernel32.dll",
                SetLastError = true)]

            private static extern bool DeviceIoControl(
                IntPtr device,
                uint code,
                IntPtr inBuffer,
                uint inSize,
                IntPtr outBuffer,
                uint outSize,
                out uint returned,
                IntPtr overlapped);

            [DllImport(
                "kernel32.dll",
                SetLastError = true)]

            private static extern bool CloseHandle(
                IntPtr h);

            [DllImport(
                "advapi32.dll",
                CharSet = CharSet.Ansi,
                SetLastError = true)]

            private static extern IntPtr OpenSCManager(
                string machine,
                string database,
                uint access);

            [DllImport(
                "advapi32.dll",
                CharSet = CharSet.Ansi,
                SetLastError = true)]

            private static extern IntPtr CreateService(
                IntPtr scm,
                string name,
                string displayName,
                uint desiredAccess,
                uint serviceType,
                uint startType,
                uint errorControl,
                string binaryPath,
                string loadOrderGroup,
                IntPtr tagId,
                string dependencies,
                string serviceStartName,
                string password);

            [DllImport(
                "advapi32.dll",
                CharSet = CharSet.Ansi,
                SetLastError = true)]

            private static extern IntPtr OpenService(
                IntPtr scm,
                string name,
                uint access);

            [DllImport(
                "advapi32.dll",
                SetLastError = true)]

            private static extern bool StartService(
                IntPtr service,
                uint argc,
                IntPtr argv);

            [DllImport(
                "advapi32.dll",
                SetLastError = true)]

            private static extern bool ControlService(
                IntPtr service,
                uint control,
                out SERVICE_STATUS status);

            [StructLayout(
                LayoutKind.Sequential)]

            private struct SERVICE_STATUS
            {
                public uint ServiceType;
                public uint CurrentState;
                public uint ControlsAccepted;
                public uint Win32ExitCode;
                public uint ServiceSpecificExitCode;
                public uint CheckPoint;
                public uint WaitHint;
            }

            [DllImport(
                "advapi32.dll",
                SetLastError = true)]

            private static extern bool DeleteService(
                IntPtr service);

            [DllImport(
                "advapi32.dll",
                SetLastError = true)]

            private static extern bool CloseServiceHandle(
                IntPtr handle);

            [DllImport(
                "kernel32.dll")]

            private static extern IntPtr GetCurrentThread();

            [DllImport(
                "kernel32.dll",
                SetLastError = true)]

            private static extern UIntPtr
                SetThreadAffinityMask(
                    IntPtr hThread,
                    UIntPtr mask);

            private WinRing0Reader(
                IntPtr h,
                bool owned,
                string path)
            {
                handle =
                    h;

                installedByUs =
                    owned;

                driverPath =
                    path;
            }

            public static WinRing0Reader
                TryCreateAndInstallOnXp()
            {
                if (!IsWindowsXpWorkstation())
                    return null;

                // First use an already installed driver.

                WinRing0Reader existing =
                    TryOpenExisting();

                if (existing != null)
                    return existing;

                string path =
                    FindBundledDriver();

                if (String.IsNullOrEmpty(path) ||
                    !File.Exists(path))
                {
                    return null;
                }

                IntPtr scm =
                    OpenSCManager(
                        null,
                        null,
                        SC_MANAGER_CREATE_SERVICE);

                if (scm == IntPtr.Zero)
                    return null;

                IntPtr service =
                    IntPtr.Zero;

                bool created =
                    false;

                try
                {
                    service =
                        CreateService(
                            scm,
                            SERVICE_NAME,
                            "Resource Monitor WinRing0",
                            0xF01FF,
                            SERVICE_KERNEL_DRIVER,
                            SERVICE_DEMAND_START,
                            SERVICE_ERROR_NORMAL,
                            path,
                            null,
                            IntPtr.Zero,
                            null,
                            null,
                            null);

                    if (service == IntPtr.Zero)
                    {
                        service =
                            OpenService(
                                scm,
                                SERVICE_NAME,
                                0xF01FF);
                    }
                    else
                    {
                        created =
                            true;
                    }

                    if (service == IntPtr.Zero)
                        return null;

                    if (!StartService(
                        service,
                        0,
                        IntPtr.Zero))
                    {
                        int error =
                            Marshal.GetLastWin32Error();

                        // ERROR_SERVICE_ALREADY_RUNNING = 1056

                        if (error != 1056)
                        {
                            if (created)
                            {
                                DeleteService(
                                    service);
                            }

                            return null;
                        }
                    }
                }
                finally
                {
                    if (service != IntPtr.Zero)
                    {
                        CloseServiceHandle(
                            service);
                    }

                    CloseServiceHandle(
                        scm);
                }

                WinRing0Reader opened =
                    TryOpenExisting();

                if (opened != null)
                {
                    opened.installedByUs =
                        created;

                    opened.driverPath =
                        path;
                }

                return opened;
            }

            private static WinRing0Reader
                TryOpenExisting()
            {
                string[] devices =
                {
                    @"\\.\WinRing0_1_2_0",
                    @"\\.\WinRing0_1_2"
                };

                for (int i = 0;
                     i < devices.Length;
                     i++)
                {
                    IntPtr h =
                        CreateFile(
                            devices[i],
                            GENERIC_READ |
                            GENERIC_WRITE,
                            0,
                            IntPtr.Zero,
                            OPEN_EXISTING,
                            0,
                            IntPtr.Zero);

                    if (h != IntPtr.Zero &&
                        h != INVALID_HANDLE_VALUE)
                    {
                        return
                            new WinRing0Reader(
                                h,
                                false,
                                null);
                    }
                }

                return null;
            }

            private static bool
                IsWindowsXpWorkstation()
            {
                try
                {
                    Version v =
                        Environment.OSVersion.Version;

                    if (v.Major > 6)
                    {
                        return false;
                    }

                    using (
                        ManagementObjectSearcher q =
                            new ManagementObjectSearcher(
                                @"root\CIMV2",
                                "SELECT ProductType FROM Win32_OperatingSystem"))
                    using (
                        ManagementObjectCollection c =
                            q.Get())
                    {
                        foreach (
                            ManagementObject o
                            in c)
                        {
                            object value =
                                o["ProductType"];

                            if (value != null &&
                                Convert.ToInt32(
                                    value) == 1)
                            {
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                }

                return false;
            }

            private static string FindBundledDriver()
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // Select the correct driver for the current process architecture.
                // A 64-bit process requires the x64 WinRing0 driver.
                // A 32-bit process requires the x86 WinRing0 driver.
                string driverName = Environment.Is64BitProcess
                    ? "WinRing0x64.sys"
                    : "WinRing0.sys";

                // Preferred location: Drivers folder.
                string p1 = Path.Combine(
                    baseDir,
                    @"Drivers\" + driverName);

                if (File.Exists(p1))
                    return p1;

                // Fallback: application directory.
                string p2 = Path.Combine(
                    baseDir,
                    driverName);

                if (File.Exists(p2))
                    return p2;

                return null;
            }

            private bool ReadMsr(
                uint index,
                out ulong value)
            {
                value =
                    0;

                if (handle == IntPtr.Zero ||
                    handle ==
                        INVALID_HANDLE_VALUE)
                {
                    return false;
                }

                IntPtr input =
                    Marshal.AllocHGlobal(4);

                IntPtr output =
                    Marshal.AllocHGlobal(8);

                try
                {
                    Marshal.WriteInt32(
                        input,
                        unchecked(
                            (int)index));

                    Marshal.WriteInt64(
                        output,
                        0);

                    uint returned;

                    if (!DeviceIoControl(
                        handle,
                        IOCTL_OLS_READ_MSR,
                        input,
                        4,
                        output,
                        8,
                        out returned,
                        IntPtr.Zero) ||
                        returned < 8)
                    {
                        return false;
                    }

                    uint lo =
                        unchecked(
                            (uint)Marshal.ReadInt32(
                                output,
                                0));

                    uint hi =
                        unchecked(
                            (uint)Marshal.ReadInt32(
                                output,
                                4));

                    value =
                        ((ulong)hi << 32) |
                        lo;

                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(
                        input);

                    Marshal.FreeHGlobal(
                        output);
                }
            }

            public float
                ReadAverageTemperature(
                    int count)
            {
                if (count <= 0 ||
                    count > 64)
                {
                    return float.NaN;
                }

                // Package DTS is preferred. Fall back to core DTS.

                double packageSum =
                    0;

                int packageCount =
                    0;

                double coreSum =
                    0;

                int coreCount =
                    0;

                IntPtr thread =
                    GetCurrentThread();

                for (int cpu = 0;
                     cpu < count;
                     cpu++)
                {
                    UIntPtr mask =
                        new UIntPtr(
                            1UL <<
                            (cpu & 63));

                    UIntPtr previous =
                        UIntPtr.Zero;

                    try
                    {
                        previous =
                            SetThreadAffinityMask(
                                thread,
                                mask);

                        if (previous ==
                            UIntPtr.Zero)
                        {
                            continue;
                        }

                        ulong status;
                        ulong packageStatus;
                        ulong target;

                        if (ReadMsr(
                            MSR_TEMPERATURE_TARGET,
                            out target))
                        {
                            int tjmax =
                                (int)(
                                    (target >> 16) &
                                    0xFF);

                            // Intel Core i7-3940XM:
                            // documented TjMax = 105 C.
                            //
                            // Use 105 C if the old MSR path returns
                            // an obviously invalid value.

                            if (tjmax < 80 ||
                                tjmax > 120)
                            {
                                tjmax =
                                    105;
                            }

                            if (ReadMsr(
                                MSR_PACKAGE_THERM_STATUS,
                                out packageStatus))
                            {
                                int delta =
                                    (int)(
                                        (packageStatus >>
                                            16) &
                                        0x7F);

                                if (delta >= 0 &&
                                    delta < 127)
                                {
                                    double temp =
                                        tjmax -
                                        delta;

                                    if (temp > 0 &&
                                        temp < 150)
                                    {
                                        packageSum +=
                                            temp;

                                        packageCount++;
                                    }
                                }
                            }

                            if (ReadMsr(
                                MSR_THERM_STATUS,
                                out status))
                            {
                                int delta =
                                    (int)(
                                        (status >>
                                            16) &
                                        0x7F);

                                if (delta >= 0 &&
                                    delta < 127)
                                {
                                    double temp =
                                        tjmax -
                                        delta;

                                    if (temp > 0 &&
                                        temp < 150)
                                    {
                                        coreSum +=
                                            temp;

                                        coreCount++;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        if (previous !=
                            UIntPtr.Zero)
                        {
                            SetThreadAffinityMask(
                                thread,
                                previous);
                        }
                    }
                }

                if (packageCount > 0)
                {
                    return
                        (float)(
                            packageSum /
                            packageCount);
                }

                return coreCount > 0
                    ? (float)(
                        coreSum /
                        coreCount)
                    : float.NaN;
            }

            public void Dispose()
            {
                if (handle != IntPtr.Zero &&
                    handle !=
                        INVALID_HANDLE_VALUE)
                {
                    CloseHandle(
                        handle);

                    handle =
                        IntPtr.Zero;
                }

                // Only remove the service if this application created it.

                if (installedByUs &&
                    IsWindowsXpWorkstation())
                {
                    IntPtr scm =
                        OpenSCManager(
                            null,
                            null,
                            SC_MANAGER_CREATE_SERVICE);

                    if (scm != IntPtr.Zero)
                    {
                        IntPtr service =
                            OpenService(
                                scm,
                                SERVICE_NAME,
                                DELETE |
                                0x00000020);

                        if (service !=
                            IntPtr.Zero)
                        {
                            SERVICE_STATUS status;

                            ControlService(
                                service,
                                SERVICE_CONTROL_STOP,
                                out status);

                            DeleteService(
                                service);

                            CloseServiceHandle(
                                service);
                        }

                        CloseServiceHandle(
                            scm);
                    }
                }

                installedByUs =
                    false;

                driverPath =
                    null;
            }
        }
    }
}