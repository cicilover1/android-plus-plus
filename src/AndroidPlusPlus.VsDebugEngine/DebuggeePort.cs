﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.VisualStudio.Debugger.Interop;
using AndroidPlusPlus.Common;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.VsDebugEngine
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public class DebuggeePort : IDebugPort2, IDebugPortNotify2, IConnectionPoint, IConnectionPointContainer
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private class Enumerator : DebugEnumerator<IDebugPort2, IEnumDebugPorts2>, IEnumDebugPorts2
    {
      public Enumerator (List<IDebugPort2> ports)
        : base (ports)
      {
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private readonly IDebugPortSupplier2 m_portSupplier;

    private readonly AndroidDevice m_portDevice;

    private readonly Guid m_portGuid;

    private List<IDebugProcess2> m_debugProcesses;

    private Dictionary<int, IDebugPortEvents2> m_eventConnectionPoints;

    private int m_eventConnectionPointCookie = 0;

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public DebuggeePort (IDebugPortSupplier2 portSupplier, AndroidDevice device)
    {
      m_portSupplier = portSupplier;

      m_portDevice = device;

      m_portGuid = Guid.NewGuid ();

      m_debugProcesses = new List<IDebugProcess2> ();

      m_eventConnectionPoints = new Dictionary<int, IDebugPortEvents2> ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public AndroidDevice PortDevice 
    {
      get
      {
        return m_portDevice;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private int RefreshProcesses ()
    {
      // 
      // Check which processes are currently running on the target device (port).
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        AndroidAdb.Refresh ();

        AndroidProcess [] activeDeviceProcesses = m_portDevice.GetProcesses ();

        lock (m_debugProcesses)
        {
          m_debugProcesses.Clear ();

          foreach (AndroidProcess process in activeDeviceProcesses)
          {
            m_debugProcesses.Add (new DebuggeeProcess (this, process));
          }
        }

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #region IDebugPort2 Members

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int EnumProcesses (out IEnumDebugProcesses2 ppEnum)
    {
      // 
      // Returns a list of all the processes running on a port.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        LoggingUtils.RequireOk (RefreshProcesses ());

        ppEnum = new DebuggeeProcess.Enumerator (m_debugProcesses);

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        ppEnum = null;

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetPortId (out Guid pguidPort)
    {
      // 
      // Gets the port identifier.
      // 

      LoggingUtils.PrintFunction ();

      pguidPort = m_portGuid;

      return DebugEngineConstants.S_OK;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetPortName (out string pbstrName)
    {
      // 
      // Gets the port name.
      // 

      LoggingUtils.PrintFunction ();

      pbstrName = "adb://" + m_portDevice.ID;

      return DebugEngineConstants.S_OK;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetPortRequest (out IDebugPortRequest2 ppRequest)
    {
      // 
      // Gets the description of a port that was previously used to create the port (if available).
      // 

      LoggingUtils.PrintFunction ();

      ppRequest = null;

      return DebugEngineConstants.E_PORT_NO_REQUEST;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetPortSupplier (out IDebugPortSupplier2 ppSupplier)
    {
      // 
      // Gets the port supplier for this port.
      // 

      LoggingUtils.PrintFunction ();

      ppSupplier = m_portSupplier;

      return DebugEngineConstants.S_OK;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetProcess (AD_PROCESS_ID ProcessId, out IDebugProcess2 ppProcess)
    {
      // 
      // Gets the specified process running on a port.
      // 

      LoggingUtils.PrintFunction ();

      ppProcess = null;

      try
      {
        if (ProcessId.ProcessIdType == (uint)enum_AD_PROCESS_ID.AD_PROCESS_ID_SYSTEM)
        {
          LoggingUtils.RequireOk (RefreshProcesses ());

          foreach (DebuggeeProcess process in m_debugProcesses)
          {
            if (process.NativeProcess.Pid == ProcessId.dwProcessId)
            {
              ppProcess = process;

              return DebugEngineConstants.S_OK;
            }
          }
        }
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }

      return DebugEngineConstants.E_FAIL;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #endregion

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #region IDebugPortNotify2 Members

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int AddProgramNode (IDebugProgramNode2 pProgramNode)
    {
      // 
      // Registers a program that can be debugged with the port it is running on.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        IDebugProcess2 process;

        DebuggeeProgram program = pProgramNode as DebuggeeProgram;

        LoggingUtils.RequireOk (program.GetProcess (out process));

        foreach (IDebugPortEvents2 connectionPoint in m_eventConnectionPoints.Values)
        {
          DebugEngineEvent.ProgramCreate debugEvent = new DebugEngineEvent.ProgramCreate ();

          Guid eventGuid = ComUtils.GuidOf (debugEvent);

          LoggingUtils.RequireOk (connectionPoint.Event (null, this, process, program, debugEvent, eventGuid));
        }

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }

      return DebugEngineConstants.E_FAIL;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int RemoveProgramNode (IDebugProgramNode2 pProgramNode)
    {
      // 
      // Unregisters a program that can be debugged from the port it is running on.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        throw new NotImplementedException ();
      }
      catch (NotImplementedException e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_NOTIMPL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #endregion

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #region IConnectionPoint Members

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Advise (object pUnkSink, out int pdwCookie)
    {
      // 
      // Establishes an advisory connection between the connection point and the caller's sink object.
      // 

      LoggingUtils.PrintFunction ();

      IDebugPortEvents2 portEvent = (IDebugPortEvents2) pUnkSink;

      if (portEvent != null)
      {
        m_eventConnectionPoints.Add (m_eventConnectionPointCookie, portEvent);

        pdwCookie = m_eventConnectionPointCookie++;

        return;
      }

      pdwCookie = 0;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void EnumConnections (out IEnumConnections ppEnum)
    {
      // 
      // Creates an enumerator object for iteration through the connections that exist to this connection point.
      // 

      LoggingUtils.PrintFunction ();

      throw new NotImplementedException ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void GetConnectionInterface (out Guid pIID)
    {
      // 
      // Returns the IID of the outgoing interface managed by this connection point.
      // 

      LoggingUtils.PrintFunction ();

      pIID = typeof (IDebugPortEvents2).GUID;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void GetConnectionPointContainer (out IConnectionPointContainer ppCPC)
    {
      // 
      // Retrieves the IConnectionPointContainer interface pointer to the connectable object that conceptually owns this connection point.
      // 

      LoggingUtils.PrintFunction ();

      ppCPC = this;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Unadvise (int dwCookie)
    {
      // 
      // Terminates an advisory connection previously established through the System.Runtime.InteropServices.ComTypes.IConnectionPoint.Advise(System.Object,System.Int32@) method.
      // 

      LoggingUtils.PrintFunction ();

      m_eventConnectionPoints.Remove (dwCookie);
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #endregion

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #region IConnectionPointContainer Members

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void EnumConnectionPoints (out IEnumConnectionPoints ppEnum)
    {
      // 
      // Creates an enumerator of all the connection points supported in the connectable object, one connection point per IID.
      // 

      LoggingUtils.PrintFunction ();

      throw new NotImplementedException ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void FindConnectionPoint (ref Guid riid, out IConnectionPoint ppCP)
    {
      // 
      // Asks the connectable object if it has a connection point for a particular IID, 
      // and if so, returns the IConnectionPoint interface pointer to that connection point.
      // 

      LoggingUtils.PrintFunction ();

      Guid connectionPort;

      GetConnectionInterface (out connectionPort);

      if (riid.Equals (connectionPort))
      {
        ppCP = this;

        return;
      }

      ppCP = null;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #endregion

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
