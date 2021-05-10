﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace PnP.Core.Modernization.Services.MappingProviders
{
    /// <summary>
    /// Provides the basic interface for a User mapping provider
    /// </summary>
    public interface IUserMappingProvider
    {
        /// <summary>
        /// Maps a user UPN from the source platform to the target platform
        /// </summary>
        /// <param name="input">The input for the mapping activity</param>
        /// <returns>The output of the mapping activity</returns>
        Task<UserMappingProviderOutput> MapUserAsync(UserMappingProviderInput input);
    }
}
