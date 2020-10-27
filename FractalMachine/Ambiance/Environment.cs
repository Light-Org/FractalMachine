﻿using FractalMachine.Code;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace FractalMachine.Ambiance
{
    public abstract class Environment
    {
        #region Static

        static Environment current;
        public static Environment GetEnvironment
        {
            get
            {
                if (current == null)
                {
                    var Platform = System.Environment.OSVersion.Platform;

                    if (Platform == PlatformID.Win32NT)
                        current = new Environments.Windows();
                    else
                        current = new Environments.Unix();
                }

                return current;
            }
        }

        #endregion

        public PlatformID Platform;
        public string ContextPath = "";
        public Repository Repository;
        public string Arch;

        public Environment()
        {
            //Arch = ExecCmd("arch").Split('\n')[0];
            //Repository.Update();
        }
    
        #region Projections

        public string AssertPath(string Path)
        {
            if (Repository == null) return Path;
            return Repository.AssertPath(Path);
        }

        #endregion

        /* 4 LINUX
         * Set temporary dynamic linking dir: https://unix.stackexchange.com/questions/24811/changing-linked-library-for-a-given-executable-centos-6
         */

        public Command NewCommand(string command = "")
        {
            var cmd = new Command(this);
            cmd.Cmd = command;
            return cmd;
        }

        public string ExecCmd(string cmd)
        {
            var comm = NewCommand(cmd);
            comm.Run();

            string res = "";
            foreach (string s in comm.OutLines) res += s + "\n";
            foreach (string s in comm.OutErrors) res += "ERR! " + s + "\n";
            return res;
        }
    }

    public class Command
    {
        /// <summary>
        /// Revelant in case of DirectCall == false
        /// </summary>
        public bool UseStdWrapper = true;
        public bool DirectCall = false;
        public Process Process;
        public Environment Environment;
        public string[] OutLines, OutErrors;

        public string arguments = "";
        public string Cmd = "";

        public Command(Environment environment)
        {
            Environment = environment;
        }

        void createProcess()
        {
            string call = "";
            string args = "";

            if (DirectCall)
            {
                var splitCmd = Cmd.Split(' ');

                call = Environment.ContextPath + "/bin/" + splitCmd[0];

                for (int c = 1; c < splitCmd.Length; c++)
                    args += splitCmd[c] + ' ';
                args += arguments + ' ';
            }
            else
            {
                call = Environment.ContextPath+"/bin/bash";
                args = $"-login -c '" + Cmd + " " + arguments;
                if (UseStdWrapper) args += " > /home/out.txt 2>/home/err.txt"; // 2>&1 | tee out.txt
                args += "'";
            }

            Process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = call,
                    Arguments = args,             
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                }
            };
        }

        public void Run()
        {
            createProcess();

            Process.Start();
            bool peak = false;
            int startModules = Process.Modules.Count, currentModules = 0, ticks = 0;

            while (!Process.HasExited && !(peak && currentModules <= startModules))
            {
                currentModules = Process.Modules.Count;
                if (currentModules > startModules) peak = true;
                Thread.Sleep(10);
                if (ticks++ > 100) Process.Start();
            }            

            Process.StandardInput.Flush();
            Process.StandardInput.Close();

            OutLines = OutErrors = new string[0];
            DateTime endProcess = DateTime.Now;

            if (UseStdWrapper && !DirectCall)
            {
                // Load file errors
                var fnOut = Environment.ContextPath + "/home/out.txt";
                var fnErr = Environment.ContextPath + "/home/err.txt";

                // Yes, this is a little ugly
                while (!Resources.IsFileReady(fnOut) || !Resources.IsFileReady(fnErr))
                {
                    if (DateTime.Now.Subtract(endProcess).TotalSeconds > 0)
                        break;

                    Thread.Sleep(10);
                }

                if(Resources.IsFileReady(fnOut))
                    OutLines = File.ReadAllLines(fnOut);

                if(Resources.IsFileReady(fnErr))
                    OutErrors = File.ReadAllLines(fnErr);
            }
            else
            {
                var taskStdOut = Process.StandardOutput.ReadToEndAsync();
                var taskStdErr = Process.StandardOutput.ReadToEndAsync();

                while(!taskStdOut.IsCompleted || !taskStdErr.IsCompleted)
                {
                    if (DateTime.Now.Subtract(endProcess).TotalSeconds > 0)
                        break;

                    Thread.Sleep(10);
                }

                if (taskStdOut.IsCompleted)
                    OutLines = taskStdOut.Result.Split("\n");

                if (taskStdErr.IsCompleted)
                    OutErrors = taskStdErr.Result.Split("\n");
            }
        }

        public void AddArgument(string arg, string ass = null)
        {
            if (ass == null)
            {
                arguments += " " + arg;
            }
            else
            {
                arguments += " " + arg + " " + ass;
            }
        }
    }
}
