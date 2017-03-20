﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graph;
using System.Net.Http.Headers;
using OfficeDevPnP.Core.Entities;
using System.IO;
using OfficeDevPnP.Core.Diagnostics;

namespace OfficeDevPnP.Core.Framework.Graph
{
    public static class UnifiedGroupsUtility
    {
        private const int defaultRetryCount = 10;
        private const int defaultDelay = 500;

        private static GraphServiceClient CreateGraphClient(String accessToken, int retryCount = defaultRetryCount, int delay = defaultDelay)
        {
            // Creates a new GraphServiceClient instance using a custom PnPHttpProvider
            // which natively supports retry logic for throttled requests
            // Default are 10 retries with a base delay of 500ms
            var result = new GraphServiceClient(new DelegateAuthenticationProvider(
                        async (requestMessage) =>
                        {
                            if (!String.IsNullOrEmpty(accessToken))
                            {
                                // Configure the HTTP bearer Authorization Header
                                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("bearer", accessToken);
                            }
                        }), new PnPHttpProvider(retryCount, delay));

            return (result);
        }

        /// <summary>
        /// Returns the URL of the Modern SharePoint Site backing an Office 365 Group (i.e. Unified Group)
        /// </summary>
        /// <param name="groupId">The ID of the Office 365 Group</param>
        /// <param name="accessToken">The OAuth 2.0 Access Token to use for invoking the Microsoft Graph</param>
        /// <param name="retryCount">Number of times to retry the request in case of throttling</param>
        /// <param name="delay">Milliseconds to wait before retrying the request. The delay will be increased (doubled) every retry</param>
        /// <returns>The URL of the modern site backing the Office 365 Group</returns>
        public static String GetUnifiedGroupSiteUrl(String groupId, String accessToken,
            int retryCount = 10, int delay = 500)
        {
            if (String.IsNullOrEmpty(groupId))
            {
                throw new ArgumentNullException(nameof(groupId));
            }

            if (String.IsNullOrEmpty(accessToken))
            {
                throw new ArgumentNullException(nameof(accessToken));
            }
            string result;
            try
            {
                // Use a synchronous model to invoke the asynchronous process
                result = Task.Run(async () =>
                {
                    String siteUrl = null;

                    var graphClient = CreateGraphClient(accessToken, retryCount, delay);

                    var groupDrive = await graphClient.Groups[groupId].Drive.Request().GetAsync();
                    if (groupDrive != null)
                    {
                        var rootFolder = await graphClient.Groups[groupId].Drive.Root.Request().GetAsync();
                        if (rootFolder != null)
                        {
                            if (!String.IsNullOrEmpty(rootFolder.WebUrl))
                            {
                                var modernSiteUrl = rootFolder.WebUrl;
                                siteUrl = modernSiteUrl.Substring(0, modernSiteUrl.LastIndexOf("/"));
                            }
                        }
                    }

                    return (siteUrl);

                }).GetAwaiter().GetResult();
            }
            catch (ServiceException ex)
            {
                Log.Error(Constants.LOGGING_SOURCE, CoreResources.GraphExtensions_ErrorOccured, ex.Error.Message);
                throw;
            }
            return (result);
        }

