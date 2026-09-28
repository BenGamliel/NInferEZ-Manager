using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NInferManager.Backend;

public sealed class ProcessJob : IDisposable
{
    private IntPtr _handle;
    public ProcessJob()
    {
        _handle=CreateJobObject(IntPtr.Zero,null);
        if(_handle==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not create process job.");
        var info=new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();info.BasicLimitInformation.LimitFlags=0x00002000;
        var length=Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();var pointer=Marshal.AllocHGlobal(length);
        try{Marshal.StructureToPtr(info,pointer,false);if(!SetInformationJobObject(_handle,9,pointer,(uint)length))throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not configure process job.");}
        finally{Marshal.FreeHGlobal(pointer);}
    }
    public void Assign(Process process){if(!AssignProcessToJobObject(_handle,process.Handle))throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not attach NInfer to the manager process job.");}
    public void Dispose(){if(_handle==IntPtr.Zero)return;CloseHandle(_handle);_handle=IntPtr.Zero;}
    [StructLayout(LayoutKind.Sequential)]private struct IO_COUNTERS{public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}
    [StructLayout(LayoutKind.Sequential)]private struct JOBOBJECT_BASIC_LIMIT_INFORMATION{public long PerProcessUserTimeLimit,PerJobUserTimeLimit;public uint LimitFlags;public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
    [StructLayout(LayoutKind.Sequential)]private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION{public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;public IO_COUNTERS IoInfo;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool SetInformationJobObject(IntPtr job,int infoClass,IntPtr info,uint length);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr handle);
}
