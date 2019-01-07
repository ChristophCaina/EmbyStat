﻿using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace EmbyStat.Common.Models.Settings
{
    public class AppSettings
    {
        public string Version { get; set; }
        public string UpdateAsset { get; set; }
        public LoggingAppSettings Logging { get; set; }
        public Dirs Dirs { get; set; }
    }

    public class LoggingAppSettings
    {
        public string Directory { get; set; }
    }

    public class Dirs
    {
        public string TempUpdateDir { get; set; }
        public string Updater { get; set; }
    }
}