        /// <summary>
        /// Creates a new Office 365 Group (i.e. Unified Group) with its backing Modern SharePoint Site
        /// </summary>
        /// <param name="displayName">The Display Name for the Office 365 Group</param>
        /// <param name="description">The Description for the Office 365 Group</param>
        /// <param name="mailNickname">The Mail Nickname for the Office 365 Group</param>
        /// <param name="accessToken">The OAuth 2.0 Access Token to use for invoking the Microsoft Graph</param>
        /// <param name="owners">A list of UPNs for group owners, if any</param>
        /// <param name="members">A list of UPNs for group members, if any</param>
        /// <param name="groupLogo">The binary stream of the logo for the Office 365 Group</param>
        /// <param name="isPrivate">Defines whether the group will be private or public, optional with default false (i.e. public)</param>
        /// <param name="retryCount">Number of times to retry the request in case of throttling</param>
        /// <param name="delay">Milliseconds to wait before retrying the request. The delay will be increased (doubled) every retry</param>
        /// <returns>The just created Office 365 Group</returns>
        public static UnifiedGroupEntity CreateUnifiedGroup(string displayName, string description, string mailNickname,
            string accessToken, string[] owners = null, string[] members = null, Stream groupLogo = null,
            bool isPrivate = false, int retryCount = 10, int delay = 500)
        {
            UnifiedGroupEntity result = null;

            if (String.IsNullOrEmpty(displayName))
            {
                throw new ArgumentNullException(nameof(displayName));
            }

            if (String.IsNullOrEmpty(description))
            {
                throw new ArgumentNullException(nameof(description));
            }

            if (String.IsNullOrEmpty(mailNickname))
            {
                throw new ArgumentNullException(nameof(mailNickname));
            }

            if (String.IsNullOrEmpty(accessToken))
            {
                throw new ArgumentNullException(nameof(accessToken));
            }

            try
            {
                // Use a synchronous model to invoke the asynchronous process
                result = Task.Run(async () =>
                {
                    var group = new UnifiedGroupEntity();

                    var graphClient = CreateGraphClient(accessToken, retryCount, delay);

                    // Prepare the group resource object
                    var newGroup = new Microsoft.Graph.Group
                    {
                        DisplayName = displayName,
                        Description = description,
                        MailNickname = mailNickname,
                        MailEnabled = true,
                        SecurityEnabled = false,
                        Visibility = isPrivate == true ? "Private" : "Public",
                        GroupTypes = new List<string> { "Unified" },
                    };

                    Microsoft.Graph.Group addedGroup = null;
                    String modernSiteUrl = null;

                    // Add the group to the collection of groups (if it does not exist
                    if (addedGroup == null)
                    {
                        addedGroup = await graphClient.Groups.Request().AddAsync(newGroup);

                        if (addedGroup != null)
                        {
                            group.DisplayName = addedGroup.DisplayName;
                            group.Description = addedGroup.Description;
                            group.GroupId = addedGroup.Id;
                            group.Mail = addedGroup.Mail;
                            group.MailNickname = addedGroup.MailNickname;

                            int imageRetryCount = retryCount;

                            if (groupLogo != null)
                            {
                                using (var memGroupLogo = new MemoryStream())
                                {
                                    groupLogo.CopyTo(memGroupLogo);

                                    while (imageRetryCount > 0)
                                    {
                                        bool groupLogoUpdated = false;
                                        memGroupLogo.Position = 0;

                                        using (var tempGroupLogo = new MemoryStream())
                                        {
                                            memGroupLogo.CopyTo(tempGroupLogo);
                                            tempGroupLogo.Position = 0;

                                            try
                                            {
                                                groupLogoUpdated = UpdateUnifiedGroup(addedGroup.Id, accessToken, groupLogo: tempGroupLogo);
                                            }
                                            catch
                                            {
                                                // Skip any exception and simply retry
                                            }
                                        }

                                        // In case of failure retry up to 10 times, with 500ms delay in between
                                        if (!groupLogoUpdated)
                                        {
                                            // Pop up the delay for the group image
                                            await Task.Delay(delay * (retryCount - imageRetryCount));
                                            imageRetryCount--;
                                        }
                                        else
                                        {
                                            break;
                                        }
                                    }
                                }
                            }

                            int driveRetryCount = retryCount;

                            while (driveRetryCount > 0 && String.IsNullOrEmpty(modernSiteUrl))
                            {
                                try
                                {
                                    modernSiteUrl = GetUnifiedGroupSiteUrl(addedGroup.Id, accessToken);
                                }
                                catch
                                {
                                    // Skip any exception and simply retry
                                }

                                // In case of failure retry up to 10 times, with 500ms delay in between
                                if (String.IsNullOrEmpty(modernSiteUrl))
                                {
                                    await Task.Delay(delay * (retryCount - driveRetryCount));
                                    driveRetryCount--;
                                }
                            }

                            group.SiteUrl = modernSiteUrl;
                        }
                    }

                    #region Handle group's owners

                    if (owners != null && owners.Length > 0)
                    {
                        await AddOwners(owners, graphClient, addedGroup);
                    }

                    #endregion

                    #region Handle group's members

                    if (members != null && members.Length > 0)
                    {
                        await AddMembers(members, graphClient, addedGroup);
                    }

                    #endregion

                    return (group);

                }).GetAwaiter().GetResult();
            }
            catch (ServiceException ex)
            {
                Log.Error(Constants.LOGGING_SOURCE, CoreResources.GraphExtensions_ErrorOccured, ex.Error.Message);
                throw;
            }
            return (result);
        }

