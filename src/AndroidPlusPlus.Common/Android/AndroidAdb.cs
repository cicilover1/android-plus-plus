﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.Common
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public static class AndroidAdb
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public interface StateListener
    {
      void DeviceConnected (AndroidDevice device);

      void DeviceDisconnected (AndroidDevice device);

      void DevicePervasive (AndroidDevice device);
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private static readonly object m_updateLockMutex = new object ();

    private static Hashtable m_connectedDevices = new Hashtable ();

    private static List<StateListener> m_registeredDeviceStateListeners = new List<StateListener> ();

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static void Refresh ()
    {
      lock (m_updateLockMutex)
      {
        // 
        // Start an ADB instance, if required.
        // 

        using (SyncRedirectProcess adbStartServer = new SyncRedirectProcess (AndroidSettings.SdkRoot + @"\platform-tools\adb.exe", "start-server"))
        {
          adbStartServer.StartAndWaitForExit ();
        }

        using (SyncRedirectProcess adbDevices = new SyncRedirectProcess (AndroidSettings.SdkRoot + @"\platform-tools\adb.exe", "devices"))
        {
          adbDevices.StartAndWaitForExit (1000);

          // 
          // Parse 'devices' output, skipping headers and potential 'start-server' output.
          // 

          Dictionary<string, string> currentConnectedDevices = new Dictionary<string, string> ();

          Trace.WriteLine (string.Format ("[AndroidAdb] Refresh: {0}", adbDevices.StandardOutput));

          if (!String.IsNullOrEmpty (adbDevices.StandardOutput))
          {
            string [] deviceOutputLines = adbDevices.StandardOutput.Split (new char [] { '\r', '\n' });

            foreach (string line in deviceOutputLines)
            {
              if (Regex.IsMatch (line, "^[A-Za-z0-9]+[\t][a-z]+$"))
              {
                string [] segments = line.Split (new char [] { '\t' });

                string deviceName = segments [0];

                string deviceType = segments [1];

                currentConnectedDevices.Add (deviceName, deviceType);
              }
            }
          }

          // 
          // Identify which devices have connected or persisted.
          // 

          foreach (KeyValuePair <string, string> connectedDevicePair in currentConnectedDevices)
          {
            string deviceName = connectedDevicePair.Key;

            string deviceType = connectedDevicePair.Value;

            if (m_connectedDevices.ContainsKey (connectedDevicePair.Key))
            {
              // 
              // Device is pervasive. Refresh internal properties.
              // 

              Trace.WriteLine (string.Format ("[AndroidAdb] Device pervaded: {0} - {1}", deviceName, deviceType));

              AndroidDevice pervasiveDevice = (m_connectedDevices [connectedDevicePair.Key] as AndroidDevice);

              pervasiveDevice.Refresh ();

              foreach (StateListener deviceListener in m_registeredDeviceStateListeners)
              {
                deviceListener.DevicePervasive (pervasiveDevice);
              }
            }
            else
            {
              // 
              // Device connected.
              // 

              Trace.WriteLine (string.Format ("[AndroidAdb] Device connected: {0} - {1}", deviceName, deviceType));

              AndroidDevice connectedDevice = new AndroidDevice (deviceName);

              connectedDevice.Refresh ();

              m_connectedDevices.Add (deviceName, connectedDevice);

              foreach (StateListener deviceListener in m_registeredDeviceStateListeners)
              {
                deviceListener.DeviceConnected (connectedDevice);
              }
            }
          }

          // 
          // Use a reverse lookup to identify devices which have been 'disconnected'.
          // 

          List<string> disconnectedDevices = new List<string> ();

          foreach (string key in m_connectedDevices.Keys)
          {
            string deviceName = (string)key;

            if (!currentConnectedDevices.ContainsKey (deviceName))
            {
              disconnectedDevices.Add (deviceName);
            }
          }

          foreach (string deviceName in disconnectedDevices)
          {
            // 
            // Device disconnected.
            // 

            AndroidDevice disconnectedDevice = (AndroidDevice)m_connectedDevices [deviceName];

            Trace.WriteLine (string.Format ("[AndroidAdb] Device disconnected: {0}", deviceName));

            m_connectedDevices.Remove (deviceName);

            foreach (StateListener deviceListener in m_registeredDeviceStateListeners)
            {
              deviceListener.DeviceDisconnected (disconnectedDevice);
            }
          }
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static AndroidDevice [] GetConnectedDevices ()
    {
      lock (m_connectedDevices)
      {
        uint i = 0;

        AndroidDevice [] deviceArray = new AndroidDevice [m_connectedDevices.Count];

        foreach (object key in m_connectedDevices.Keys)
        {
          AndroidDevice device = (AndroidDevice)m_connectedDevices [key];

          deviceArray [i++] = device;
        }

        Trace.Assert (i == m_connectedDevices.Count);

        return deviceArray;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static SyncRedirectProcess AdbCommand (string command, string arguments)
    {
      Trace.WriteLine (string.Format ("[AndroidDevice] AdbCommand: Cmd={0} Args={1}", command, arguments));

      SyncRedirectProcess adbCommand = new SyncRedirectProcess (AndroidSettings.SdkRoot + @"\platform-tools\adb.exe", string.Format ("-s {0} {1}", command, arguments));

      return adbCommand;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static SyncRedirectProcess AdbCommand (AndroidDevice target, string command, string arguments)
    {
      Trace.WriteLine (string.Format ("[AndroidDevice] AdbCommand: Target={0} Cmd={1} Args={2}", target.ID, command, arguments));

      SyncRedirectProcess adbCommand = new SyncRedirectProcess (AndroidSettings.SdkRoot + @"\platform-tools\adb.exe", string.Format ("-s {0} {1} {2}", target.ID, command, arguments));

      return adbCommand;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static AsyncRedirectProcess AdbCommandAsync (AndroidDevice target, string command, string arguments)
    {
      Trace.WriteLine (string.Format ("[AndroidDevice] AdbCommandAsync: Target={0} Cmd={1} Args={2}", target.ID, command, arguments));

      AsyncRedirectProcess adbCommand = new AsyncRedirectProcess (AndroidSettings.SdkRoot + @"\platform-tools\adb.exe", string.Format ("-s {0} {1} {2}", target.ID, command, arguments));

      return adbCommand;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static bool IsDeviceConnected (AndroidDevice queryDevice)
    {
      lock (m_connectedDevices)
      {
        foreach (object key in m_connectedDevices.Keys)
        {
          AndroidDevice device = (AndroidDevice)m_connectedDevices [key];

          if (queryDevice.ID == device.ID)
          {
            return true;
          }
        }
      }

      return false;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static void RegisterDeviceStateListener (StateListener listner)
    {
      lock (m_registeredDeviceStateListeners)
      {
        m_registeredDeviceStateListeners.Add (listner);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  }

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
