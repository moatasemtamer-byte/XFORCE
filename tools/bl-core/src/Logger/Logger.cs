/********************************************************************************************
* Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
*
* Purpose           : Logger activities.
*
* Rev  Date         By      Purpose
* ---- ------------ ------- ---------------------------------------------------------------------
* 01   2016-05-30   AG     Draft created. The initial version.
 ********************************************************************************************/

using System;
using System.Collections.Generic;



namespace BL
{
    public static class Logger
    {
        public static event LoggerEventHandler LogEvent;

        public static bool LogError(object sender, string Message)
        {
            bool res = true;
            try
            {
                List<string> msg = new List<string>();
                msg.Add(Message);
                LogEvent(sender, msg.ToArray(), LoggerKind.Error);
            }
            catch (Exception)
            {
                res = false;
            }
            return res;
        }

        public static bool LogError(object sender, string[] Message)
        {
            bool res = true;
            try
            {
                LogEvent(sender, Message, LoggerKind.Error);
            }
            catch (Exception)
            {
                res = false;
            }
            return res;
        }

        public static bool LogInfo(object sender, string Message)
        {
            bool res = true;
            try
            {
                List<string> msg = new List<string>();
                msg.Add(Message);
                LogEvent(sender, msg.ToArray(), LoggerKind.Info);
            }
            catch (Exception)
            {
                res = false;
            }
            return res;
        }

        public static bool LogInfo(object sender, string[] Message)
        {
            bool res = true;
            try
            {
                LogEvent(sender, Message, LoggerKind.Info);
            }
            catch (Exception)
            {
                res = false;
            }
            return res;
        }

        public static bool LogWarning(object sender, string Message)
        {
            bool res = true;
            try
            {
                //warning
                List<string> msg = new List<string>();
                msg.Add(Message);
                LogEvent(sender, msg.ToArray(), LoggerKind.Warning);
            }
            catch (Exception)
            {
                res = false;
            }
            return res;
        }

        public static bool LogWarning(object sender, string[] Message)
        {
            bool res = true;
            try
            {
                LogEvent(sender, Message, LoggerKind.Warning);
            }
            catch (Exception)
            {
                res = false;
            }
            return res;
        }

        public static bool LogText(object sender, string Message)
        {
            bool res = true;
            try
            {
                List<string> msg = new List<string>();
                msg.Add(Message);
                LogEvent(sender, msg.ToArray(), LoggerKind.Text);
            }
            catch (Exception)
            {
                res = false;
            }
            return res;
        }

        public static bool LogText(object sender, string[] Message)
        {
            bool res = true;
            try
            {
                LogEvent(sender, Message, LoggerKind.Text);
            }
            catch (Exception)
            {
                res = false;
            }
            return res;
        }

        public static bool LogFile(object sender, string Message)
        {
            bool res = true;
            try
            {
                List<string> msg = new List<string>();
                msg.Add(Message);
                LogEvent(sender, msg.ToArray(), LoggerKind.File);
            }
            catch (Exception)
            {
                res = false;
            }
            return res;
        }


    }

    public delegate void LoggerEventHandler(object sender, string[] Message, LoggerKind LogType);

    public enum LoggerKind
    {
        Info,
        Error,
        Warning,
        Text,
        File
    }
}