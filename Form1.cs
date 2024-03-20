using System;
using System.Drawing;
using System.Windows.Forms;

using static mpvnet.Native;
using static mpvnet.Global;
using mpvnet;

using System.Runtime.InteropServices;

using System.Text.RegularExpressions;
using System.Diagnostics;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Linq;



// Based on Cho Yunghoon's SPAR
// Modified by Kim Syehoon

namespace nds_ingest_player
{
    public partial class Form1 : Form
    {
        private IntPtr mpvWindowHandle=IntPtr.Zero;
        private static Form1 Instance { get; set; }
        private IntPtr panelHandle=IntPtr.Zero;
        private bool WasMaximized;
        private int LastCycleFullscreen;

        Point _lastCursorPosition;

        private Action seek_to_pos = null;

        private int position=0;

        private String baseURI = "http://10.40.25.111:5010/";

        private bool KeepSize() => App.StartSize == "session" || App.StartSize == "always";
        public Form1()
        {
            InitializeComponent();

            Instance=this;
            panelHandle = panel1.Handle;
            Core.Init(panel1.Handle);

            LastCycleFullscreen = Environment.TickCount;

            while (mpvWindowHandle == IntPtr.Zero)
            {
                mpvWindowHandle = FindWindowEx(panelHandle, IntPtr.Zero, "mpv", null);
            }

            ClipboardNotification.ClipboardUpdate += ClipboardNotification_ClipboardUpdate;

            Core.SetPropertyString("lavfi-complex", "[aid1]pan=stereo|c0<c0+c1|c1<c0+c1[ao]");
        }


        private void ClipboardNotification_ClipboardUpdate(object sender, EventArgs e)
        {
            Regex mediaID_regex = new Regex("(^[N]{1}\\d{8,8}[V]{1}\\d{5})");
            String clipboard_content=Clipboard.GetText();
            Match m=mediaID_regex.Match(clipboard_content);
            if (m.Groups.Count > 1)
            {
                var mediID = m.Groups[0].ToString();
                mediaIDTextBox.Text = mediID;
                play_start();

            }
        }

        private bool IsMouseInOSC()
        {
            Point pos = panel1.PointToClient(MousePosition);
            float top = 0;

            if (!Core.Border)
                top = panel1.ClientSize.Height * 0.1f;

            return pos.X < ClientSize.Width * 0.1 ||
           pos.X > ClientSize.Width * 0.9 ||
           pos.Y < top ||
           pos.Y > ClientSize.Height * 0.78;
        }

        private void mpv_SizeChanged(object sender, EventArgs e)
        {
            if (FormBorderStyle != FormBorderStyle.None)
            {
                if (WindowState == FormWindowState.Maximized)
                    WasMaximized = true;
                else if (WindowState == FormWindowState.Normal)
                    WasMaximized = false;
            }
        }


        public void CycleFullscreen(bool enabled)
        {
            LastCycleFullscreen = Environment.TickCount;
            Core.Fullscreen = enabled;

            if (enabled)
            {
                if (WindowState != FormWindowState.Maximized || FormBorderStyle != FormBorderStyle.None)
                {
                    FormBorderStyle = FormBorderStyle.None;
                    WindowState = FormWindowState.Maximized;

                    if (WasMaximized)
                    {
                        Rectangle bounds = Screen.FromControl(this).Bounds;
                        uint SWP_SHOWWINDOW = 0x0040;
                        IntPtr HWND_TOP = IntPtr.Zero;
                        SetWindowPos(Handle, HWND_TOP, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_SHOWWINDOW);
                    }
                }
            }
            else
            {
                if (WindowState == FormWindowState.Maximized && FormBorderStyle == FormBorderStyle.None)
                {
                    if (WasMaximized)
                        WindowState = FormWindowState.Maximized;
                    else
                    {
                        WindowState = FormWindowState.Normal;

                        if (!Core.WasInitialSizeSet)
                            SetFormPosAndSize();
                    }

                    if (Core.Border)
                        FormBorderStyle = FormBorderStyle.Sizable;
                    else
                        FormBorderStyle = FormBorderStyle.None;

                    if (!KeepSize())
                        SetFormPosAndSize();
                }
            }

        }

