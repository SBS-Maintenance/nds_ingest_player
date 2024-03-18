
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Windows.Forms;

using Microsoft.Win32;

using static mpvnet.Global;

namespace mpvnet
{
    public class Folder
    {
        public static string Startup { get; } = Application.StartupPath.AddSep();
        public static string AppData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).AddSep();
    }


}
