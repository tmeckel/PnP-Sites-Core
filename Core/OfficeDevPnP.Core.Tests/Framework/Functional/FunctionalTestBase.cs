﻿using Microsoft.Online.SharePoint.TenantAdministration;
using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeDevPnP.Core.Entities;
using System;
using System.Collections.Generic;
using System.Configuration;

namespace OfficeDevPnP.Core.Tests.Framework.Functional
{
    [TestClass()]
    public abstract class FunctionalTestBase
    {
        private static string sitecollectionNamePrefix = "TestPnPSC_12345_";

        internal static string centralSiteCollectionUrl = "";
        internal static string centralSubSiteUrl = "";
        internal const string centralSubSiteName = "sub";
        internal static bool debugMode = false;
        internal string sitecollectionName = "";

        #region Test preparation
        public static void ClassInitBase(TestContext context, bool noScriptSite = false)
        {
            // Drop all previously created site collections to keep the environment clean
            using (var tenantContext = TestCommon.CreateTenantClientContext())
            {
                if (!debugMode)
                {
                    CleanupAllTestSiteCollections(tenantContext);

                    // Each class inheriting from this base class gets a central test site collection, so let's create that one
                    var tenant = new Tenant(tenantContext);
                    centralSiteCollectionUrl = CreateTestSiteCollection(tenant, sitecollectionNamePrefix + Guid.NewGuid().ToString());

                    // Add a default sub site
                    centralSubSiteUrl = CreateTestSubSite(tenant, centralSiteCollectionUrl, centralSubSiteName);

#if !ONPREMISES
                    // Apply noscript setting
                    if (noScriptSite)
                    {
                        Console.WriteLine("Setting site {0} as NoScript", centralSiteCollectionUrl);
                        tenant.SetSiteProperties(centralSiteCollectionUrl, noScriptSite: true);
                    }
#endif
                }
            }
        }

        public static void ClassCleanupBase()
        {
            if (!debugMode)
            {
                using (var tenantContext = TestCommon.CreateTenantClientContext())
                {
#if !ONPREMISES
                    CleanupAllTestSiteCollections(tenantContext);
#else
                    // first cleanup the just created one...most likely it's not indexed yet
                    try
                    {
                        Tenant t = new Tenant(tenantContext);
                        t.DeleteSiteCollection(centralSiteCollectionUrl, false);
                    }
                    catch { }

                    // Use search based site collection retrieval to delete the one's that are left over from failed test cases
                    CleanupAllTestSiteCollections(tenantContext);
#endif
                }
            }
        }

        [TestInitialize()]
        public virtual void Initialize()
        {
            sitecollectionName = sitecollectionNamePrefix + Guid.NewGuid().ToString();
        }

#endregion

#region Helper methods
        internal static string CreateTestSiteCollection(Tenant tenant, string sitecollectionName)
        {
            try
            {
                string devSiteUrl = TestCommon.AppSetting("SPODevSiteUrl");
                string siteToCreateUrl = GetTestSiteCollectionName(devSiteUrl, sitecollectionName);

                string siteOwnerLogin = TestCommon.AppSetting("SPOUserName");
                if (TestCommon.AppOnlyTesting())
                {
                    using (var clientContext = TestCommon.CreateClientContext())
                    {
                        List<UserEntity> admins = clientContext.Web.GetAdministrators();
                        siteOwnerLogin = admins[0].LoginName.Split(new string[] { "|" }, StringSplitOptions.RemoveEmptyEntries)[2];
                    }
                }

                SiteEntity siteToCreate = new SiteEntity()
                {
                    Url = siteToCreateUrl,
                    Template = "STS#0",
                    Title = "Test",
                    Description = "Test site collection",
                    SiteOwnerLogin = siteOwnerLogin,
                    Lcid = 1033,
                    StorageMaximumLevel = 100,
                    UserCodeMaximumLevel = 0
                };

                tenant.CreateSiteCollection(siteToCreate, false, true);

                return siteToCreateUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToDetailedString(tenant.Context));
                throw;
            }
        }

        private static void CleanupAllTestSiteCollections(ClientContext tenantContext)
        {
#if !ONPREMISES
            var tenant = new Tenant(tenantContext);

            try
            {
                var siteCols = tenant.GetSiteCollections();

                foreach (var siteCol in siteCols)
                {
                    if (siteCol.Url.Contains(sitecollectionNamePrefix))
                    {
                        try
                        {
                            // Drop the site collection from the recycle bin
                            if (tenant.CheckIfSiteExists(siteCol.Url, "Recycled"))
                            {
                                tenant.DeleteSiteCollectionFromRecycleBin(siteCol.Url, false);
                            }
                            else
                            {
                                // Eat the exceptions: would occur if the site collection is already in the recycle bin.
                                try
                                {
                                    // ensure the site collection in unlocked state before deleting
                                    tenant.SetSiteLockState(siteCol.Url, SiteLockState.Unlock);
                                }
                                catch { }

                                // delete the site collection, do not use the recyle bin
                                tenant.DeleteSiteCollection(siteCol.Url, false);
                            }
                        }
                        catch (Exception ex)
                        {
                            // eat all exceptions
                            Console.WriteLine(ex.ToDetailedString(tenant.Context));
                        }
                    }
                }
            }
            // catch exceptions with the GetSiteCollections call and log them so we can grab the corelation ID
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToDetailedString(tenant.Context));
                throw;
            }
#endif
        }

        internal static string CreateTestSubSite(Tenant tenant, string sitecollectionUrl, string subSiteName)
        {
            try
            {
                // Create a sub site in the central site collection
                using (var cc = TestCommon.CreateClientContext(sitecollectionUrl))
                {
                    //Create sub site
                    SiteEntity sub = new SiteEntity() { Title = "Sub site for engine testing", Url = subSiteName, Description = "" };
                    var subWeb = cc.Web.CreateWeb(sub);
                    subWeb.EnsureProperty(t => t.Url);
                    return subWeb.Url;
                }

            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToDetailedString());
                throw;
            }

            // Below approach is not working on edog...to be investigated
            //// create a sub site in the central site collection
            //Site site = tenant.GetSiteByUrl(sitecollectionUrl);
            //tenant.Context.Load(site);
            //tenant.Context.ExecuteQueryRetry();
            //Web web = site.RootWeb;
            //web.Context.Load(web);
            //web.Context.ExecuteQueryRetry();

            ////Create sub site
            //SiteEntity sub = new SiteEntity() { Title = "Sub site for engine testing", Url = subSiteName, Description = "" };
            //var subWeb = web.CreateWeb(sub);
            //subWeb.EnsureProperty(t => t.Url);
            //return subWeb.Url;
        }

        private static string GetTestSiteCollectionName(string devSiteUrl, string siteCollection)
        {
            Uri u = new Uri(devSiteUrl);
            string host = String.Format("{0}://{1}", u.Scheme, u.DnsSafeHost);

            string path = u.AbsolutePath;
            if (path.EndsWith("/"))
            {
                path = path.Substring(0, path.Length - 1);
            }
            path = path.Substring(0, path.LastIndexOf('/'));

            return string.Format("{0}{1}/{2}", host, path, siteCollection);
        }
#endregion

    }
}
