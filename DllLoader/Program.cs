//    This file is part of WindowsRuntimeDemos.
//    Copyright (C) James Forshaw 2018
//
//    WindowsRuntimeDemos is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    WindowsRuntimeDemos is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with WindowsRuntimeDemos.  If not, see <http://www.gnu.org/licenses/>.

using NtApiDotNet;
using NtApiDotNet.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DllLoader
{
    class Program
    {
        const string ENTRYPOINT = "EntryPoint";
        const string FAKEDLLNAME = "TAPI32.DLL";

        // Update this for new versions of NTDLL.
        // Using WinDBG, get version string from 'lm vm ntdll' and find the ProductVersion.
        // For the address run '? ntdll!LdrpKnownDllDirectoryHandle-ntdll'
        static long GetHandleOffset()
        {
            FileVersionInfo file_version = FileVersionInfo.GetVersionInfo(Path.Combine(Environment.SystemDirectory, @"ntdll.dll"));
            switch (file_version.ProductVersion)
            {
                case "10.0.17763.1":
                    return 0x00164f10;
                case "10.0.17134.228":
                    return 0x0015bef0;
            }
            throw new ArgumentException($"Unknown NTDLL version {file_version.ProductVersion}");
        }

        static long GetHandleAddress()
        {
            return SafeLoadLibraryHandle.GetModuleHandle("ntdll.dll").DangerousGetHandle().ToInt64() + GetHandleOffset();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern SafeKernelObjectHandle CreateRemoteThread(
          SafeKernelObjectHandle hProcess,
          IntPtr lpThreadAttributes,
          IntPtr dwStackSize,
          IntPtr lpStartAddress,
          IntPtr lpParameter,
          int dwCreationFlags,
          OptionalInt32 lpThreadId
        );

        static SecurityDescriptor CreateSecurityDescriptor()
        {
            return new SecurityDescriptor("D:(A;;GA;;;WD)(A;;GA;;;AC)(A;;GA;;;S-1-15-2-2)");
        }

        static void UpdateDacl(string typename, NtDirectory root, string name, SecurityDescriptor sd)
        {
            using (var obj = NtObject.OpenWithType(typename, name, root, AttributeFlags.CaseInsensitive, GenericAccessRights.WriteDac, null, false))
            {
                if (obj.IsSuccess)
                {
                    obj.Result.SetSecurityDescriptor(sd, SecurityInformation.Dacl);
                }
            }
        }

        static void FixupDbgView()
        {
            var sd = CreateSecurityDescriptor();
            using (NtDirectory objdir = NtDirectory.OpenBaseNamedObjects())
            {
                UpdateDacl("event", objdir, "DBWIN_DATA_READY", sd);
                UpdateDacl("section", objdir, "DBWIN_BUFFER", sd);
                UpdateDacl("event", objdir, "DBWIN_BUFFER_READY", sd);
            }
        }

        static ObjectAttributes CreateObjectAttributes(string name, NtObject root)
        {
            return new ObjectAttributes(name, AttributeFlags.CaseInsensitive, root, null, CreateSecurityDescriptor()); 
        }

        static NtSection CreateSection(string file_path, string name, NtDirectory root)
        {
            using (var file = NtFile.Open(NtFileUtils.DosFileNameToNt(file_path), null,
                        FileAccessRights.GenericRead | FileAccessRights.GenericExecute,
                        FileShareMode.Read | FileShareMode.Delete, FileOpenOptions.NonDirectoryFile))
            {
                using (var obja = CreateObjectAttributes(name, root))
                {
                    return NtSection.Create(obja, SectionAccessRights.MaximumAllowed,
                            null, MemoryAllocationProtect.ExecuteRead, SectionAttributes.Image, file);
                }
            }
        }

        static NtDirectory CreateDirectory()
        {
            using (var obja = CreateObjectAttributes(null, null))
            {
                return NtDirectory.Create(obja, DirectoryAccessRights.MaximumAllowed, null);
            }
        }

        static string GetBaseDirectory()
        {
            return Path.GetDirectoryName(typeof(Program).Assembly.Location);
        }

        static IntPtr WriteString(NtProcess process, string str)
        {
            byte[] data = Encoding.Unicode.GetBytes(str + "\0");
            var mem = process.AllocateMemory(data.Length);
            process.WriteMemory(mem, data);
            return new IntPtr(mem);
        }

        static void CallMethod(NtProcess proc, IntPtr entry_point, IntPtr arg_ptr)
        {
            using (var load_thread = NtThread.FromHandle(CreateRemoteThread(proc.Handle, 
                IntPtr.Zero, IntPtr.Zero, entry_point, arg_ptr, 0, null)))
            {
                load_thread.Wait();
            }
        }

        static void Main(string[] args)
        {
            try
            {
                if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
                {
                    Console.WriteLine("Must run as x64 binary on this OS");
                    return;
                }

                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: PID PathToDll [optional argument]");
                    return;
                }

                int pid = int.Parse(args[0]);
                string dllpath = Path.GetFullPath(args[1]);
                string argstr = args.Length > 2 ? args[2] : string.Empty;
                Console.WriteLine("Loading {0} into pid {1}", dllpath, pid);

                FixupDbgView();

                using (var list = new DisposableList())
                {
                    long handle_ptr = GetHandleAddress();
                    var proc = list.AddResource(NtProcess.Open(pid, ProcessAccessRights.AllAccess));
                    var old_handle_value = proc.ReadMemory<IntPtr>(handle_ptr);
                    var lib = list.AddResource(SafeLoadLibraryHandle.LoadLibrary(dllpath));
                    var entry_point = lib.GetProcAddress(ENTRYPOINT);
                    if (entry_point == IntPtr.Zero)
                    {
                        throw new ArgumentException($"Can't find {ENTRYPOINT} export.");
                    }

                    var dir = list.AddResource(CreateDirectory());
                    var image_section = list.AddResource(CreateSection(dllpath, FAKEDLLNAME, dir));

                    IntPtr handle_value = dir.DuplicateTo(proc);
                    var dllname_ptr = WriteString(proc, FAKEDLLNAME);
                    var kernel32 = SafeLoadLibraryHandle.GetModuleHandle("kernelbase.dll");
                    var load_lib = kernel32.GetProcAddress("LoadLibraryW");

                    proc.WriteMemory(handle_ptr, handle_value);
                    try
                    {
                        // Call LoadLibraryW with the path to the fake DLL name.
                        CallMethod(proc, load_lib, dllname_ptr);
                        // Call the DLL entry point.
                        CallMethod(proc, entry_point, WriteString(proc, argstr));
                    }
                    finally
                    {
                        // Restore the handle value.
                        proc.WriteMemory(handle_ptr, old_handle_value);
                    }
                    Console.WriteLine("Done");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
