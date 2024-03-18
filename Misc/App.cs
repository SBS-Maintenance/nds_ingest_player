
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;

using static mpvnet.Global;

namespace mpvnet
{
    public static class App
    {
        public static List<string> TempFiles { get; } = new List<string>();

        public static string StartSize { get; set; } = "height-session";
        public static string ConfPath { get => Core.ConfigFolder + "mpvnet.conf"; }
        public static string ProcessInstance { get; set; } = "single";
        public static bool AutoPlay { get; set; }
        public static bool Exit { get; set; }
        public static bool MediaInfo { get; set; } = true;
        public static bool Queue { get; set; }

        public static void Init()
        {
            var useless1 = Core.ConfigFolder;
            var useless2 = Core.Conf;

        }


        public static void RunTask(Action action)
        {
            Task.Run(() => {
                try {
                    action.Invoke();
                }
                catch (Exception e) {
                    Console.WriteLine(e);
                }
            });
        }

        public static string Version => "Copyright (C) 2000-2022 mpv.net/mpv/mplayer\n" +
            $"mpv.net {Application.ProductVersion}" + GetLastWriteTime(Application.ExecutablePath) + "\n" +
            $"{Core.GetPropertyString("mpv-version")}" + GetLastWriteTime(Folder.Startup + "libmpv-2.dll") + "\n" +
            $"ffmpeg {Core.GetPropertyString("ffmpeg-version")}\n" +
            $"MediaInfo {FileVersionInfo.GetVersionInfo(Path.Combine(Application.StartupPath, "MediaInfo.dll")).FileVersion}" +
            GetLastWriteTime(Path.Combine(Application.StartupPath , "MediaInfo.dll")) + "\nGPL v2 License";

        static string GetLastWriteTime(string path)
        {
            if (IsStoreVrsion)
                return "";

            return $" ({File.GetLastWriteTime(path).ToShortDateString()})";
        }

        static bool IsStoreVrsion => Application.StartupPath.Contains("FrankSkare.mpv.net");


    }
}
