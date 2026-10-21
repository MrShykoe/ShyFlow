using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Island;

// Переключение устройства звука и микрофона по умолчанию. У Windows для этого нет открытого интерфейса,
// поэтому используется тот же недокументированный вызов, что и в системных настройках звука.
internal static class AudioSwitch
{
    public sealed record Dev(string Id, string Name, bool IsDefault);

    public static List<Dev> List(DataFlow flow)
    {
        var res = new List<Dev>();
        try
        {
            using var en = new MMDeviceEnumerator();
            string def = "";
            try { def = en.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID; } catch { }
            foreach (var d in en.EnumerateAudioEndPoints(flow, DeviceState.Active))
                res.Add(new Dev(d.ID, d.FriendlyName, d.ID == def));
        }
        catch { }
        return res;
    }

    public static bool SetDefault(string id)
    {
        try
        {
            var pc = (IPolicyConfig)new PolicyConfigClient();
            // для обычных программ, мультимедиа и связи (звонки)
            int a = pc.SetDefaultEndpoint(id, 0);
            int b = pc.SetDefaultEndpoint(id, 1);
            int c = pc.SetDefaultEndpoint(id, 2);
            return a == 0 || b == 0 || c == 0;
        }
        catch { return false; }
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Bool)] bool def, IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Bool)] bool def, IntPtr defPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Bool)] bool fx, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Bool)] bool fx, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string name, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Bool)] bool visible);
    }
}
