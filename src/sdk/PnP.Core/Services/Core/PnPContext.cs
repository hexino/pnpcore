﻿using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using PnP.Core.Model.AzureActiveDirectory;
using PnP.Core.Model.SharePoint;
using PnP.Core.Model.Teams;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace PnP.Core.Services
{
    /// <summary>
    /// PnP Context class...the glue between the model and the data stores
    /// </summary>
    public class PnPContext : IDisposable
    {
        #region Private fields

        private bool graphCanUseBeta = true;
        private static readonly HttpClient httpClient = new HttpClient();

        #endregion

        #region Lazy properties for fluent API

        private readonly Lazy<IWeb> web = new Lazy<IWeb>(() =>
        {
            return new Web();
        }, true);

        private readonly Lazy<ISite> site = new Lazy<ISite>(() =>
        {
            return new Site();
        }, true);

        private readonly Lazy<ITeam> team = new Lazy<ITeam>(() =>
        {
            return new Team();
        }, true);

        private readonly Lazy<IGroup> group = new Lazy<IGroup>(() =>
        {
            return new Group();
        }, true);

        #endregion

        #region Private properties

        private readonly ISettings settings;
        private readonly TelemetryClient telemetry;
        private Batch currentBatch;

        #endregion

        #region Constructors

        /// <summary>
        /// Public constructor for an SPO context based on target site URL
        /// </summary>
        /// <param name="url">The URL of the site as a string</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="authenticationProvider">The authentication provider to authenticate against the target site url</param>
        /// <param name="sharePointRestClient">SharePoint REST HTTP client</param>
        /// <param name="microsoftGraphClient">Microsoft Graph HTTP client</param>
        /// <param name="telemetryClient">AppInsights client for telemetry work</param>
        public PnPContext(string url, ILogger logger,
                                      IAuthenticationProvider authenticationProvider,
                                      SharePointRestClient sharePointRestClient,
                                      MicrosoftGraphClient microsoftGraphClient,
                                      ISettings settingsClient,
                                      TelemetryClient telemetryClient) : this(new Uri(url), logger, authenticationProvider, sharePointRestClient, microsoftGraphClient, settingsClient, telemetryClient)
        {
        }

        /// <summary>
        /// Public constructor for an SPO context based on target site URL
        /// </summary>
        /// <param name="uri">The URI of the site as a URI</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="authenticationProvider">The authentication provider to authenticate against the target site url</param>
        /// <param name="sharePointRestClient">SharePoint REST HTTP client</param>
        /// <param name="microsoftGraphClient">Microsoft Graph HTTP client</param>
        /// <param name="telemetryClient">AppInsights client for telemetry work</param>
        public PnPContext(Uri uri, ILogger logger,
                                   IAuthenticationProvider authenticationProvider,
                                   SharePointRestClient sharePointRestClient,
                                   MicrosoftGraphClient microsoftGraphClient,
                                   ISettings settingsClient,
                                   TelemetryClient telemetryClient): this(logger, authenticationProvider, sharePointRestClient, microsoftGraphClient, settingsClient, telemetryClient)
        {
            Uri = uri;

            // Populate the Azure AD tenant id
            if (settingsClient != null && !settingsClient.DisableTelemetry)
            {
                SetAADTenantId();
            }
        }

        /// <summary>
        /// Public constructor for an SPO context based on target site URL
        /// </summary>
        /// <param name="groupId">The id of the Office 365 group</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="authenticationProvider">The authentication provider to authenticate against the target site url</param>
        /// <param name="sharePointRestClient">SharePoint REST HTTP client</param>
        /// <param name="microsoftGraphClient">Microsoft Graph HTTP client</param>
        /// <param name="telemetryClient">AppInsights client for telemetry work</param>
        public PnPContext(Guid groupId, ILogger logger,
                                        IAuthenticationProvider authenticationProvider,
                                        SharePointRestClient sharePointRestClient,
                                        MicrosoftGraphClient microsoftGraphClient,
                                        ISettings settingsClient,
                                        TelemetryClient telemetryClient) : this(logger, authenticationProvider, sharePointRestClient, microsoftGraphClient, settingsClient, telemetryClient)
        {
            // Ensure the group is loaded, given we've received the group id we can populate the metadata of the group model upfront before loading it
            (Group as Group).Metadata.Add(PnPConstants.MetaDataGraphId, groupId.ToString());
            // Do the default group load, should load all properties
            Group.GetAsync().GetAwaiter().GetResult();
            // If the group has a linked SharePoint site then WebUrl is populated
            Uri = Group.WebUrl;

            // Populate the Azure AD tenant id
            if (settingsClient != null && !settingsClient.DisableTelemetry)
            {
                SetAADTenantId();
            }
        }

        private PnPContext(ILogger logger,
                           IAuthenticationProvider authenticationProvider,
                           SharePointRestClient sharePointRestClient,
                           MicrosoftGraphClient microsoftGraphClient,
                           ISettings settingsClient,
                           TelemetryClient telemetryClient)
        {
            Id = Guid.NewGuid();
            Logger = logger;

#if DEBUG
            Mode = PnPContextMode.Default;
#endif
            AuthenticationProvider = authenticationProvider;
            RestClient = sharePointRestClient;
            GraphClient = microsoftGraphClient;
            settings = settingsClient;
            telemetry = telemetryClient;

            if (settingsClient != null)
            {
                GraphFirst = settingsClient.GraphFirst;
                GraphAlwaysUseBeta = settingsClient.GraphAlwaysUseBeta;
                GraphCanUseBeta = settingsClient.GraphCanUseBeta;
            }

            BatchClient = new BatchClient(this, settingsClient, telemetryClient);
        }
        #endregion

        #region Properties

        /// <summary>
        /// Uri of the SharePoint site we're working against
        /// </summary>
        public Uri Uri { get; private set; }

        /// <summary>
        /// Connected logger
        /// </summary>
        public ILogger Logger { get; }

        /// <summary>
        /// Connected authentication provider
        /// </summary>
        public IAuthenticationProvider AuthenticationProvider { get; }

        /// <summary>
        /// Connected SharePoint REST client
        /// </summary>
        public SharePointRestClient RestClient { get; }

        /// <summary>
        /// Connected Microsoft Graph client
        /// </summary>
        public MicrosoftGraphClient GraphClient { get; }

        /// <summary>
        /// Connected batch client
        /// </summary>
        internal BatchClient BatchClient { get; }

        /// <summary>
        /// Unique id for this <see cref="PnPContext"/>
        /// </summary>
        internal Guid Id { get; private set; }

#if DEBUG

        #region Test related properties
        /// <summary>
        /// Mode this context operates in
        /// </summary>
        internal PnPContextMode Mode { get; private set; }

        /// <summary>
        /// Id associated to this context in a test case
        /// </summary>
        internal int TestId { get; private set; }

        /// <summary>
        /// Name of the test case using this context
        /// </summary>
        internal string TestName { get; private set; }

        /// <summary>
        /// Path of the test case
        /// </summary>
        internal string TestFilePath { get; private set; }

        /// <summary>
        /// Generate the .request and .debug files, can be handy for debugging
        /// </summary>
        internal bool GenerateTestMockingDebugFiles { get; private set; }

        /// <summary>
        /// Urls's used by the test cases
        /// </summary>
        internal Dictionary<string, Uri> TestUris { get; set; }

        #endregion

#endif

        #region Graph related properties
        
        /// <summary>
        /// Controls whether the library will try to use Microsoft Graph over REST whenever that's defined in the model
        /// </summary>
        public bool GraphFirst { get; set;  } = true;

        /// <summary>
        /// If true than all requests to Microsoft Graph use the beta endpoint
        /// </summary>
        public bool GraphAlwaysUseBeta { get; set; } = false;

        /// <summary>
        /// If true than the Graph beta endpoint is used when there's no other option, default approach stays using the v1 endpoint
        /// </summary>
        public bool GraphCanUseBeta
        {
            get
            {
                if (GraphAlwaysUseBeta)
                {
                    return true;
                }
                else
                {
                    return graphCanUseBeta;
                }
            }

            set
            {
                if (GraphAlwaysUseBeta && value == false)
                {
                    throw new Exception("The GraphAlwaysUseBeta is set to true, you can't turn off the 'on-demand' beta support. First set GraphAlwaysUseBeta to false before turning of GraphCanUseBeta");
                }

                graphCanUseBeta = value;
            }
        }
        #endregion

        /// <summary>
        /// Current batch, used for implicit batching
        /// </summary>
        public Batch CurrentBatch
        {
            get
            {
                if (currentBatch == null || currentBatch.Executed)
                {
                    currentBatch = BatchClient.EnsureBatch();
                }

                return currentBatch;
            }
        }

        /// <summary>
        /// Are there pending requests to execute (in the case of batching)
        /// </summary>
        public bool HasPendingRequests
        {
            get
            {
                return CurrentBatch.Requests.Count > 0;
            }
        }

        /// <summary>
        /// Entry point for the Web Object
        /// </summary>
        public IWeb Web
        {
            get
            {
                (web.Value as Web).PnPContext = this;
                return web.Value;
            }
        }

        /// <summary>
        /// Entry point for the Site Object
        /// </summary>
        public ISite Site
        {
            get
            {
                // Special case: PnPContext.Site.RootWeb = PnPContext.Web, 
                // only applies when the loaded Url is from a root web of the site
                if (!IsSubSite(Uri))
                {
                    (site.Value as Site).RootWeb = web.Value;
                }

                (site.Value as Site).PnPContext = this;
                return site.Value;
            }
        }

        /// <summary>
        /// Entry point for the Team Object
        /// </summary>
        public ITeam Team
        {
            get
            {
                (team.Value as Team).PnPContext = this;
                return team.Value;
            }
        }

        /// <summary>
        /// Entry point for the Office 365 Group Object
        /// </summary>
        public IGroup Group
        {
            get
            {
                (group.Value as Group).PnPContext = this;
                return group.Value;
            }
        }
        #endregion

        #region Public Methods   

        /// <summary>
        /// Creates a new batch
        /// </summary>
        /// <returns></returns>
        public Batch NewBatch()
        {
            return BatchClient.EnsureBatch();
        }

        /// <summary>
        /// Method to execute the current batch
        /// </summary>
        /// <returns>The asynchronous task that will be executed</returns>
        public async Task ExecuteAsync()
        {
            await BatchClient.ExecuteBatch(CurrentBatch).ConfigureAwait(false);
        }

        /// <summary>
        /// Method to execute a given batch
        /// </summary>
        /// <param name="batch">Batch to execute</param>
        /// <returns>The asynchronous task that will be executed</returns>
        public async Task ExecuteAsync(Batch batch)
        {
            if (batch == null)
            {
                throw new ArgumentNullException(nameof(batch));
            }

            await BatchClient.ExecuteBatch(batch).ConfigureAwait(false);
        }

        #endregion

#if DEBUG

        #region Internal methods to support unit testing

        internal void SetRecordingMode(int id, string testName, string sourceFilePath, bool generateTestMockingDebugFiles, Dictionary<string, Uri> testUris)
        {
            SetTestConfiguration(id, testName, sourceFilePath, generateTestMockingDebugFiles, testUris);
            Mode = PnPContextMode.Record;
        }

        internal void SetMockMode(int id, string testName, string sourceFilePath, bool generateTestMockingDebugFiles, Dictionary<string, Uri> testUris)
        {
            SetTestConfiguration(id, testName, sourceFilePath, generateTestMockingDebugFiles, testUris);
            Mode = PnPContextMode.Mock;
        }

        private void SetTestConfiguration(int id, string testName, string sourceFilePath, bool generateTestMockingDebugFiles, Dictionary<string, Uri> testUris)
        {
            TestId = id;
            TestName = testName;
            TestFilePath = sourceFilePath.Replace(".cs", "");
            GenerateTestMockingDebugFiles = generateTestMockingDebugFiles;
            TestUris = testUris;
        }

        #endregion

#endif

        #region IDisposable implementation

        private bool disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                // Flush telemetry
                telemetry.Flush();                
            }

            disposed = true;
        }

        #endregion

        #region Helper methods

        /// <summary>
        /// Gets the Azure Active Directory tenant id. Using the client.svc endpoint approach as that one will also work with vanity SharePoint domains
        /// </summary>
        private void SetAADTenantId()
        {
            if (settings.AADTenantId == Guid.Empty && Uri != null)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, $"{Uri}/_vti_bin/client.svc"))
                {
                    request.Headers.Add("Authorization", "Bearer");
                    HttpResponseMessage response = httpClient.SendAsync(request).GetAwaiter().GetResult();

                    // Grab the tenant id from the wwwauthenticate header. 
                    var bearerResponseHeader = response.Headers.WwwAuthenticate.ToString();
                    const string bearer = "Bearer realm=\"";
                    var bearerIndex = bearerResponseHeader.IndexOf(bearer, StringComparison.Ordinal);

                    var realmIndex = bearerIndex + bearer.Length;

                    if (bearerResponseHeader.Length >= realmIndex + 36)
                    {
                        var targetRealm = bearerResponseHeader.Substring(realmIndex, 36);

                        if (Guid.TryParse(targetRealm, out Guid realmGuid))
                        {
                            settings.AADTenantId = realmGuid;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Simple is sub site check based upon the url pattern
        /// </summary>
        /// <param name="site">Uri to check</param>
        /// <returns>True if sub site, false otherwise</returns>
        private static bool IsSubSite(Uri site)
        {
            string cleanedPath = site.AbsolutePath.ToLower().Replace("/teams/", "").Replace("/sites/", "").TrimEnd(new char[] { '/' });

            if (cleanedPath.Contains("/"))
            {
                return true;
            }

            return false;
        }

        #endregion
    }
}