        private void mpv_Load(object sender, EventArgs e)
        {
            LastCycleFullscreen = Environment.TickCount;
        }

        private void SetFormPosAndSize(bool force = false, bool checkAutofit = true)
        {
            if (!force)
            {
                if (WindowState != FormWindowState.Normal)
                    return;

                if (Core.Fullscreen)
                {
                    CycleFullscreen(true);
                    return;
                }
            }

            Screen screen = Screen.FromControl(this);
            Rectangle workingArea = GetWorkingArea(Handle, screen.WorkingArea);
            int autoFitHeight = Convert.ToInt32(workingArea.Height * Core.Autofit);

            if (Core.VideoSize.Height == 0 || Core.VideoSize.Width == 0)
                Core.VideoSize = new Size((int)(autoFitHeight * (16 / 9f)), autoFitHeight);


            Size videoSize = Core.VideoSize;

            int height = videoSize.Height;
            int width = videoSize.Width;

            if (Core.WasInitialSizeSet)
            {
                if (KeepSize())
                {
                    width = ClientSize.Width;
                    height = ClientSize.Height;
                }
                else if (App.StartSize == "height-always" || App.StartSize == "height-session")
                {
                    height = ClientSize.Height;
                    width = height * videoSize.Width / videoSize.Height;
                }
                else if (App.StartSize == "width-always" || App.StartSize == "width-session")
                {
                    width = ClientSize.Width;
                    height = (int)Math.Ceiling(width * videoSize.Height / (double)videoSize.Width);
                }
            }
        }

        private void panel1_MouseMove(object sender, MouseEventArgs e)
        {
            Point pos = panel1.PointToClient(Cursor.Position);
            Core.Command($"mouse {pos.X} {pos.Y}");
        }

        private int GetLongParameter(int low, int high)
        {
            return ((high << 16) | (low & 0xffff));
        }

        private void panel1_MouseDown(object sender, MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (e.Button == MouseButtons.Left)
            {
                if (IsMouseInOSC())
                {
                    Point pos = panel1.PointToClient(Cursor.Position);
                    int x = pos.X;
                    int y = pos.Y;
                    int longParameter = GetLongParameter(x, y);

                    SendMessage(mpvWindowHandle, 513, longParameter, 1);
                    SendMessage(mpvWindowHandle, 514, longParameter, 1);

                    DateTime start_time = DateTime.Now;
                    while (((GetAsyncKeyState(0x01) & 0x8000) != 0) && IsMouseInOSC())
                    {
                        if (start_time.AddMilliseconds(200) < DateTime.Now)
                        {
                            Point pos_new = panel1.PointToClient(Cursor.Position);
                            Core.Command($"mouse {pos_new.X} {pos_new.Y}");

                            if (Math.Abs(pos.X - pos_new.X) > 5)
                            {
                                longParameter = GetLongParameter(pos.X, pos.Y);
                                SendMessage(mpvWindowHandle, 513, longParameter, 1);
                                SendMessage(mpvWindowHandle, 514, longParameter, 1);
                                pos = pos_new;
                            }
                            start_time = DateTime.Now;
                        }
                    }
                }
                else
                {
                    Core.SetPropertyBool("pause", !Core.GetPropertyBool("pause"));
                }

            }
            if (Width - e.Location.X < 10 && e.Location.Y < 10)
                Core.CommandV("quit");
        }


        private void mpv_FormClosing(object sender, FormClosingEventArgs e)
        {
            Core.CommandV("quit");
            Core.Destroy();
        }

