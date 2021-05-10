﻿using System;
using System.Collections.Generic;
using System.Text;
using PnP.Core.Modernization.Model.Classic;

namespace PnP.Core.Modernization.Services.MappingProviders
{
    /// <summary>
    /// Defines the input for a URL mapping provider
    /// </summary>
    public class UrlMappingProviderInput : MappingProviderInput
    {
        /// <summary>
        /// Defines the source URL to map
        /// </summary>
        public Uri Url { get; set; }
    }
}
