﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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

  public class CLangDebuggeeStackFrame : DebuggeeStackFrame
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private CLangDebugger m_debugger;

    private DebuggeeAddress m_memoryAddress;

    private string m_location;

    private bool m_locationIsFunction;

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebuggeeStackFrame (CLangDebugger debugger, CLangDebuggeeThread thread, uint level)
      : base (debugger.Engine, thread as DebuggeeThread)
    {
      m_debugger = debugger;

      Level = level;

      GetInfoFromCurrentLevel ();

      GetArguments ();

      GetLocals ();

      GetRegisters ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public uint Level { get; protected set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void GetInfoFromCurrentLevel ()
    {
      try
      {
        m_debugger.NativeProgram.SelectThread (m_thread as CLangDebuggeeThread);

        MiResultRecord resultRecord = m_debugger.GdbClient.SendCommand (string.Format ("-stack-list-frames {0} {0}", Level));

        if ((resultRecord != null) && (!resultRecord.IsError ()) && (resultRecord.HasField ("stack")))
        {
          MiResultValueTuple frameTuple = resultRecord ["stack"] [0] as MiResultValueTuple;

          if (frameTuple != null)
          {
            /*if (frameTuple.HasField ("level"))
            {
              Level = frameTuple ["level"].GetUnsignedInt ();
            }*/

            m_memoryAddress = new DebuggeeAddress (frameTuple ["addr"].GetString ());

            // 
            // Discover the function or shared library location.
            // 

            if (frameTuple.HasField ("func"))
            {
              m_location = frameTuple ["func"].GetString ();

              m_locationIsFunction = (m_location != "??") ? true : false;
            }
            else if (frameTuple.HasField ("from"))
            {
              // The shared library where this function is defined. This is only given if the frame's function is not known.

              m_location = frameTuple ["from"].GetString ();

              m_locationIsFunction = false;
            }
            else
            {
              m_location = "<unknown>";

              m_locationIsFunction = false;
            }

            // 
            // Generate code and document contexts for this frame location.
            // 

            if (frameTuple.HasField ("fullname") && frameTuple.HasField ("line"))
            {
              TEXT_POSITION [] textPositions = new TEXT_POSITION [2];

              textPositions [0].dwLine = frameTuple ["line"].GetUnsignedInt () - 1;

              textPositions [0].dwColumn = 0;

              textPositions [1].dwLine = textPositions [0].dwLine;

              textPositions [1].dwColumn = textPositions [0].dwColumn;

              string filename = StringUtils.ConvertPathPosixToWindows (frameTuple ["fullname"].GetString ());

              m_documentContext = new DebuggeeDocumentContext (m_debugger.Engine, filename, textPositions [0], textPositions [1], DebugEngineGuids.guidLanguageCpp, m_memoryAddress);

              m_codeContext = m_documentContext.GetCodeContext ();
            }
          }
        }
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public Dictionary<string, DebuggeeProperty> GetArguments ()
    {
      // 
      // Returns a list of arguments for the current stack level. Caches results for faster lookup.
      // 

      try
      {
        if (m_stackArguments.Count == 0)
        {
          MiResultRecord resultRecord = m_debugger.GdbClient.SendCommand ("-stack-list-arguments " + string.Format ("0 {0} {0}", Level));

          if ((resultRecord != null) && (!resultRecord.IsError ()) && (resultRecord.HasField ("stack-args")))
          {
            MiResultValue stackLevelArguments = resultRecord ["stack-args"] [0] ["args"];

            lock (m_stackArguments)
            {
              for (int i = 0; i < stackLevelArguments.Count; ++i)
              {
                string argument = stackLevelArguments [i] ["name"].GetString ();

                if (!string.IsNullOrEmpty (argument))
                {
                  CLangDebuggeeProperty property = new CLangDebuggeeProperty (m_debugger, this, argument, null);

                  m_stackArguments.Add (argument, property);
                }
              }
            }
          }
        }
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }

      return m_stackArguments;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public Dictionary<string, DebuggeeProperty> GetLocals ()
    {
      // 
      // Returns a list of local variables for the current stack level. Caches results for faster lookup.
      // 

      try
      {
        if (m_stackLocals.Count == 0)
        {
          m_debugger.GdbClient.SendCommand (string.Format ("-stack-select-frame {0}", Level));

          MiResultRecord resultRecord = m_debugger.GdbClient.SendCommand ("-stack-list-locals 0");

          if ((resultRecord != null) && (!resultRecord.IsError ()) && (resultRecord.HasField ("locals")))
          {
            MiResultValue localVariables = resultRecord ["locals"];

            lock (m_stackLocals)
            {
              for (int i = 0; i < localVariables.Count; ++i)
              {
                string variable = localVariables [i] ["name"].GetString ();

                if (!string.IsNullOrEmpty (variable))
                {
                  CLangDebuggeeProperty property = new CLangDebuggeeProperty (m_debugger, this, variable, null);

                  m_stackLocals.Add (variable, property);
                }
              }
            }
          }
        }
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }

      return m_stackLocals;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public Dictionary<string, DebuggeeProperty> GetRegisters ()
    {
      // 
      // Returns a list of registers for the current stack level. Caches results for faster lookup.
      // 

      try
      {
        if (m_stackRegisters.Count == 0)
        {

          // TODO: Change requested type based on radix.
          MiResultRecord registerValueRecord = m_debugger.GdbClient.SendCommand ("-data-list-register-values r");

          if ((registerValueRecord == null) || (registerValueRecord.IsError ()) || (!registerValueRecord.HasField ("register-values")))
          {
            throw new InvalidOperationException ("Failed to retrieve list of register values");
          }

          MiResultRecord registerNamesRecord = m_debugger.GdbClient.SendCommand ("-data-list-register-names");

          if ((registerNamesRecord == null) || (registerNamesRecord.IsError ()) || (!registerNamesRecord.HasField ("register-names")))
          {
            throw new InvalidOperationException ("Failed to retrieve list of register names");
          }

          MiResultValue registerValues = registerValueRecord ["register-values"];

          MiResultValue registerNames = registerNamesRecord ["register-names"];

          lock (m_stackRegisters)
          {
            for (int i = 0; i < registerValues.Count; ++i)
            {
              int number = registerValues [i] ["number"].GetInt ();

              string value = registerValues [i] ["value"].GetString ();

              string register = registerNames [number].GetString ();

              if (!string.IsNullOrEmpty (register))
              {
                string prettified = "$" + register;

                CLangDebuggeeProperty property = new CLangDebuggeeProperty (m_debugger, this, prettified, null);

                property.Value = value;

                m_stackRegisters.Add (prettified, property);
              }
            }
          }
        }
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }

      return m_stackRegisters;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public DebuggeeProperty EvaluateCustomExpression (string expression, uint radix)
    {
      // 
      // Evaluates a custom property lookup, and registers a new entry for this expression if one can't be found.
      // 

      DebuggeeProperty property = null;

      try
      {
        if (m_stackArguments.TryGetValue (expression, out property))
        {
          return property;
        }

        if (m_stackLocals.TryGetValue (expression, out property))
        {
          return property;
        }

        if (m_stackRegisters.TryGetValue (expression, out property))
        {
          return property;
        }

        if (m_customExpressions.TryGetValue (expression, out property))
        {
          return property;
        }

        property = new CLangDebuggeeProperty (m_debugger, this, expression, null);

        m_customExpressions.Add (expression, property);
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }

      return property;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public override int SetFrameInfo (enum_FRAMEINFO_FLAGS requestedFlags, uint radix, ref FRAMEINFO frameInfo)
    {
      LoggingUtils.PrintFunction ();

      try
      {
        frameInfo.m_dwValidFields = 0;

        if ((requestedFlags & enum_FRAMEINFO_FLAGS.FIF_FUNCNAME) != 0)
        {
          frameInfo.m_bstrFuncName = m_location;

          frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_FUNCNAME;
        }

        if (((requestedFlags & enum_FRAMEINFO_FLAGS.FIF_LANGUAGE) != 0) && (m_documentContext != null))
        {
          string languageName = string.Empty;

          Guid languageGuid = Guid.Empty;

          m_documentContext.GetLanguageInfo (ref languageName, ref languageGuid);

          frameInfo.m_bstrLanguage = languageName;

          frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_LANGUAGE;
        }

        if ((requestedFlags & enum_FRAMEINFO_FLAGS.FIF_FRAME) != 0)
        {
          frameInfo.m_pFrame = this;

          frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_FRAME;
        }

        if ((requestedFlags & enum_FRAMEINFO_FLAGS.FIF_STACKRANGE) != 0)
        {
          frameInfo.m_addrMin = 0L;

          frameInfo.m_addrMax = 0L;

          frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_STACKRANGE;
        }

        if ((requestedFlags & enum_FRAMEINFO_FLAGS.FIF_DEBUGINFO) != 0)
        {
          frameInfo.m_fHasDebugInfo = (m_locationIsFunction) ? 1 : 0;

          frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_DEBUGINFO;
        }

        if ((requestedFlags & enum_FRAMEINFO_FLAGS.FIF_STALECODE) != 0)
        {
          frameInfo.m_fStaleCode = 0;

          frameInfo.m_dwValidFields |= enum_FRAMEINFO_FLAGS.FIF_STALECODE;
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

    public override int GetLanguageInfo (ref string languageName, ref Guid languageGuid)
    {
      // 
      // Gets the language associated with this stack frame. 
      // 

      LoggingUtils.PrintFunction ();

      languageName = "C++";

      languageGuid = DebugEngineGuids.guidLanguageCpp;

      try
      {
        IDebugDocumentContext2 documentContext = null;

        GetDocumentContext (out documentContext);

        if (documentContext != null)
        {
          LoggingUtils.RequireOk (documentContext.GetLanguageInfo (ref languageName, ref languageGuid));
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

  }

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