        private void mpv_KeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Shift && e.KeyCode == Keys.Up)
            {
                Core.Command("osd-bar frame-step");
                return;
            }
            if (e.Shift && e.KeyCode == Keys.Up)
            {
                Core.Command("osd-bar seek 0.3 exact");
                return;
            }
            if (!e.Shift && e.KeyCode == Keys.Down)
            {
                Core.Command("osd-bar frame-back-step");
                return;
            }
            if (e.Shift && e.KeyCode == Keys.Down)
            {
                Core.Command("osd-bar seek -0.3 exact");
                return;
            }
            if (!e.Shift && e.KeyCode == Keys.Right)
            {
                Core.Command("osd-bar seek 1 exact");
                return;
            }
            if (e.Shift && e.KeyCode == Keys.Right)
            {
                Core.Command("osd-bar seek 60 exact");
                return;
            }
            if (!e.Shift && e.KeyCode == Keys.Left)
            {
                Core.Command("osd-bar seek -1 exact");
                return;
            }
            if (e.Shift && e.KeyCode == Keys.Left)
            {
                Core.Command("osd-bar seek -60 exact");
                return;
            }
            if (e.KeyCode == Keys.F5)
            {
                position = Core.GetPropertyInt("time-pos");
                play_start();
                Core.FileLoaded += seek_to_pos = () =>
                {
                    Core.FileLoaded -= seek_to_pos;
                    Core.CommandV("seek", position.ToString(), "absolute");
                };
            }
        }


        private void play_start()
        {
            try
            {
                String mediaID = mediaIDTextBox.Text;
                String year = mediaID.Substring(1, 4);
                String month = mediaID.Substring(5, 2);
                String targetVideo = null;
                if (targetVideo==null) 
                { 
                    targetVideo = baseURI + "NDS_MAIN/" + "NDS_VStream/" + year + "-" + month + "/" + mediaID + ".mp4"; 
                }

                Core.CommandV("loadfile", targetVideo);
                Core.FileLoaded += seek_to_pos = () =>
                {
                    Core.FileLoaded -= seek_to_pos;
                    Core.CommandV("seek", "0", "absolute");
                };
            }
            catch
            {
            }
            this.ActiveControl = panel1;
        }


        private void playButton_Click(object sender, EventArgs e)
        {
            play_start();
        }

        private void panel1_DoubleClick(object sender, EventArgs e)
        {
            var is_fullscreen = Core.GetPropertyString("fullscreen");
            if (is_fullscreen == "no")
            {
                Core.SetPropertyString("fullscreen", "yes");
                CycleFullscreen(true);
            }
            else
            {
                Core.SetPropertyString("fullscreen", "no");
                CycleFullscreen(false);
            }
        }

        
    }
}

/// <summary>
/// Provides notifications when the contents of the clipboard is updated.
/// </summary>
public sealed class ClipboardNotification
{
    /// <summary>
    /// Occurs when the contents of the clipboard is updated.
    /// </summary>
    public static event EventHandler ClipboardUpdate;

    private static NotificationForm _form = new NotificationForm();

    /// <summary>
    /// Raises the <see cref="ClipboardUpdate"/> event.
    /// </summary>
    /// <param name="e">Event arguments for the event.</param>
    private static void OnClipboardUpdate(EventArgs e)
    {
        var handler = ClipboardUpdate;
        if (handler != null)
        {
            handler(null, e);
        }
    }

    /// <summary>
    /// Hidden form to recieve the WM_CLIPBOARDUPDATE message.
    /// </summary>
    private class NotificationForm : Form
    {
        public NotificationForm()
        {
            NativeMethods.SetParent(Handle, NativeMethods.HWND_MESSAGE);
            NativeMethods.AddClipboardFormatListener(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                OnClipboardUpdate(null);
            }
            base.WndProc(ref m);
        }
    }

   

}

internal static class NativeMethods
{
    // See http://msdn.microsoft.com/en-us/library/ms649021%28v=vs.85%29.aspx
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public static IntPtr HWND_MESSAGE = new IntPtr(-3);

    // See http://msdn.microsoft.com/en-us/library/ms632599%28VS.85%29.aspx#message_only
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    // See http://msdn.microsoft.com/en-us/library/ms633541%28v=vs.85%29.aspx
    // See http://msdn.microsoft.com/en-us/library/ms649033%28VS.85%29.aspx
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
}