        private static async Task AddMembers(string[] members, GraphServiceClient graphClient, Group addedGroup)
        {
            foreach (var m in members)
            {
                // Search for the user object
                var memberQuery = await graphClient.Users
                    .Request()
                    .Filter($"userPrincipalName eq '{m}'")
                    .GetAsync();

                var member = memberQuery.FirstOrDefault();

                if (member != null)
                {
                    try
                    {
                        // And if any, add it to the collection of group's owners
                        await graphClient.Groups[addedGroup.Id].Members.References.Request().AddAsync(member);
                    }
                    catch (ServiceException ex)
                    {
                        if (ex.Error.Code == "Request_BadRequest" &&
                            ex.Error.Message.Contains("added object references already exist"))
                        {
                            // Skip any already existing member
                        }
                        else
                        {
                            throw ex;
                        }
                    }
                }
            }
        }

        private static async Task AddOwners(string[] owners, GraphServiceClient graphClient, Group addedGroup)
        {
            foreach (var o in owners)
            {
                // Search for the user object
                var ownerQuery = await graphClient.Users
                    .Request()
                    .Filter($"userPrincipalName eq '{o}'")
                    .GetAsync();

                var owner = ownerQuery.FirstOrDefault();

                if (owner != null)
                {
                    try
                    {
                        // And if any, add it to the collection of group's owners
                        await graphClient.Groups[addedGroup.Id].Owners.References.Request().AddAsync(owner);
                    }
                    catch (ServiceException ex)
                    {
                        if (ex.Error.Code == "Request_BadRequest" &&
                            ex.Error.Message.Contains("added object references already exist"))
                        {
                            // Skip any already existing owner
                        }
                        else
                        {
                            throw ex;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Updates the logo, members or visibility state of an Office 365 Group
        /// </summary>
        /// <param name="groupId">The ID of the Office 365 Group</param>
        /// <param name="displayName">The Display Name for the Office 365 Group</param>
        /// <param name="description">The Description for the Office 365 Group</param>
        /// <param name="owners">A list of UPNs for group owners, if any, to be added to the site</param>
        /// <param name="members">A list of UPNs for group members, if any, to be added to the site</param>
        /// <param name="isPrivate">Defines whether the group will be private or public, optional with default false (i.e. public)</param>
        /// <param name="groupLogo">The binary stream of the logo for the Office 365 Group</param>
        /// <param name="accessToken">The OAuth 2.0 Access Token to use for invoking the Microsoft Graph</param>
        /// <param name="retryCount">Number of times to retry the request in case of throttling</param>
        /// <param name="delay">Milliseconds to wait before retrying the request. The delay will be increased (doubled) every retry</param>
        /// <returns>Declares whether the Office 365 Group has been updated or not</returns>
        public static bool UpdateUnifiedGroup(string groupId,
            string accessToken, int retryCount = 10, int delay = 500,
            string displayName = null, string description = null, string[] owners = null, string[] members = null,
            Stream groupLogo = null, bool isPrivate = false)
        {
            bool result;
            try
            {
                // Use a synchronous model to invoke the asynchronous process
                result = Task.Run(async () =>
                {
                    var graphClient = CreateGraphClient(accessToken, retryCount, delay);

                    var groupToUpdate = await graphClient.Groups[groupId]
                        .Request()
                        .GetAsync();

                    #region Logic to update the group DisplayName and Description

                    var updateGroup = false;
                    var groupUpdated = false;

                    // Check if we have to update the DisplayName
                    if (!String.IsNullOrEmpty(displayName) && groupToUpdate.DisplayName != displayName)
                    {
                        groupToUpdate.DisplayName = displayName;
                        updateGroup = true;
                    }

                    // Check if we have to update the Description
                    if (!String.IsNullOrEmpty(description) && groupToUpdate.Description != description)
                    {
                        groupToUpdate.Description = description;
                        updateGroup = true;
                    }

                    // Check if visibility has changed for the Group
                    bool existingIsPrivate = groupToUpdate.Visibility == "Private";
                    if (existingIsPrivate != isPrivate)
                    {
                        groupToUpdate.Visibility = isPrivate == true ? "Private" : "Public";
                        updateGroup = true;
                    }

                    // Check if we are to add owners
                    if (owners != null && owners.Length > 0)
                    {
                        // For each and every owner
                        await AddOwners(owners, graphClient, groupToUpdate);
                        updateGroup = true;
                    }

                    // Check if we are to add members
                    if (members != null && members.Length > 0)
                    {
                        // For each and every owner
                        await AddMembers(members, graphClient, groupToUpdate);
                        updateGroup = true;
                    }

                    // If the Group has to be updated, just do it
                    if (updateGroup)
                    {
                        var updatedGroup = await graphClient.Groups[groupId]
                            .Request()
                            .UpdateAsync(groupToUpdate);

                        groupUpdated = true;
                    }

                    #endregion

                    #region Logic to update the group Logo

                    var logoUpdated = false;

                    if (groupLogo != null)
                    {
                        await graphClient.Groups[groupId].Photo.Content.Request().PutAsync(groupLogo);
                        logoUpdated = true;
                    }

                    #endregion

                    // If any of the previous update actions has been completed
                    return (groupUpdated || logoUpdated);

                }).GetAwaiter().GetResult();
            }
            catch (ServiceException ex)
            {
                Log.Error(Constants.LOGGING_SOURCE, CoreResources.GraphExtensions_ErrorOccured, ex.Error.Message);
                throw;
            }
            return (result);
        }

        /// <summary>
        /// Creates a new Office 365 Group (i.e. Unified Group) with its backing Modern SharePoint Site
        /// </summary>
        /// <param name="displayName">The Display Name for the Office 365 Group</param>
        /// <param name="description">The Description for the Office 365 Group</param>
        /// <param name="mailNickname">The Mail Nickname for the Office 365 Group</param>
        /// <param name="accessToken">The OAuth 2.0 Access Token to use for invoking the Microsoft Graph</param>
        /// <param name="owners">A list of UPNs for group owners, if any</param>
        /// <param name="members">A list of UPNs for group members, if any</param>
        /// <param name="groupLogoPath">The path of the logo for the Office 365 Group</param>
        /// <param name="isPrivate">Defines whether the group will be private or public, optional with default false (i.e. public)</param>
        /// <param name="retryCount">Number of times to retry the request in case of throttling</param>
        /// <param name="delay">Milliseconds to wait before retrying the request. The delay will be increased (doubled) every retry</param>
        /// <returns>The just created Office 365 Group</returns>
        public static UnifiedGroupEntity CreateUnifiedGroup(string displayName, string description, string mailNickname,
            string accessToken, string[] owners = null, string[] members = null, String groupLogoPath = null,
            bool isPrivate = false, int retryCount = 10, int delay = 500)
        {
            if (!String.IsNullOrEmpty(groupLogoPath) && !System.IO.File.Exists(groupLogoPath))
            {
                throw new FileNotFoundException(CoreResources.GraphExtensions_GroupLogoFileDoesNotExist, groupLogoPath);
            }
            else if (!String.IsNullOrEmpty(groupLogoPath))
            {
                using (var groupLogoStream = new FileStream(groupLogoPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return (CreateUnifiedGroup(displayName, description,
                        mailNickname, accessToken, owners, members,
                        groupLogo: groupLogoStream, isPrivate: isPrivate,
                        retryCount: retryCount, delay: delay));
                }
            }
            else
            {
                return (CreateUnifiedGroup(displayName, description,
                    mailNickname, accessToken, owners, members,
                    groupLogo: null, isPrivate: isPrivate,
                    retryCount: retryCount, delay: delay));
            }
        }

        /// <summary>
        /// Creates a new Office 365 Group (i.e. Unified Group) with its backing Modern SharePoint Site
        /// </summary>
        /// <param name="displayName">The Display Name for the Office 365 Group</param>
        /// <param name="description">The Description for the Office 365 Group</param>
        /// <param name="mailNickname">The Mail Nickname for the Office 365 Group</param>
        /// <param name="accessToken">The OAuth 2.0 Access Token to use for invoking the Microsoft Graph</param>
        /// <param name="owners">A list of UPNs for group owners, if any</param>
        /// <param name="members">A list of UPNs for group members, if any</param>
        /// <param name="isPrivate">Defines whether the group will be private or public, optional with default false (i.e. public)</param>
        /// <param name="retryCount">Number of times to retry the request in case of throttling</param>
        /// <param name="delay">Milliseconds to wait before retrying the request. The delay will be increased (doubled) every retry</param>
        /// <returns>The just created Office 365 Group</returns>
        public static UnifiedGroupEntity CreateUnifiedGroup(string displayName, string description, string mailNickname,
            string accessToken, string[] owners = null, string[] members = null,
            bool isPrivate = false, int retryCount = 10, int delay = 500)
        {
            return (CreateUnifiedGroup(displayName, description,
                mailNickname, accessToken, owners, members,
                groupLogo: null, isPrivate: isPrivate,
                retryCount: retryCount, delay: delay));
        }

        /// <summary>
        /// Deletes an Office 365 Group (i.e. Unified Group)
        /// </summary>
        /// <param name="groupId">The ID of the Office 365 Group</param>
        /// <param name="accessToken">The OAuth 2.0 Access Token to use for invoking the Microsoft Graph</param>
        /// <param name="retryCount">Number of times to retry the request in case of throttling</param>
        /// <param name="delay">Milliseconds to wait before retrying the request. The delay will be increased (doubled) every retry</param>
        public static void DeleteUnifiedGroup(String groupId, String accessToken,
            int retryCount = 10, int delay = 500)
        {
            if (String.IsNullOrEmpty(groupId))
            {
                throw new ArgumentNullException(nameof(groupId));
            }

            if (String.IsNullOrEmpty(accessToken))
            {
                throw new ArgumentNullException(nameof(accessToken));
            }
            try
            {
                // Use a synchronous model to invoke the asynchronous process
                Task.Run(async () =>
                {

                    var graphClient = CreateGraphClient(accessToken, retryCount, delay);
                    await graphClient.Groups[groupId].Request().DeleteAsync();

                }).GetAwaiter().GetResult();
            }
            catch (ServiceException ex)
            {
                Log.Error(Constants.LOGGING_SOURCE, CoreResources.GraphExtensions_ErrorOccured, ex.Error.Message);
                throw;
            }
        }

        /// <summary>
        /// Get an Office 365 Group (i.e. Unified Group) by Id
        /// </summary>
        /// <param name="groupId">The ID of the Office 365 Group</param>
        /// <param name="accessToken">The OAuth 2.0 Access Token to use for invoking the Microsoft Graph</param>
        /// <param name="retryCount">Number of times to retry the request in case of throttling</param>
        /// <param name="delay">Milliseconds to wait before retrying the request. The delay will be increased (doubled) every retry</param>
        public static UnifiedGroupEntity GetUnifiedGroup(String groupId, String accessToken,
            int retryCount = 10, int delay = 500)
        {
            if (String.IsNullOrEmpty(groupId))
            {
                throw new ArgumentNullException(nameof(groupId));
            }

            if (String.IsNullOrEmpty(accessToken))
            {
                throw new ArgumentNullException(nameof(accessToken));
            }

            UnifiedGroupEntity result = null;
            try
            {
                // Use a synchronous model to invoke the asynchronous process
                result = Task.Run(async () =>
                {
                    UnifiedGroupEntity group = null;

                    var graphClient = CreateGraphClient(accessToken, retryCount, delay);

                    var g = await graphClient.Groups[groupId].Request().GetAsync();

                    group = new UnifiedGroupEntity
                    {
                        GroupId = g.Id,
                        DisplayName = g.DisplayName,
                        Description = g.Description,
                        Mail = g.Mail,
                        MailNickname = g.MailNickname,
                        SiteUrl = GetUnifiedGroupSiteUrl(groupId, accessToken),
                    };

                    return (group);

                }).GetAwaiter().GetResult();
            }
            catch (ServiceException ex)
            {
                Log.Error(Constants.LOGGING_SOURCE, CoreResources.GraphExtensions_ErrorOccured, ex.Error.Message);
                throw;
            }
            return (result);
        }

        /// <summary>
        /// Returns all the Office 365 Groups in the current Tenant based on a startIndex. IncludeSite adds additional properties about the Modern SharePoint Site backing the group
        /// </summary>
        /// <param name="accessToken">The OAuth 2.0 Access Token to use for invoking the Microsoft Graph</param>
        /// <param name="displayName">The DisplayName of the Office 365 Group</param>
        /// <param name="mailNickname">The MailNickname of the Office 365 Group</param>
        /// <param name="startIndex">Not relevant anymore</param>
        /// <param name="endIndex">Not relevant anymore</param>
        /// <param name="includeSite">Defines whether to return details about the Modern SharePoint Site backing the group. Default is true.</param>
        /// <param name="retryCount">Number of times to retry the request in case of throttling</param>
        /// <param name="delay">Milliseconds to wait before retrying the request. The delay will be increased (doubled) every retry</param>
        /// <returns>An IList of SiteEntity objects</returns>
        public static List<UnifiedGroupEntity> ListUnifiedGroups(string accessToken,
            String displayName = null, string mailNickname = null,
            int startIndex = 0, int endIndex = 999, bool includeSite = true,
            int retryCount = 10, int delay = 500)
        {
            if (String.IsNullOrEmpty(accessToken))
            {
                throw new ArgumentNullException(nameof(accessToken));
            }

            List<UnifiedGroupEntity> result = null;
            try
            {
                // Use a synchronous model to invoke the asynchronous process
                result = Task.Run(async () =>
                {
                    List<UnifiedGroupEntity> groups = new List<UnifiedGroupEntity>();

                    var graphClient = CreateGraphClient(accessToken, retryCount, delay);

                    // Apply the DisplayName filter, if any
                    var displayNameFilter = !String.IsNullOrEmpty(displayName) ? $" and startswith(DisplayName,'{displayName}')" : String.Empty;
                    var mailNicknameFilter = !String.IsNullOrEmpty(mailNickname) ? $" and startswith(MailNickname,'{mailNickname}')" : String.Empty;

                    var pagedGroups = await graphClient.Groups
                        .Request()
                        .Filter($"groupTypes/any(grp: grp eq 'Unified'){displayNameFilter}{mailNicknameFilter}")
                        .Top(endIndex)
                        .GetAsync();

                    Int32 pageCount = 0;
                    Int32 currentIndex = 0;

                    while (true)
                    {
                        pageCount++;

                        foreach (var g in pagedGroups)
                        {
                            currentIndex++;

                            if (currentIndex >= startIndex)
                            {
                                var group = new UnifiedGroupEntity
                                {
                                    GroupId = g.Id,
                                    DisplayName = g.DisplayName,
                                    Description = g.Description,
                                    Mail = g.Mail,
                                    MailNickname = g.MailNickname,
                                };

                                if (includeSite)
                                {
                                    try
                                    {
                                        group.SiteUrl = GetUnifiedGroupSiteUrl(g.Id, accessToken);
                                    }
                                    catch (ServiceException e)
                                    {
                                        group.SiteUrl = e.Error.Message;
                                    }
                                }

                                groups.Add(group);

                            }
                        }

                        if (pagedGroups.NextPageRequest != null && groups.Count < endIndex)
                        {
                            pagedGroups = await pagedGroups.NextPageRequest.GetAsync();
                        }
                        else
                        {
                            break;
                        }
                    }

                    return (groups);
                }).GetAwaiter().GetResult();
            }
            catch (ServiceException ex)
            {
                Log.Error(Constants.LOGGING_SOURCE, CoreResources.GraphExtensions_ErrorOccured, ex.Error.Message);
                throw;
            }
            return (result);
        }
    }
}
