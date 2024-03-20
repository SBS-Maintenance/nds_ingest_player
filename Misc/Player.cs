
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

using static libmpv;
using static mpvnet.Global;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace mpvnet
{
    public class CorePlayer
    {
        public static string[] VideoTypes { get; set; } = "mkv mp4 avi mov flv mpg webm wmv ts vob 264 265 asf avc avs dav h264 h265 hevc m2t m2ts m2v m4v mpeg mpv mts vpy y4m".Split(' ');
        public static string[] AudioTypes { get; set; } = "mp3 flac m4a mka mp2 ogg opus aac ac3 dts dtshd dtshr dtsma eac3 mpa mpc thd w64 wav".Split(' ');
        public static string[] ImageTypes { get; set; } = { "jpg", "bmp", "png", "gif", "webp" };
        public static string[] SubtitleTypes { get; } = { "srt", "ass", "idx", "sub", "sup", "ttxt", "txt", "ssa", "smi", "mks" };

        public event Action<mpv_log_level, string> LogMessageAsync; // log-message        MPV_EVENT_LOG_MESSAGE
        public event Action<mpv_end_file_reason> EndFileAsync;      // end-file           MPV_EVENT_END_FILE
        public event Action<string[]> ClientMessageAsync;           // client-message     MPV_EVENT_CLIENT_MESSAGE
        public event Action GetPropertyReplyAsync;                  // get-property-reply MPV_EVENT_GET_PROPERTY_REPLY
        public event Action SetPropertyReplyAsync;                  // set-property-reply MPV_EVENT_SET_PROPERTY_REPLY
        public event Action CommandReplyAsync;                      // command-reply      MPV_EVENT_COMMAND_REPLY
        public event Action StartFileAsync;                         // start-file         MPV_EVENT_START_FILE
        public event Action FileLoadedAsync;                        // file-loaded        MPV_EVENT_FILE_LOADED
        public event Action VideoReconfigAsync;                     // video-reconfig     MPV_EVENT_VIDEO_RECONFIG
        public event Action AudioReconfigAsync;                     // audio-reconfig     MPV_EVENT_AUDIO_RECONFIG
        public event Action SeekAsync;                              // seek               MPV_EVENT_SEEK
        public event Action PlaybackRestartAsync;                   // playback-restart   MPV_EVENT_PLAYBACK_RESTART

        public event Action<mpv_log_level, string> LogMessage; // log-message        MPV_EVENT_LOG_MESSAGE
        public event Action<mpv_end_file_reason> EndFile;     // end-file           MPV_EVENT_END_FILE
        public event Action<string[]> ClientMessage;          // client-message     MPV_EVENT_CLIENT_MESSAGE
        public event Action Shutdown;                         // shutdown           MPV_EVENT_SHUTDOWN
        public event Action GetPropertyReply;                 // get-property-reply MPV_EVENT_GET_PROPERTY_REPLY
        public event Action SetPropertyReply;                 // set-property-reply MPV_EVENT_SET_PROPERTY_REPLY
        public event Action CommandReply;                     // command-reply      MPV_EVENT_COMMAND_REPLY
        public event Action StartFile;                        // start-file         MPV_EVENT_START_FILE
        public event Action FileLoaded;                       // file-loaded        MPV_EVENT_FILE_LOADED
        public event Action VideoReconfig;                    // video-reconfig     MPV_EVENT_VIDEO_RECONFIG
        public event Action AudioReconfig;                    // audio-reconfig     MPV_EVENT_AUDIO_RECONFIG
        public event Action Seek;                             // seek               MPV_EVENT_SEEK
        public event Action PlaybackRestart;                  // playback-restart   MPV_EVENT_PLAYBACK_RESTART

        public event Action Initialized;
        public event Action InitializedAsync;
        public event Action Pause;
        public event Action ShowMenu;
        public event Action<double> WindowScaleMpv;
        public event Action<float> ScaleWindow;
        public event Action<float> WindowScaleNET;
        public event Action<int> PlaylistPosChanged;
        public event Action<int> PlaylistPosChangedAsync;
        public event Action<Size> VideoSizeChanged;
        public event Action<Size> VideoSizeChangedAsync;
        public event Action<string> MoveWindow;

        public Dictionary<string, List<Action>> PropChangeActions { get; set; } = new Dictionary<string, List<Action>>();
        public Dictionary<string, List<Action<int>>> IntPropChangeActions { get; set; } = new Dictionary<string, List<Action<int>>>();
        public Dictionary<string, List<Action<bool>>> BoolPropChangeActions { get; set; } = new Dictionary<string, List<Action<bool>>>();
        public Dictionary<string, List<Action<double>>> DoublePropChangeActions { get; set; } = new Dictionary<string, List<Action<double>>>();
        public Dictionary<string, List<Action<string>>> StringPropChangeActions { get; set; } = new Dictionary<string, List<Action<string>>>();

        public AutoResetEvent ShutdownAutoResetEvent { get; } = new AutoResetEvent(false);
        public AutoResetEvent VideoSizeAutoResetEvent { get; } = new AutoResetEvent(false);
        public DateTime HistoryTime;
        public IntPtr Handle { get; set; }
        public IntPtr NamedHandle { get; set; }
        public List<TimeSpan> BluRayTitles { get; } = new List<TimeSpan>();
        public object MediaTracksLock { get; } = new object();
        public Size VideoSize { get; set; }
        public TimeSpan Duration;

        public string ConfPath { get => ConfigFolder + "mpv.conf"; }
        public string GPUAPI { get; set; } = "auto";
        public string InputConfPath => ConfigFolder + "input.conf";
        public string Path { get; set; } = "";
        public string VO { get; set; } = "gpu";

        public string VID { get; set; } = "";
        public string AID { get; set; } = "";
        public string SID { get; set; } = "";

        public bool Border { get; set; } = true;
        public bool FileEnded { get; set; }
        public bool Fullscreen { get; set; }
        public bool IsQuitNeeded { set; get; } = true;
        public bool KeepaspectWindow { get; set; }
        public bool Paused { get; set; }
        public bool Shown { get; set; }
        public bool SnapWindow { get; set; }
        public bool TaskbarProgress { get; set; } = true;
        public bool WasInitialSizeSet;
        public bool WindowMaximized { get; set; }
        public bool WindowMinimized { get; set; }

        public int Edition { get; set; }
        public int PlaylistPos { get; set; } = -1;
        public int Screen { get; set; } = -1;
        public int VideoRotate { get; set; }

        public float Autofit { get; set; } = 0.6f;
        public float AutofitSmaller { get; set; } = 0.3f;
        public float AutofitLarger { get; set; } = 0.8f;

        public String path = "";
        public int duration = 0;

        public void Init(IntPtr handle)
        {
            Handle = mpv_create();

            var events = Enum.GetValues(typeof(mpv_event_id)).Cast<mpv_event_id>();

            foreach (mpv_event_id i in events)
                mpv_request_event(Handle, i, 0);

            mpv_request_log_messages(Handle, "no");

            App.RunTask(() => MainEventLoop());

            if (Handle == IntPtr.Zero)
                throw new Exception("error mpv_create");

            SetPropertyInt("osd-duration", 2000);
            SetPropertyLong("wid", handle.ToInt64());

            //SetPropertyBool("input-default-bindings", true);
            SetPropertyBool("input-builtin-bindings", false);

            SetPropertyString("watch-later-options", "mute");
            SetPropertyString("screenshot-directory", "~~desktop/");
            SetPropertyString("osd-playing-msg", "${media-title}");
            SetPropertyString("osc", "yes");
            SetPropertyString("force-window", "yes");
            SetPropertyString("config-dir", ConfigFolder);
            SetPropertyString("config", "yes");
            SetPropertyBool("loop", false);
            SetPropertyInt("loop-playlist", 1);
            SetPropertyString("vo", "gpu");
            SetPropertyString("keep-open", "yes");
            //SetPropertyBool("deinterlace", true);

            ProcessCommandLine(true);

            Environment.SetEnvironmentVariable("MPVNET_VERSION", Application.ProductVersion);

            mpv_error err = mpv_initialize(Handle);

            if (err < 0)
                throw new Exception("mpv_initialize error" + BR2 + GetError(err) + BR);

            string idle = GetPropertyString("idle");
            App.Exit = idle == "no" || idle == "once";

            NamedHandle = mpv_create_client(Handle, "mpvnet");

            if (NamedHandle == IntPtr.Zero)
                throw new Exception("mpv_create_client error");

            mpv_request_log_messages(NamedHandle, "terminal-default");

            App.RunTask(() => EventLoop());

            // otherwise shutdown is raised before media files are loaded,
            // this means Lua scripts that use idle might not work correctly
            SetPropertyString("idle", "yes");

            ObservePropertyString("path", value => {
                if (HistoryTime == DateTime.MinValue)
                {
                    HistoryTime = DateTime.Now;
                    HistoryPath = value;
                }
                Path = value;
            });


            ObservePropertyInt("video-rotate", value => {
                VideoRotate = value;
                UpdateVideoSize("dwidth", "dheight");
            });

            ObservePropertyInt("playlist-pos", value => {
                PlaylistPos = value;
                InvokeEvent(PlaylistPosChanged, PlaylistPosChangedAsync, value);

                if (FileEnded && value == -1)
                {
                    if (GetPropertyString("keep-open") == "no" && App.Exit)
                        Core.CommandV("quit");
                }
            });

            Initialized?.Invoke();
            InvokeAsync(InitializedAsync);
        }

        public void Destroy()
        {
            mpv_destroy(Handle);
            mpv_destroy(NamedHandle);
        }

        public void ProcessProperty(string name, string value)
        {
            switch (name)
            {
                case "autofit":
                    {
                        if (int.TryParse(value.Trim('%'), out int result))
                            Autofit = result / 100f;
                    }
                    break;
                case "autofit-smaller":
                    {
                        if (int.TryParse(value.Trim('%'), out int result))
                            AutofitSmaller = result / 100f;
                    }
                    break;
                case "autofit-larger":
                    {
                        if (int.TryParse(value.Trim('%'), out int result))
                            AutofitLarger = result / 100f;
                    }
                    break;
                case "border": Border = value == "yes"; break;
                case "fs":
                case "fullscreen": Fullscreen = value == "yes"; break;
                case "gpu-api": GPUAPI = value; break;
                case "keepaspect-window": KeepaspectWindow = value == "yes"; break;
                case "screen": Screen = Convert.ToInt32(value); break;
                case "snap-window": SnapWindow = value == "yes"; break;
                case "taskbar-progress": TaskbarProgress = value == "yes"; break;
                case "vo": VO = value; break;
                case "window-maximized": WindowMaximized = value == "yes"; break;
                case "window-minimized": WindowMinimized = value == "yes"; break;
            }

            if (AutofitLarger > 1)
                AutofitLarger = 1;
        }

        bool? _UseNewMsgModel;

        public bool UseNewMsgModel
        {
            get
            {
                if (!_UseNewMsgModel.HasValue)
                    _UseNewMsgModel = InputConfContent.Contains("script-message-to mpvnet");
                return _UseNewMsgModel.Value;
            }
        }

        string _InputConfContent;

        public string InputConfContent
        {
            get
            {
                if (_InputConfContent == null)
                    _InputConfContent = File.ReadAllText(Core.InputConfPath);
                return _InputConfContent;
            }
        }

        string _ConfigFolder;

        public string ConfigFolder
        {
            get
            {
                if (_ConfigFolder == null)
                {
                    _ConfigFolder = Folder.Startup + "portable_config";

                    if (!Directory.Exists(_ConfigFolder))
                        _ConfigFolder = Folder.AppData + "mpv.net";

                    if (!Directory.Exists(_ConfigFolder))
                    {
                        try
                        {
                            using (Process proc = new Process())
                            {
                                proc.StartInfo.UseShellExecute = false;
                                proc.StartInfo.CreateNoWindow = true;
                                proc.StartInfo.FileName = "powershell.exe";
                                proc.StartInfo.Arguments = $@"-Command New-Item -Path '{_ConfigFolder}' -ItemType Directory";
                                proc.Start();
                                proc.WaitForExit();
                            }
                        }
                        catch (Exception) { }

                        if (!Directory.Exists(_ConfigFolder))
                            Directory.CreateDirectory(_ConfigFolder);
                    }

                    _ConfigFolder = _ConfigFolder.AddSep();

                    if (!File.Exists(_ConfigFolder + "input.conf"))
                    {
                        File.WriteAllText(_ConfigFolder + "input.conf", nds_ingest_player.Properties.Resources.input_conf);

                        string scriptOptsPath = _ConfigFolder + "script-opts" + System.IO.Path.DirectorySeparatorChar;

                        if (!Directory.Exists(scriptOptsPath))
                        {
                            Directory.CreateDirectory(scriptOptsPath);
                            File.WriteAllText(scriptOptsPath + "console.conf", BR + "scale=1.5" + BR);
                            string content = BR + "scalewindowed=1.5" + BR + "hidetimeout=4000" + BR +
                                             "idlescreen=yes" + BR + "scalefullscreen=1.5" + BR;
                            File.WriteAllText(scriptOptsPath + "osc.conf", content);
                        }
                    }
                }

                return _ConfigFolder;
            }
        }

        Dictionary<string, string> _Conf;

        public Dictionary<string, string> Conf
        {
            get
            {
                if (_Conf == null)
                {
                    _Conf = new Dictionary<string, string>();

                    if (File.Exists(ConfPath))
                        foreach (var i in File.ReadAllLines(ConfPath))
                            if (i.Contains("=") && !i.TrimStart().StartsWith("#"))
                            {
                                string key = i.Substring(0, i.IndexOf("=")).Trim();
                                string value = i.Substring(i.IndexOf("=") + 1).Trim();

                                if (key.StartsWith("-"))
                                    key = key.TrimStart('-');

                                if (value.Contains("#") && !value.StartsWith("#") &&
                                    !value.StartsWith("'#") && !value.StartsWith("\"#"))

                                    value = value.Substring(0, value.IndexOf("#")).Trim();

                                _Conf[key] = value;
                            }

                    foreach (var i in _Conf)
                        ProcessProperty(i.Key, i.Value);
                }

                return _Conf;
            }
        }



        void UpdateVideoSize(string w, string h)
        {
            Size size = new Size(GetPropertyInt(w), GetPropertyInt(h));

            if (size.Width == 0 || size.Height == 0)
                return;

            if (VideoRotate == 90 || VideoRotate == 270)
                size = new Size(size.Height, size.Width);

            if (VideoSize != size)
            {
                VideoSize = size;
                InvokeEvent(VideoSizeChanged, VideoSizeChangedAsync, size);
                VideoSizeAutoResetEvent.Set();
            }
        }

        public void MainEventLoop()
        {
            while (true)
                mpv_wait_event(Handle, -1);
        }

        public void EventLoop()
        {
            while (true)
            {
                IntPtr ptr = mpv_wait_event(NamedHandle, -1);
                mpv_event evt = (mpv_event)Marshal.PtrToStructure(ptr, typeof(mpv_event));

                try
                {
                    switch (evt.event_id)
                    {
                        case mpv_event_id.MPV_EVENT_SHUTDOWN:
                            IsQuitNeeded = false;
                            Shutdown?.Invoke();
                            ShutdownAutoResetEvent.Set();
                            return;
                        case mpv_event_id.MPV_EVENT_LOG_MESSAGE:
                            {
                                var data = (mpv_event_log_message)Marshal.PtrToStructure(evt.data, typeof(mpv_event_log_message));

                                if (data.log_level == mpv_log_level.MPV_LOG_LEVEL_INFO)
                                {
                                    string prefix = ConvertFromUtf8(data.prefix);

                                    if (prefix == "bd")
                                        ProcessBluRayLogMessage(ConvertFromUtf8(data.text));
                                }

                                if (LogMessage != null || LogMessageAsync != null)
                                {
                                    string msg = $"[{ConvertFromUtf8(data.prefix)}] {ConvertFromUtf8(data.text)}";
                                    InvokeAsync(LogMessageAsync, data.log_level, msg);
                                    LogMessage?.Invoke(data.log_level, msg);
                                }
                            }
                            break;
                        case mpv_event_id.MPV_EVENT_CLIENT_MESSAGE:
                            break;
                        case mpv_event_id.MPV_EVENT_VIDEO_RECONFIG:
                            UpdateVideoSize("dwidth", "dheight");
                            InvokeEvent(VideoReconfig, VideoReconfigAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_END_FILE:
                            {
                                var data = (mpv_event_end_file)Marshal.PtrToStructure(evt.data, typeof(mpv_event_end_file));
                                var reason = (mpv_end_file_reason)data.reason;
                                InvokeAsync(EndFileAsync, reason);
                                EndFile?.Invoke(reason);
                                FileEnded = true;
                                if (reason == mpv_end_file_reason.MPV_END_FILE_REASON_ERROR)
                                {
                                    if (Path.Contains(".mp4"))
                                    {
                                        CommandV("loadfile", Path.Replace(".mp4", ".MOV"));
                                    }
                                    else if (Path.Contains(".wmv"))
                                    {
                                        CommandV("loadfile", Path.Replace(".wmv", ".mp4"));
                                    }
                                }
                            }
                            break;
                        case mpv_event_id.MPV_EVENT_FILE_LOADED:
                            {
                                //if (App.AutoPlay && Paused)
                                //    SetPropertyBool("pause", false);
                                SetPropertyBool("pause", false);

                                Duration = TimeSpan.FromSeconds(GetPropertyDouble("duration"));

                                path = GetPropertyString("path");

                                if (!VideoTypes.Contains(path.Ext()) || AudioTypes.Contains(path.Ext()))
                                {
                                    UpdateVideoSize("width", "height");
                                    VideoSizeAutoResetEvent.Set();
                                }
                                InvokeEvent(FileLoaded, FileLoadedAsync);
                            }
                            break;
                        case mpv_event_id.MPV_EVENT_PROPERTY_CHANGE:
                            {
                                var data = (mpv_event_property)Marshal.PtrToStructure(evt.data, typeof(mpv_event_property));

                                if (data.format == mpv_format.MPV_FORMAT_FLAG)
                                {
                                    lock (BoolPropChangeActions)
                                        foreach (var pair in BoolPropChangeActions)
                                            if (pair.Key == data.name)
                                            {
                                                bool value = Marshal.PtrToStructure<int>(data.data) == 1;

                                                foreach (var action in pair.Value)
                                                    action.Invoke(value);
                                            }
                                }
                                else if (data.format == mpv_format.MPV_FORMAT_STRING)
                                {
                                    lock (StringPropChangeActions)
                                        foreach (var pair in StringPropChangeActions)
                                            if (pair.Key == data.name)
                                            {
                                                string value = ConvertFromUtf8(Marshal.PtrToStructure<IntPtr>(data.data));

                                                foreach (var action in pair.Value)
                                                    action.Invoke(value);
                                            }
                                }
                                else if (data.format == mpv_format.MPV_FORMAT_INT64)
                                {
                                    lock (IntPropChangeActions)
                                        foreach (var pair in IntPropChangeActions)
                                            if (pair.Key == data.name)
                                            {
                                                int value = Marshal.PtrToStructure<int>(data.data);

                                                foreach (var action in pair.Value)
                                                    action.Invoke(value);
                                            }
                                }
                                else if (data.format == mpv_format.MPV_FORMAT_NONE)
                                {
                                    lock (PropChangeActions)
                                        foreach (var pair in PropChangeActions)
                                            if (pair.Key == data.name)
                                                foreach (var action in pair.Value)
                                                    action.Invoke();
                                }
                                else if (data.format == mpv_format.MPV_FORMAT_DOUBLE)
                                {
                                    lock (DoublePropChangeActions)
                                        foreach (var pair in DoublePropChangeActions)
                                            if (pair.Key == data.name)
                                            {
                                                double value = Marshal.PtrToStructure<double>(data.data);

                                                foreach (var action in pair.Value)
                                                    action.Invoke(value);
                                            }
                                }
                            }
                            break;
                        case mpv_event_id.MPV_EVENT_GET_PROPERTY_REPLY:
                            InvokeEvent(GetPropertyReply, GetPropertyReplyAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_SET_PROPERTY_REPLY:
                            InvokeEvent(SetPropertyReply, SetPropertyReplyAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_COMMAND_REPLY:
                            InvokeEvent(CommandReply, CommandReplyAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_START_FILE:
                            InvokeEvent(StartFile, StartFileAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_AUDIO_RECONFIG:
                            InvokeEvent(AudioReconfig, AudioReconfigAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_SEEK:
                            InvokeEvent(Seek, SeekAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_PLAYBACK_RESTART:
                            InvokeEvent(PlaybackRestart, PlaybackRestartAsync);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        void ProcessBluRayLogMessage(string msg)
        {
            lock (BluRayTitles)
            {
                if (msg.Contains(" 0 duration: "))
                    BluRayTitles.Clear();

                if (msg.Contains(" duration: "))
                {
                    int start = msg.IndexOf(" duration: ") + 11;
                    BluRayTitles.Add(new TimeSpan(
                        msg.Substring(start, 2).ToInt(),
                        msg.Substring(start + 3, 2).ToInt(),
                        msg.Substring(start + 6, 2).ToInt()));
                }
            }
        }

        public void SetBluRayTitle(int id)
        {
            LoadFiles(new[] { @"bd://" + id }, false, false);
        }

        void InvokeEvent(Action action, Action asyncAction)
        {
            InvokeAsync(asyncAction);
            action?.Invoke();
        }

        void InvokeEvent<T>(Action<T> action, Action<T> asyncAction, T t)
        {
            InvokeAsync(asyncAction, t);
            action?.Invoke(t);
        }

        void InvokeAsync(Action action)
        {
            if (action != null)
            {
                foreach (Action a in action.GetInvocationList())
                {
                    var a2 = a;
                    App.RunTask(a2);
                }
            }
        }

        void InvokeAsync<T>(Action<T> action, T t)
        {
            if (action != null)
            {
                foreach (Action<T> a in action.GetInvocationList())
                {
                    var a2 = a;
                    App.RunTask(() => a2.Invoke(t));
                }
            }
        }

        void InvokeAsync<T1, T2>(Action<T1, T2> action, T1 t1, T2 t2)
        {
            if (action != null)
            {
                foreach (Action<T1, T2> a in action.GetInvocationList())
                {
                    var a2 = a;
                    App.RunTask(() => a2.Invoke(t1, t2));
                }
            }
        }

        public void Command(string command)
        {
            mpv_error err = mpv_command_string(Handle, command);

            if (err < 0)
                HandleError(err, "error executing command: " + command);
        }

        public void CommandV(params string[] args)
        {
            int count = args.Length + 1;
            IntPtr[] pointers = new IntPtr[count];
            IntPtr rootPtr = Marshal.AllocHGlobal(IntPtr.Size * count);

            for (int index = 0; index < args.Length; index++)
            {
                var bytes = GetUtf8Bytes(args[index]);
                IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                pointers[index] = ptr;
            }

            Marshal.Copy(pointers, 0, rootPtr, count);
            mpv_error err = mpv_command(Handle, rootPtr);

            foreach (IntPtr ptr in pointers)
                Marshal.FreeHGlobal(ptr);

            Marshal.FreeHGlobal(rootPtr);
            if (err < 0)
                HandleError(err, "error executing command: " + string.Join("\n", args));
        }

        public string Expand(string value)
        {
            if (value == null)
                return "";

            if (!value.Contains("${"))
                return value;

            string[] args = { "expand-text", value };
            int count = args.Length + 1;
            IntPtr[] pointers = new IntPtr[count];
            IntPtr rootPtr = Marshal.AllocHGlobal(IntPtr.Size * count);

            for (int index = 0; index < args.Length; index++)
            {
                var bytes = GetUtf8Bytes(args[index]);
                IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                pointers[index] = ptr;
            }

            Marshal.Copy(pointers, 0, rootPtr, count);
            IntPtr resultNodePtr = Marshal.AllocHGlobal(16);
            mpv_error err = mpv_command_ret(Handle, rootPtr, resultNodePtr);

            foreach (IntPtr ptr in pointers)
                Marshal.FreeHGlobal(ptr);

            Marshal.FreeHGlobal(rootPtr);

            if (err < 0)
            {
                HandleError(err, "error executing command: " + string.Join("\n", args));
                Marshal.FreeHGlobal(resultNodePtr);
                return "property expansion error";
            }

            mpv_node resultNode = Marshal.PtrToStructure<mpv_node>(resultNodePtr);
            string ret = ConvertFromUtf8(resultNode.str);
            mpv_free_node_contents(resultNodePtr);
            Marshal.FreeHGlobal(resultNodePtr);
            return ret;
        }

        public bool GetPropertyBool(string name)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_FLAG, out IntPtr lpBuffer);
            if (err < 0)
                HandleError(err, "error getting property: " + name);
            return lpBuffer.ToInt32() != 0;
        }

        public void SetPropertyBool(string name, bool value)
        {
            long val = value ? 1 : 0;
            mpv_error err = mpv_set_property(Handle, GetUtf8Bytes(name), mpv_format.MPV_FORMAT_FLAG, ref val);
            if (err < 0)
                HandleError(err, $"error setting property: {name} = {value}");
        }

        public int GetPropertyInt(string name)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_INT64, out IntPtr lpBuffer);
            if (err < 0)
                HandleError(err, "error getting property: " + name);
            return lpBuffer.ToInt32();
        }

        public void SetPropertyInt(string name, int value)
        {
            long val = value;
            mpv_error err = mpv_set_property(Handle, GetUtf8Bytes(name), mpv_format.MPV_FORMAT_INT64, ref val);
            if (err < 0)
                HandleError(err, $"error setting property: {name} = {value}");
        }

        public void SetPropertyLong(string name, long value)
        {
            mpv_error err = mpv_set_property(Handle, GetUtf8Bytes(name), mpv_format.MPV_FORMAT_INT64, ref value);
            if (err < 0)
                HandleError(err, $"error setting property: {name} = {value}");
        }

        public long GetPropertyLong(string name)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_INT64, out IntPtr lpBuffer);
            if (err < 0)
                HandleError(err, "error getting property: " + name);
            return lpBuffer.ToInt64();
        }

        public double GetPropertyDouble(string name, bool handleError = true)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_DOUBLE, out double value);
            if (err < 0 && handleError)
                HandleError(err, "error getting property: " + name);
            return value;
        }

        public void SetPropertyDouble(string name, double value)
        {
            double val = value;
            mpv_error err = mpv_set_property(Handle, GetUtf8Bytes(name), mpv_format.MPV_FORMAT_DOUBLE, ref val);
            if (err < 0)
                HandleError(err, $"error setting property: {name} = {value}");
        }

        public string GetPropertyString(string name)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_STRING, out IntPtr lpBuffer);

            if (err == 0)
            {
                string ret = ConvertFromUtf8(lpBuffer);
                mpv_free(lpBuffer);
                return ret;
            }

            if (err < 0)
                HandleError(err, "error getting property: " + name);

            return "";
        }

        public void SetPropertyString(string name, string value)
        {
            byte[] bytes = GetUtf8Bytes(value);
            mpv_error err = mpv_set_property(Handle, GetUtf8Bytes(name), mpv_format.MPV_FORMAT_STRING, ref bytes);
            if (err < 0)
                HandleError(err, $"error setting property: {name} = {value}");
        }

        public string GetPropertyOsdString(string name)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_OSD_STRING, out IntPtr lpBuffer);

            if (err == 0)
            {
                string ret = ConvertFromUtf8(lpBuffer);
                mpv_free(lpBuffer);
                return ret;
            }

            if (err < 0)
                HandleError(err, "error getting property: " + name);

            return "";
        }

        public void ObservePropertyInt(string name, Action<int> action)
        {
            lock (IntPropChangeActions)
            {
                if (!IntPropChangeActions.ContainsKey(name))
                {
                    mpv_error err = mpv_observe_property(NamedHandle, 0, name, mpv_format.MPV_FORMAT_INT64);

                    if (err < 0)
                        HandleError(err, "error observing property: " + name);
                    else
                        IntPropChangeActions[name] = new List<Action<int>>();
                }

                if (IntPropChangeActions.ContainsKey(name))
                    IntPropChangeActions[name].Add(action);
            }
        }

        public void ObservePropertyDouble(string name, Action<double> action)
        {
            lock (DoublePropChangeActions)
            {
                if (!DoublePropChangeActions.ContainsKey(name))
                {
                    mpv_error err = mpv_observe_property(NamedHandle, 0, name, mpv_format.MPV_FORMAT_DOUBLE);

                    if (err < 0)
                        HandleError(err, "error observing property: " + name);
                    else
                        DoublePropChangeActions[name] = new List<Action<double>>();
                }

                if (DoublePropChangeActions.ContainsKey(name))
                    DoublePropChangeActions[name].Add(action);
            }
        }

        public void ObservePropertyBool(string name, Action<bool> action)
        {
            lock (BoolPropChangeActions)
            {
                if (!BoolPropChangeActions.ContainsKey(name))
                {
                    mpv_error err = mpv_observe_property(NamedHandle, 0, name, mpv_format.MPV_FORMAT_FLAG);

                    if (err < 0)
                        HandleError(err, "error observing property: " + name);
                    else
                        BoolPropChangeActions[name] = new List<Action<bool>>();
                }

                if (BoolPropChangeActions.ContainsKey(name))
                    BoolPropChangeActions[name].Add(action);
            }
        }

        public void ObservePropertyString(string name, Action<string> action)
        {
            lock (StringPropChangeActions)
            {
                if (!StringPropChangeActions.ContainsKey(name))
                {
                    mpv_error err = mpv_observe_property(NamedHandle, 0, name, mpv_format.MPV_FORMAT_STRING);

                    if (err < 0)
                        HandleError(err, "error observing property: " + name);
                    else
                        StringPropChangeActions[name] = new List<Action<string>>();
                }

                if (StringPropChangeActions.ContainsKey(name))
                    StringPropChangeActions[name].Add(action);
            }
        }

        public void ObserveProperty(string name, Action action)
        {
            lock (PropChangeActions)
            {
                if (!PropChangeActions.ContainsKey(name))
                {
                    mpv_error err = mpv_observe_property(NamedHandle, 0, name, mpv_format.MPV_FORMAT_NONE);

                    if (err < 0)
                        HandleError(err, "error observing property: " + name);
                    else
                        PropChangeActions[name] = new List<Action>();
                }

                if (PropChangeActions.ContainsKey(name))
                    PropChangeActions[name].Add(action);
            }
        }

        public void HandleError(mpv_error err, string msg)
        {
            Console.WriteLine(msg);
            Console.WriteLine(GetError(err));
        }

        public void ProcessCommandLine(bool preInit)
        {
            bool shuffle = false;
            var args = Environment.GetCommandLineArgs().Skip(1);

            string[] preInitProperties = { "input-terminal", "terminal", "input-file", "config",
                "config-dir", "input-conf", "load-scripts", "scripts", "player-operation-mode",
                "idle", "log-file", "msg-color", "dump-stats", "msg-level", "really-quiet" };

            foreach (string i in args)
            {
                string arg = i;

                if (arg.StartsWith("-") && arg.Length > 1)
                {
                    if (!preInit)
                    {
                        if (arg == "--profile=help")
                        {
                            continue;
                        }
                        else if (arg == "--vd=help" || arg == "--ad=help")
                        {
                            continue;
                        }
                        else if (arg == "--audio-device=help")
                        {
                            Console.WriteLine(GetPropertyOsdString("audio-device-list"));
                            continue;
                        }
                        else if (arg == "--version")
                        {
                            Console.WriteLine(App.Version);
                            continue;
                        }
                        else if (arg == "--input-keylist")
                        {
                            Console.WriteLine(GetPropertyString("input-key-list").Replace(",", BR));
                            continue;
                        }
                        else if (arg.StartsWith("--command="))
                        {
                            Command(arg.Substring(10));
                            continue;
                        }
                    }

                    if (!arg.StartsWith("--"))
                        arg = "-" + arg;

                    if (!arg.Contains("="))
                    {
                        if (arg.Contains("--no-"))
                        {
                            arg = arg.Replace("--no-", "--");
                            arg += "=no";
                        }
                        else
                            arg += "=yes";
                    }

                    string left = arg.Substring(2, arg.IndexOf("=") - 2);
                    string right = arg.Substring(left.Length + 3);

                    switch (left)
                    {
                        case "script": left = "scripts"; break;
                        case "audio-file": left = "audio-files"; break;
                        case "sub-file": left = "sub-files"; break;
                        case "external-file": left = "external-files"; break;
                    }
                }
            }

            if (!preInit)
            {
                List<string> files = new List<string>();

                foreach (string i in args)
                    if (!i.StartsWith("--") && (i == "-" || i.Contains("://") ||
                        i.Contains(":\\") || i.StartsWith("\\\\") || File.Exists(i)))

                        files.Add(i);

                LoadFiles(files.ToArray(), !App.Queue, Control.ModifierKeys.HasFlag(Keys.Control) || App.Queue);

                if (shuffle)
                {
                    Command("playlist-shuffle");
                    SetPropertyInt("playlist-pos", 0);
                }

                if (files.Count == 0 || files[0].Contains("://"))
                {
                    VideoSizeChanged?.Invoke(VideoSize);
                    VideoSizeAutoResetEvent.Set();
                }
            }
        }

        public DateTime LastLoad;

        public void LoadFiles(string[] files, bool loadFolder, bool append)
        {
            if (files is null || files.Length == 0)
                return;

            if ((DateTime.Now - LastLoad).TotalMilliseconds < 1000)
                append = true;

            LastLoad = DateTime.Now;

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];

                if (string.IsNullOrEmpty(file))
                    continue;

                if (file.Contains("|"))
                    file = file.Substring(0, file.IndexOf("|"));

                file = ConvertFilePath(file);

                string ext = file.Ext();

                if (SubtitleTypes.Contains(ext))
                    CommandV("sub-add", file);
                else if (!IsMediaExtension(ext) && !file.Contains("://") && Directory.Exists(file) &&
                    File.Exists(System.IO.Path.Combine(file, "BDMV\\index.bdmv")))
                {
                    Command("stop");
                    Thread.Sleep(500);
                    SetPropertyString("bluray-device", file);
                    CommandV("loadfile", @"bd://");
                }
                else
                {
                    if (i == 0 && !append)
                        CommandV("loadfile", file);
                    else
                        CommandV("loadfile", file, "append");
                }
            }

            if (string.IsNullOrEmpty(GetPropertyString("path")))
                SetPropertyInt("playlist-pos", 0);
        }

        public string ConvertFilePath(string path)
        {
            if ((path.Contains(":/") && !path.Contains("://")) || (path.Contains(":\\") && path.Contains("/")))
                path = path.Replace("/", "\\");

            if (!path.Contains(":") && !path.StartsWith("\\\\") && File.Exists(path))
                path = System.IO.Path.GetFullPath(path);

            return path;
        }


        IEnumerable<string> GetMediaFiles(IEnumerable<string> files) => files.Where(i => IsMediaExtension(i.Ext()));

        bool IsMediaExtension(string ext)
        {
            return VideoTypes.Contains(ext) || AudioTypes.Contains(ext) || ImageTypes.Contains(ext);
        }

        bool WasAviSynthLoaded;

        void LoadAviSynth()
        {
            if (!WasAviSynthLoaded)
            {
                string dll = Environment.GetEnvironmentVariable("AviSynthDLL");

                if (File.Exists(dll))
                    Native.LoadLibrary(dll);
                else
                    Native.LoadLibrary("AviSynth.dll");

                WasAviSynthLoaded = true;
            }
        }

        string HistoryPath;


    }
}
