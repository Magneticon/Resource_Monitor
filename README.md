# Resource Monitor

A CPU/GPU/RAM Resource Monitor for Windows XP and later.

I have created it, so I can see utilization of my GTX 980M NVIDIA GPU, when loading AI models on my Windows XP x64 machine.

AMD/Intel GPUs probably won't be reported. Non-Intel CPUs might not be supported fully. On Windows 10 and newer, the WinRing0.sys / WinRing0x64.sys drivers are not loaded, as they contain security vulnerability, making Windows Defender to block them. Instead, the monitoring program will try ACPI temperature monitoring instead, with varying level of success.

But since all these information are available in Windows 10 task manager and I need to have these on my Windows XP machine, I won't put any effort to make it working on Windows 10.

Resource Monitor on Windows XP x64:
<img width="1000" height="900" alt="RESMON" src="https://github.com/user-attachments/assets/eb07ed23-5374-4d4c-aad5-476274c7f058" />

Resource Monitor on Windows 10 x64:
<img width="1920" height="1080" alt="RESMON2" src="https://github.com/user-attachments/assets/a6e1faa4-d4cc-40aa-b37d-d946be0827a4" />
