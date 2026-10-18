//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace NDMicroCpu.Core.Common
{
    /// <summary>
    /// This class contains the logic for Debug logging from the different modules
    /// </summary>
    public static class Logger
    {

        /// <summary>
        /// Receives one log message when a destination is set to Event.
        /// </summary>
        /// <param name="message">
        /// The message with its level, address and time.
        /// </param>
        public delegate void LogMessage(CoreLogMessage message);

        /// <summary>
        /// Hook this to collect logs from source code debug-logs
        /// </summary>
        public static event LogMessage? OnLogDebugMessage;

        /// <summary>
        /// Hook this to collect logs from Debugger
        /// </summary>
        public static event LogMessage? OnLogDebugger;


        /// <summary>
        /// Log level for DEBUG messages
        /// </summary>
        public enum LogLevel
        {
            /// <summary>
            /// Logs that contain execution code from the running CPU. This is the lowest level of log we can get to.
            /// These messages are disabled by default and should never be enabled in a production environment.
            /// </summary>
            Opcodes,

            /// <summary>
            /// Logs that contain certain CPU activities
            /// </summary>
            CPU,

            /// <summary>
            /// Logs that contain execution code from the HW Devices
            /// These messages are disabled by default and should never be enabled in a production environment.
            /// </summary>
            Device,

            /// <summary>
            /// Logs that contain execution code from the Machine
            /// These messages are disabled by default and should never be enabled in a production environment.
            /// </summary>
            Machine,

            /// <summary>
            /// Logs that contain the most detailed messages.
            /// These messages are disabled by default and should never be enabled in a production environment.
            /// </summary>
            Trace,

            /// <summary>
            /// Logs that are used for interactive investigation during development.
            /// These logs should primarily contain information useful for debugging and have no long-term value.
            /// </summary>
            Debug,

            /// <summary>
            /// Logs that track the general flow of the application.
            /// These logs should have long-term value.
            /// </summary>
            Information,

            /// <summary>
            /// Logs that highlight an abnormal or unexpected event in the application flow, but do not otherwise cause the application execution to stop.
            /// </summary>
            Warning,

            /// <summary>
            /// Logs that highlight an abnormal or unexpected event in the application flow, but do not otherwise cause the application execution to stop.
            /// </summary>
            Error,

            /// <summary>
            /// Logs that describe an unrecoverable application or system crash, or a catastrophic failure that requires immediate attention.
            /// </summary>
            Fatal,
        }

        /// <summary>
        /// Where log output goes.
        /// </summary>
        public enum LogDestination
        {
            /// <summary>
            /// Nothing is written.
            /// </summary>
            Disabled,
            /// <summary>
            /// The process console.
            /// </summary>
            Console,
            /// <summary>
            /// The .NET trace listeners (DebugView on Windows).
            /// </summary>
            Trace,
            /// <summary>
            /// The OnLogDebugMessage and OnLogDebugger events, for a user interface to collect.
            /// </summary>
            Event,
            /// <summary>
            /// A file-trace.txt file under the local application data folder.
            /// </summary>
            File
        }

        /// <summary>
        /// Output destination for Debug-Logs from the source code
        /// </summary>
        internal static LogDestination Destination { get; set; } = LogDestination.Disabled;

        /// <summary>
        /// Output destination for debugger
        /// </summary>
        internal static LogDestination DbgDestination { get; set; } = LogDestination.Console;

        internal static LogLevel Level { get; set; } = LogLevel.Debug;

        internal static LogLevel DbgLevel { get; set; } = LogLevel.Information;

        /// <summary>
        /// Sets where debugger output goes.
        /// </summary>
        /// <param name="dst">
        /// The destination.
        /// </param>
        public static void SetDebuggerDestination(LogDestination dst)
        {
            DbgDestination = dst;
        }

        /// <summary>
        /// Where debugger output goes.
        /// </summary>
        /// <returns>
        /// The destination.
        /// </returns>
        public static LogDestination GetDebuggerDestination()
        {
            return DbgDestination;
        }


        /// <summary>
        /// Sets where debug log output goes.
        /// </summary>
        /// <param name="dst">
        /// The destination.
        /// </param>
        public static void SetDestination(LogDestination dst)
        {
            Destination = dst;
        }
        /// <summary>
        /// Where debug log output goes.
        /// </summary>
        /// <returns>
        /// The destination.
        /// </returns>
        public static LogDestination GetDestination()
        {
            return Destination;
        }

        /// <summary>
        /// Sets the lowest level of debugger output that is written.
        /// </summary>
        /// <param name="lvl">
        /// Messages below this level are dropped.
        /// </param>
        public static void SetDebuggerLevel(LogLevel lvl)
        {
            DbgLevel = lvl;
        }

        /// <summary>
        /// Sets the lowest level of debug log output that is written.
        /// </summary>
        /// <param name="lvl">
        /// Messages below this level are dropped.
        /// </param>
        public static void SetLogLevel(LogLevel lvl)
        {
            Level = lvl;
        }

        /// <summary>
        /// The lowest level of debug log output that is written.
        /// </summary>
        /// <returns>
        /// The level.
        /// </returns>
        public static LogLevel GetLogLevel()
        {
            return Level;
        }


        /// <summary>
        /// Sets the level and destination of debug log output in one call.
        /// </summary>
        /// <param name="lvl">
        /// Messages below this level are dropped.
        /// </param>
        /// <param name="dst">
        /// The destination.
        /// </param>
        public static void EnableLogger(LogLevel lvl = LogLevel.Warning, LogDestination dst = LogDestination.Trace)
        {
            Level = lvl;
            Destination = dst;
        }
        /// <summary>
        /// Sets the level and destination of debugger output in one call.
        /// </summary>
        /// <param name="lvl">
        /// Messages below this level are dropped.
        /// </param>
        /// <param name="dst">
        /// The destination.
        /// </param>
        public static void EnableDebugLogger(LogLevel lvl = LogLevel.Debug, LogDestination dst = LogDestination.Console)
        {
            DbgLevel = lvl;
            DbgDestination = dst;
        }

        /// <summary>
        /// Writes a debug log message without a line ending, for output built up in pieces.
        /// </summary>
        /// <param name="message">
        /// The text.
        /// </param>
        /// <param name="level">
        /// The level of the message.
        /// </param>
        public static void LogC(string message, LogLevel level)
        {
            Log(message, level, noLineEnding: true);
        }

        /// <summary>
        /// Writes a debug log message when its level is at or above the configured level.
        /// </summary>
        /// <param name="message">
        /// The text.
        /// </param>
        /// <param name="level">
        /// The level of the message.
        /// </param>
        /// <param name="address">
        /// The memory address the message is about, or 0; shown when the message goes through an event.
        /// </param>
        /// <param name="noLineEnding">
        /// True writes the text without a line ending, for output built up in pieces.
        /// </param>
        public static void Log(string message, LogLevel level, uint address = 0x00, bool noLineEnding = false)
        {

            if ((int)level < (int)Level) return; // Dont log if not wanted

            switch (Destination)
            {
                case LogDestination.Disabled:
                    break;
                case LogDestination.Console:
                    if (noLineEnding)
                        Console.Write(message);
                    else
                        Console.WriteLine(message);
                    break;

                case LogDestination.Trace:
                    // View Trace using DebugView - https://docs.microsoft.com/en-us/sysinternals/downloads/debugview
                    if (noLineEnding)
                        Trace.Write(message);
                    else
                        Trace.WriteLine(message);
                    break;

                case LogDestination.Event:
                    if (OnLogDebugMessage != null)
                    {
                        CoreLogMessage msg = new CoreLogMessage(message, level, address);
                        msg.noLineEnding = noLineEnding;
                        OnLogDebugMessage(msg);
                    }
                    break;
                case LogDestination.File:
                    FileLog($"{level}; {message}");
                    break;

                default:
                    break;
            }
        }

        static string? logFilName;
        static FileStream? logFile;
        private static void FileLog(string message)
        {
            if (logFilName == null)
            {
                var folder = Environment.SpecialFolder.LocalApplicationData;
                var path = Environment.GetFolderPath(folder);

                path = path + "\\trace";
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                logFilName = path + "\\file-trace.txt";
            }

            if (logFile == null)
            {
                logFile = new FileStream(logFilName, FileMode.Append);
            }


            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(message + "\r\n");
            logFile.Write(bytes);
            logFile.Flush();

        }
        /// <summary>
        /// Writes one character of debugger output without a line ending.
        /// </summary>
        /// <param name="c">
        /// The character.
        /// </param>
        public static void DbgC(char c)
        {

            Dbg($"{c}", noLineEnding: true);
        }

        /// <summary>
        /// Writes debugger output without a line ending.
        /// </summary>
        /// <param name="message">
        /// The text.
        /// </param>
        public static void DbgC(string message)
        {
            Dbg(message, noLineEnding: true);

        }
        /// <summary>
        /// Writes a message from the debugger interface. Normally this goes to the console; a
        /// remote debugger or a user interface routes it through the OnLogDebugger event instead.
        /// </summary>
        /// <param name="message">
        /// The text.
        /// </param>
        /// <param name="lvl">
        /// The level of the message; dropped when below the debugger level.
        /// </param>
        /// <param name="noLineEnding">
        /// True writes the text without a line ending, for output built up in pieces.
        /// </param>
        public static void Dbg(string message = "", LogLevel lvl = LogLevel.Information, bool noLineEnding = false)
        {
            if ((int)lvl < (int)DbgLevel) return; // Dont log if not wanted

            switch (DbgDestination)
            {
                case LogDestination.Disabled:
                    break;

                case LogDestination.Console:
                    if (noLineEnding)
                        Console.Write(message);
                    else
                        Console.WriteLine(message);
                    break;

                case LogDestination.Trace:
                    if (noLineEnding)
                        Trace.Write(message);
                    else
                        Trace.WriteLine(message);
                    break;
                case LogDestination.Event:
                    if (OnLogDebugger != null)
                    {
                        CoreLogMessage msg = new CoreLogMessage(message, lvl);
                        msg.noLineEnding = noLineEnding;
                        OnLogDebugger(msg);
                    }
                    break;
                default:
                    break;
            }

            // If DebugTrace == File, also send console text to File
            if (Destination == LogDestination.File)
            {
                FileLog($"Console; {message}");
            }

        }


        /// <summary>
        /// Whether a message at this level would be written at all, so callers can skip building expensive text.
        /// </summary>
        /// <param name="level">
        /// The level to check.
        /// </param>
        /// <returns>
        /// True when the destination is enabled and the level is high enough.
        /// </returns>
        public static bool IsLogEnabled(LogLevel level)
        {
            if (Destination == LogDestination.Disabled) return false;
            if ((int)level < (int)Level) return false;
            return true;
        }


        /// <summary>
        /// This class contains the complete debug-message. 
        /// Used when logs are sent through an Event, instad of Debug.Trace/Logger.Dbg
        /// </summary>
        public class CoreLogMessage
        {

            /// <summary>
            /// The message to be logged
            /// </summary>
            public string LogMessage { get; set; }

            /// <summary>
            /// What level is the message at 
            /// </summary>
            public LogLevel LogLevel { get; set; }


            /// <summary>
            /// Memory address, used by code logging to show memory address
            /// </summary>
            public uint Address { get; set; }

            /// <summary>
            /// DateTime when the message is generated
            /// </summary>
            public DateTime LogTimeStamp { get; set; }

            /// <summary>
            /// Set to true if the message has been handled, and such no other handlers should be needed
            /// </summary>
            public bool Handled { get; set; }

            /// <summary>
            /// Set to true if this is a WRITE not a WRITELINE (aka, dont write a new CRLF, wait for the next message)
            /// </summary>
            public bool noLineEnding { get; internal set; }

            /// <summary>
            /// Creates a message stamped with the current time.
            /// </summary>
            /// <param name="message">
            /// The text.
            /// </param>
            /// <param name="level">
            /// The level of the message.
            /// </param>
            /// <param name="address">
            /// The memory address the message is about, or 0.
            /// </param>
            public CoreLogMessage(string message, LogLevel level = LogLevel.Debug, uint address = 0x00)
            {
                LogMessage = message;
                LogLevel = level;
                Address = address;
                LogTimeStamp = DateTime.Now;
            }

            /// <summary>
            /// The message with its level, address and time, on one line.
            /// </summary>
            /// <returns>
            /// The formatted line.
            /// </returns>
            public override string ToString()
            {
                if (Address != 0x00)
                {
                    return $"{LogMessage}  - {LogLevel}  @{Numeric.Num2Str(Address)}   #{LogTimeStamp.ToLongTimeString()}";
                }
                else
                {
                    return $"{LogMessage}  - {LogLevel}  #{LogTimeStamp.ToLongTimeString()}";
                }
            }
        }

    }
}
