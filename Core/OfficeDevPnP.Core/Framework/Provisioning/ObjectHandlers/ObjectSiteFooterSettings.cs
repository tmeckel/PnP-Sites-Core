﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SharePoint.Client;
using OfficeDevPnP.Core.Framework.Provisioning.Model;
using OfficeDevPnP.Core.Diagnostics;
using OfficeDevPnP.Core.Utilities;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace OfficeDevPnP.Core.Framework.Provisioning.ObjectHandlers
{
    internal class ObjectSiteFooterSettings : ObjectHandlerBase
    {
        const string footerNodeKey = "13b7c916-4fea-4bb2-8994-5cf274aeb530";
        const string titleNodeKey = "7376cd83-67ac-4753-b156-6a7b3fa0fc1f";
        const string logoNodeKey = "2e456c2e-3ded-4a6c-a9ea-f7ac4c1b5100";
        const string menuNodeKey = "3a94b35f-030b-468e-80e3-b75ee84ae0ad";
        public override string Name
        {
            get { return "Site Footer"; }
        }

        public override ProvisioningTemplate ExtractObjects(Web web, ProvisioningTemplate template, ProvisioningTemplateCreationInformation creationInfo)
        {
#if !ONPREMISES
            using (var scope = new PnPMonitoredScope(this.Name))
            {
                web.EnsureProperties(w => w.FooterEnabled, w => w.ServerRelativeUrl);

                var footer = new SiteFooter();

                footer.Enabled = web.FooterEnabled;
                var structureString = web.ExecuteGet($"/_api/navigation/MenuState?menuNodeKey='{footerNodeKey}'").GetAwaiter().GetResult();
                var menuState = JsonConvert.DeserializeObject<MenuState>(structureString);

                if (menuState.Nodes.Count > 1)
                {
                    // Find the title node
                    var titleNode = menuState.Nodes.FirstOrDefault(n => n.Title == "2e456c2e-3ded-4a6c-a9ea-f7ac4c1b5100");
                    if (titleNode != null)
                    {
                        if (!string.IsNullOrEmpty(titleNode.SimpleUrl))
                        {
                            footer.Logo = Tokenize(titleNode.SimpleUrl, web.ServerRelativeUrl);
                        }
                        if (!string.IsNullOrEmpty(titleNode.Title))
                        {
                            footer.Name = titleNode.Title;
                        }
                    }
                }
                // find the menu Nodes
                var menuNodesNode = menuState.Nodes.FirstOrDefault(n => n.Title == "3a94b35f-030b-468e-80e3-b75ee84ae0ad");
                if (menuNodesNode != null)
                {
                    foreach (var innerMenuNode in menuNodesNode.Nodes)
                    {
                        footer.FooterLinks.Add(ParseNodes(innerMenuNode, template, web.ServerRelativeUrl));
                    }
                }
                template.Footer = footer;
            }
            return template;
#else
            throw new NotImplementedException();
#endif
        }

#if !ONPREMISES
        private SiteFooterLink ParseNodes(MenuNode node, ProvisioningTemplate template, string webServerRelativeUrl)
        {
            var link = new SiteFooterLink();
            link.DisplayName = node.Title;
            link.Url = Tokenize(node.SimpleUrl, webServerRelativeUrl);

            if (node.Nodes.Count > 0)
            {
                link.FooterLinks = new SiteFooterLinkCollection(template);
                foreach (var childNode in node.Nodes)
                {
                    link.FooterLinks.Add(ParseNodes(childNode, template, webServerRelativeUrl));
                }
            }
            return link;
        }
#endif

        public override TokenParser ProvisionObjects(Web web, ProvisioningTemplate template, TokenParser parser, ProvisioningTemplateApplyingInformation applyingInformation)
        {
#if !ONPREMISES
            using (var scope = new PnPMonitoredScope(this.Name))
            {
                if (template.Footer != null)
                {
                    web.EnsureProperty(w => w.ServerRelativeUrl);
                    web.FooterEnabled = template.Footer.Enabled;
                    web.Update();


                    var structureString = web.ExecuteGet($"/_api/navigation/MenuState?menuNodeKey='{footerNodeKey}'").GetAwaiter().GetResult();
                    var menuState = JsonConvert.DeserializeObject<MenuState>(structureString);

                    var n1 = web.Navigation.GetNodeById(Convert.ToInt32(menuState.StartingNodeKey));

                    web.Context.Load(n1, n => n.Children.IncludeWithDefaultProperties());
                    web.Context.ExecuteQueryRetry();

                    var menuNode = n1.Children.FirstOrDefault(n => n.Title == menuNodeKey);
                    if (menuNode != null)
                    {
                        if (template.Footer.RemoveExistingNodes == true)
                        {
                            menuNode.DeleteObject();
                            web.Context.ExecuteQueryRetry();

                            menuNode = n1.Children.Add(new NavigationNodeCreationInformation()
                            {
                                Title = menuNodeKey
                            });
                        }
                    }
                    else
                    {
                        menuNode = n1.Children.Add(new NavigationNodeCreationInformation()
                        {
                            Title = menuNodeKey
                        });
                    }
                    foreach (var footerLink in template.Footer.FooterLinks)
                    {
                        menuNode.Children.Add(new NavigationNodeCreationInformation()
                        {
                            Url = parser.ParseString(footerLink.Url),
                            Title = parser.ParseString(footerLink.DisplayName)
                        });
                    }
                    if (web.Context.PendingRequestCount() > 0)
                    {
                        web.Context.ExecuteQueryRetry();
                    }

                    var logoNode = n1.Children.FirstOrDefault(n => n.Title == logoNodeKey);
                    if (logoNode != null)
                    {
                        if (string.IsNullOrEmpty(template.Footer.Logo))
                        {
                            // remove the logo
                            logoNode.DeleteObject();
                        }
                        else
                        {
                            logoNode.Url = parser.ParseString(template.Footer.Logo);
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(template.Footer.Logo))
                        {
                            logoNode = n1.Children.Add(new NavigationNodeCreationInformation()
                            {
                                Title = logoNodeKey,
                                Url = parser.ParseString(template.Footer.Logo)
                            });
                        }
                    }
                    if (web.Context.PendingRequestCount() > 0)
                    {
                        web.Context.ExecuteQueryRetry();
                    }

                    var titleNode = n1.Children.FirstOrDefault(n => n.Title == titleNodeKey);
                    if (titleNode != null)
                    {
                        titleNode.EnsureProperty(n => n.Children);
                        if (string.IsNullOrEmpty(template.Footer.Name))
                        {
                            // remove the title
                            titleNode.DeleteObject();
                        }
                        else
                        {
                            titleNode.Children[0].Title = template.Footer.Name;
                            titleNode.Update();
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(template.Footer.Name))
                        {
                            titleNode = n1.Children.Add(new NavigationNodeCreationInformation() { Title = titleNodeKey });
                            titleNode.Children.Add(new NavigationNodeCreationInformation() { Title = template.Footer.Name });
                        }
                    }
                    if (web.Context.PendingRequestCount() > 0)
                    {
                        web.Context.ExecuteQueryRetry();
                    }
                }
            }

            return parser;
#else
            throw new NotImplementedException();
#endif
        }

        public override bool WillExtract(Web web, ProvisioningTemplate template, ProvisioningTemplateCreationInformation creationInfo)
        {
#if !ONPREMISES
            web.EnsureProperties(w => w.Configuration, w => w.WebTemplate);
            var webTemplate = $"{web.WebTemplate}#{web.Configuration}";
            if (webTemplate.Equals("SITEPAGEPUBLISHING#0", StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }
            else
            {
                return false;
            }
#else
            return false;
#endif
        }

        public override bool WillProvision(Web web, ProvisioningTemplate template, ProvisioningTemplateApplyingInformation applyingInformation)
        {
#if !ONPREMISES
            web.EnsureProperties(w => w.Configuration, w => w.WebTemplate);
            var webTemplate = $"{web.WebTemplate}#{web.Configuration}";
            if (webTemplate.Equals("SITEPAGEPUBLISHING#0", StringComparison.InvariantCultureIgnoreCase))
            {
                return template.Footer != null;
            }
            else
            {
                return false;
            }
#else
            return false;
#endif
        }

        private class MenuState
        {
            public string FriendlyUrlPrefix { get; set; }
            public List<MenuNode> Nodes { get; set; }

            public string SimpleUrl { get; set; }
            public string SPSitePrefix { get; set; }
            public string SPWebPrefix { get; set; }
            public string StartingNodeKey { get; set; }
            public string StartingNodeTitle { get; set; }
            public string Version { get; set; }

            public MenuState()
            {
                Nodes = new List<MenuNode>();
            }
        }

        private class MenuNode
        {
            public string FriendlyUrlSegment { get; set; }
            public bool IsDeleted { get; set; }
            public bool IsHidden { get; set; }
            public string Key { get; set; }
            public List<MenuNode> Nodes { get; set; }
            public int NodeType { get; set; }
            public string SimpleUrl { get; set; }
            public string Title { get; set; }

            public MenuNode()
            {
                Nodes = new List<MenuNode>();
            }
        }

        private class MenuStateWrapper
        {
            [JsonProperty("menuState")]
            public MenuState MenuState { get; set; }
        }
    }
}
