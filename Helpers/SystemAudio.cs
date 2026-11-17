using System;
using System.Runtime.InteropServices;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Tells whether the system is currently producing audible sound, i.e. the default playback
    /// device is neither muted nor turned down to zero. Uses the Core Audio COM interfaces
    /// directly, so no additional packages are needed.
    /// </summary>
    internal static class SystemAudio
    {
        private const int CLSCTX_ALL = 23;
        private const int E_RENDER = 0;      // EDataFlow.eRender
        private const int E_CONSOLE = 0;     // ERole.eConsole

        /// <summary>
        /// True if the default render device is not muted and its master volume is above zero.
        /// Never throws; no audio device or any error counts as not audible.
        /// </summary>
        public static bool IsAudible()
        {
            object enumeratorObject = null;
            IMMDevice device = null;
            object volumeObject = null;

            try
            {
                enumeratorObject = new MMDeviceEnumeratorComObject();
                var enumerator = (IMMDeviceEnumerator)enumeratorObject;

                if (enumerator.GetDefaultAudioEndpoint(E_RENDER, E_CONSOLE, out device) != 0 || device == null)
                    return false;

                var volumeIid = IAudioEndpointVolumeGuid;
                if (device.Activate(ref volumeIid, CLSCTX_ALL, IntPtr.Zero, out volumeObject) != 0 || volumeObject == null)
                    return false;

                var volume = (IAudioEndpointVolume)volumeObject;

                bool muted;
                if (volume.GetMute(out muted) != 0 || muted)
                    return false;

                float level;
                if (volume.GetMasterVolumeLevelScalar(out level) != 0)
                    return false;

                return level > 0f;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                ReleaseIfComObject(volumeObject);
                ReleaseIfComObject(device);
                ReleaseIfComObject(enumeratorObject);
            }
        }

        private static readonly Guid IAudioEndpointVolumeGuid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

        private static void ReleaseIfComObject(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        }

        #region Core Audio COM interop

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject
        {
        }

        // Only GetDefaultAudioEndpoint is used; the other slots keep the vtable order intact.
        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice(IntPtr id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        // Activate is used; the other slots keep the vtable order intact.
        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object activated);
            [PreserveSig] int OpenPropertyStore(int access, IntPtr properties);
            [PreserveSig] int GetId(out IntPtr id);
            [PreserveSig] int GetState(out int state);
        }

        // GetMute and GetMasterVolumeLevelScalar are used; the other slots keep the vtable order intact.
        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint channelCount);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
            [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
            [PreserveSig] int VolumeStepUp(ref Guid eventContext);
            [PreserveSig] int VolumeStepDown(ref Guid eventContext);
            [PreserveSig] int QueryHardwareSupport(out uint mask);
            [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
        }

        #endregion
    }
}
