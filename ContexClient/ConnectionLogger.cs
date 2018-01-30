using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//Replace these loggers with Log4Net

namespace ConnectionEngine
{
    public static class Logger
    {
        private static readonly string LogFolder;
        private static readonly string LogFile;
        private static readonly StreamWriter LogWriter;
        private static string PID = Process.GetCurrentProcess().Id.ToString();
        private static ConcurrentQueue<string> LogQueue = new ConcurrentQueue<string>();
        public static event EventHandler<string> LogMessageOccurred;

        static Logger()
        {
            DateTime date = DateTime.Now;
            string dateString = date.ToString("yy-MM-dd_HHmmss");
            LogFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Apex");
            LogFile = Path.Combine(LogFolder, "ConnectionLog_" + dateString + ".txt");
            if (!Directory.Exists(LogFolder))
                Directory.CreateDirectory(LogFolder);
            LogWriter = new StreamWriter(LogFile);
            LogWriter.AutoFlush = true;

            Task.Run(async () =>
            {
                while (true)
                {
                    string log;
                    if (LogQueue.TryDequeue(out log))
                        LogWriter.WriteLine(log);
                    else
                        await Task.Delay(1000);
                }
            });
        }

        public static void Log(string message)
        {
            var eHandler = LogMessageOccurred;
            if (eHandler != null)
            {
                eHandler(PID, message);
            }
            LogQueue.Enqueue(DateTime.Now.ToString("HH:mm:ss:fff") + "-" + PID + "-" + message);
        }
    }
}
