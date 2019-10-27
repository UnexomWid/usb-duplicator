/**
 * USB Duplicator (https://github.com/UnexomWid/usb-duplicator)
 *
 * This project is licensed under the MIT license.
 * Copyright (c) 2019 UnexomWid (https://uw.exom.dev)
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
 * associated documentation files (the "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
 * following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or substantial
 * portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
 * LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
 * IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
 * SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

#if !SILENT
#define CONSOLE
#endif

using System;
using System.IO;
using System.Management;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace USBDuplicator
{
    class USBDuplicator
    {
        #if CONSOLE
        [DllImport("kernel32.dll",
                    EntryPoint = "AllocConsole",
                    SetLastError = true,
                    CharSet = CharSet.Auto,
                    CallingConvention = CallingConvention.StdCall)]
        private static extern int AllocConsole();
        #endif

        [Conditional("CONSOLE")]
        private static void Log(string text)
        {
            #if CONSOLE
            Console.WriteLine(text);
            #endif
        }

        /// <summary>
        /// The working directory where to duplicate.
        /// </summary>
        private static string WorkingDirectory = "";

        /// <summary>
        /// Whether or not to only duplicate marked drives.
        /// </summary>
        private static bool Whitelist = false;

        /// <summary>
        /// Whether or not to only duplicate unmarked drives.
        /// </summary>
        private static bool Blacklist = false;

        /// <summary>
        /// An integer used to minimize the chances of 2 drives being duplicated in the same directory.
        /// </summary>
        private static Int64 ID = 0;

        /// <summary>
        /// An object used to safely modify the ID.
        /// </summary>
        private static readonly object IDLock = new object();


        [STAThread]
        static void Main(string[] args)
        {
            #if CONSOLE
            AllocConsole();
            #endif

            Log("Starting up...");
            ParseArgs(args);

            Log("Starting management event watcher...");

            ManagementEventWatcher watcher = new ManagementEventWatcher();
            WqlEventQuery query = new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2");
            watcher.EventArrived += new EventArrivedEventHandler(Watcher_EventArrived);
            watcher.Query = query;
            watcher.Start();

            Log("Initialization complete\r\n-----------------------------------------\r\n\r\nListening for drive activity...");

            while (true)
                watcher.WaitForNextEvent();
        }

        private static void Watcher_EventArrived(object sender, EventArrivedEventArgs e)
        {
            Log("\r\nUSB Drive detected!\r\nIdentifying drive...");

            string source = e.NewEvent.Properties["DriveName"].Value.ToString();
            bool marked = File.Exists(source + "\\usbd");

            string driveName = new DriveInfo(source).VolumeLabel;
            if (string.IsNullOrEmpty(driveName))
                driveName = "USB Drive";

            Log(string.Format("\r\nLetter - {0}\r\nLabel - {1}\n\r",
                e.NewEvent.Properties["DriveName"].Value.ToString(),
                driveName));

            if ((Whitelist && !marked))
            {
                Log("Ignoring drive, as it is not whitelisted.");
                return;
            }
            if(Blacklist && marked)
            {
                Log("Ignoring drive, as it is blacklisted.");
                return;
            }

            string destination;
            lock (IDLock)
            {
                destination = string.Format("{0}/[{1}_{2}-{3}-{4}_{5}-{6}-{7}] {8}",
                              WorkingDirectory,
                              (ID++),
                              DateTime.Now.Day,
                              DateTime.Now.Month,
                              DateTime.Now.Year,
                              DateTime.Now.Hour,
                              DateTime.Now.Minute,
                              DateTime.Now.Second,
                              DateTime.Now.Millisecond,
                              driveName);
            }

            // You can replace File.Copy with File.Move, or another method that accepts 2 string parameters (the source and destination).
            ProcessFiles(source + "\\", "*", File.Copy, destination);

            Log("\r\nSuccessfully snatched " + driveName + "");
        }

        /// <summary>
        /// Applies an action for every file in a directory that matches a filter.
        /// </summary>
        /// <param name="directory">The directory where to search for files.</param>
        /// <param name="filter">The filename filter.</param>
        /// <param name="action">The action to apply for every file that matches the filter.</param>
        /// <param name="localWorkingDirectory">The local working directory where to apply the action for the file.</param>
        public static void ProcessFiles(string directory, string filter, Action<string, string> action, string localWorkingDirectory)
        {
            if (!Directory.Exists(localWorkingDirectory))
                Directory.CreateDirectory(localWorkingDirectory);

            foreach (string file in Directory.EnumerateFiles(directory, filter, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    action(file, localWorkingDirectory + "/" + Path.GetFileName(file));
                    Log("Snatched " + file);
                }
                catch { }
            }
            foreach (string subDir in Directory.EnumerateDirectories(directory))
            {
                try { ProcessFiles(subDir, filter, action, localWorkingDirectory + "/" + Path.GetFileName(subDir)); }
                catch { }
            }
        }

        /// <summary>
        /// Parses the command line arguments.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        private static void ParseArgs(string[] args)
        {
            foreach (string arg in args)
            {
                string lower = arg.ToLower();
                switch (lower)
                {
                    case "-h":
                    case "--help":
                        Log("USB Duplicator (USBD)\n" +
                                          "============================\n" +
                                          "[Description] Copies all data from USB drives to a folder, when they are plugged in\n\n" +
                                          "[Arguments]:\n" +
                                          "\"WORKING_DIRECTORY\"\n" +
                                          "    *sets the working directory to WORKING_DIRECTORY\n" +
                                          "-w, --whitelist\n" +
                                          "    *switches to the whitelist mode, copies only marked drives\n" +
                                          "-b, --blacklist\n" +
                                          "    *switches to the blacklist mode, copies only unmarked drives\n\n" +
                                          "[Usage]\n" +
                                          "usbd.exe\n" +
                                          "    *copies data from all USB drives to the current directory, when they are plugged in\n" +
                                          "usbd.exe \"WORKING_DIRECTORY\"\n" +
                                          "    *copies data from all USB drives to the CURRENT_DIRECTORY directory, when they are plugged in\n" +
                                          "usbd.exe -w\n" +
                                          "    *copies data from marked USB drives to the current directory, when they are plugged in\n" +
                                          "usbd.exe -b\n" +
                                          "    *copies data from unmarked USB drives to the current directory, when they are plugged in\n" +
                                          "usbd.exe -w \"WORKING_DIRECTORY\", usbd.exe \"WORKING_DIRECTORY\" -w\n" +
                                          "    *copies data from marked USB drives to the CURRENT_DIRECTORY directory, when they are plugged in\n" +
                                          "usbd.exe -b \"WORKING_DIRECTORY\", usbd.exe \"WORKING_DIRECTORY\" -b\n" +
                                          "    *copies data from unmarked USB drives to the CURRENT_DIRECTORY directory, when they are plugged in\n");
                        Environment.Exit(0);
                        break;
                    case "-w":
                    case "--whitelist":
                        Whitelist = true;
                        Blacklist = false;
                        Log("Switched to whitelist mode");
                        break;
                    case "-b":
                    case "--blacklist":
                        Whitelist = false;
                        Blacklist = true;
                        Log("Switched to blacklist mode");
                        break;
                    default:
                        if (WorkingDirectory.Length == 0)
                        {
                            WorkingDirectory = arg;
                            Log(string.Format("Changed working directory to {0}", arg));

                            if (!Directory.Exists(WorkingDirectory))
                            {
                                try
                                {
                                    Log("Creating working directory...");
                                    Directory.CreateDirectory(WorkingDirectory);
                                }
                                catch
                                {
                                    Log("Could not create working directory");
                                    WorkingDirectory = Directory.GetCurrentDirectory();
                                    Log(string.Format("Changed working directory to {0}", WorkingDirectory));
                                }
                            }
                        }
                        break;
                }
            }

            if(WorkingDirectory.Length == 0)
                WorkingDirectory = Directory.GetCurrentDirectory();
        }
    }
}
