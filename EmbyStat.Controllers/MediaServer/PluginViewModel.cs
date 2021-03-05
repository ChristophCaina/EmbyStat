﻿using System;

namespace EmbyStat.Controllers.MediaServer
{
    public class PluginViewModel
    {
	    public string Name { get; set; }
	    public DateTime ConfigurationDateLastModified { get; set; }
	    public string Version { get; set; }
	    public string AssemblyFileName { get; set; }
	    public string ConfigurationFileName { get; set; }
	    public string Description { get; set; }
	    public string Id { get; set; }
	    public string ImageUrl { get; set; }
	}
}
