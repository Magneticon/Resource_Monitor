# Resource Monitor

A CPU/GPU/RAM Resource Monitor for Windows XP and later.

I have created it, so I can see utilization of my GTX 980M NVIDIA GPU, when loading AI models on my Windows XP x64 machine.

AMD/Intel GPUs probably won't be reported. Non-Intel CPUs might not be supported fully. On Windows 10 and newer, the WinRing0.sys / WinRing0x64.sys drivers are not loaded, as they contain security vulnerability, making Windows Defender to block them. Instead, the monitoring program will try ACPI temperature monitoring instead, with varying level of success.

But since all these information are available in Windows 10 task manager and I need to have these on my Windows XP machine, I won't put any effort to make it working on Windows 10.

Resource Monitor on Windows XP x64:
<img width="1000" height="900" alt="RES_MON" src="https://github.com/user-attachments/assets/561239e9-f671-44d6-be50-e82346636742" />

Resource Monitor on Windows 10 x64:
<img width="1920" height="1080" alt="RESMON2" src="https://github.com/user-attachments/assets/a6e1faa4-d4cc-40aa-b37d-d946be0827a4" />

Note: WinRing0.sys / WinRing0x64.sys CPU monitoring drivers are not my work, and they come with modified BSD license, not subject of license of the Resource Monitoring program. These drivers are not part of Windows 10+ Resource Monitoring executables.

WinRing0 1.3.0 license notice
============================

Copyright (c) 2007-2009 OpenLibSys.org. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:
1. Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
