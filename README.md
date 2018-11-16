# WindowsRuntimeDemos (c) James Forshaw 2018

This project contains demos from the presentation "The Inner Workings of the Windows Runtime"
presented at HITB-PEK and PacSec 2018.

PowerShell demos are under the demos folder. To run you need to install the OleViewDotNet 
PowerShell module from the PSGallery using the command:

```powershell
PS> Install-Module OleViewDotNet
```

Or if it's already installed make sure it's up to date.

The demos folder consists of:
* demo1.ps1 - Show runtime class and runtime extension registrations.
* demo2.ps1 - Activate the calculator Windows.Launch extension (you need a copy of calculator running).
* demo3.ps1 - Get proxy definitions for a runtime class.
* demo4.ps1 - Get accessible objects from an Edge content process (needs Edge running)

The other part of the project is the DllLoader. This allows you to load a DLL into process with an elevated
signature level (such as Edge or Store applications). It abuses KnownDlls to bootstrap the DLL. An example
DLL is provided PrintDebugDll which just prints text to debug output.