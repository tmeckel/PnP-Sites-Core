﻿using Microsoft.SharePoint.Client;
using OfficeDevPnP.Core.Framework.Provisioning.Model;
using OfficeDevPnP.Core.Framework.Provisioning.Providers.Xml.Resolvers;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace OfficeDevPnP.Core.Framework.Provisioning.Providers.Xml.Serializers
{
    /// <summary>
    /// Class to serialize/deserialize the Custom Actions
    /// </summary>
    [TemplateSchemaSerializer(SerializationSequence = 1300, DeserializationSequence = 1300,
        MinimalSupportedSchemaVersion = XMLPnPSchemaVersion.V201605,
        Default = true)]
    internal class CustomActionsSerializer : PnPBaseSchemaSerializer<CustomActions>
    {
        public override void Deserialize(object persistence, ProvisioningTemplate template)
        {
            var customActions = persistence.GetPublicInstancePropertyValue("CustomActions");
            var expressions = new Dictionary<Expression<Func<CustomActions, Object>>, IResolver>();

            expressions.Add(c => c.SiteCustomActions[0].CommandUIExtension, new XmlAnyFromSchemaToModelValueResolver("CommandUIExtension"));
            expressions.Add(c => c.SiteCustomActions[0].RegistrationType, new FromStringToEnumValueResolver(typeof(UserCustomActionRegistrationType)));
            expressions.Add(c => c.SiteCustomActions[0].Rights, new FromStringToBasePermissionsValueResolver());

            PnPObjectsMapper.MapProperties(customActions, template.CustomActions, expressions, true);
        }

        public override void Serialize(ProvisioningTemplate template, object persistence)
        {
            var customActionsTypeName = $"{PnPSerializationScope.Current?.BaseSchemaNamespace}.CustomActions, {PnPSerializationScope.Current?.BaseSchemaAssemblyName}";
            var customActionsType = Type.GetType(customActionsTypeName, true);
            var customActionTypeName = $"{PnPSerializationScope.Current?.BaseSchemaNamespace}.CustomAction, {PnPSerializationScope.Current?.BaseSchemaAssemblyName}";
            var customActionType = Type.GetType(customActionTypeName, true);
            var registrationTypeTypeName = $"{PnPSerializationScope.Current?.BaseSchemaNamespace}.RegistrationType";
            var registrationTypeType = Type.GetType(registrationTypeTypeName, true);
            var commandUIExtensionTypeName = $"{PnPSerializationScope.Current?.BaseSchemaNamespace}.CustomActionCommandUIExtension";
            var commandUIExtensionType = Type.GetType(commandUIExtensionTypeName, true);

            var target = Activator.CreateInstance(customActionsType, true);

            var expressions = new Dictionary<string, IResolver>();
            expressions.Add($"{customActionType}.Rights", new FromBasePermissionsToStringValueResolver());
            expressions.Add($"{customActionType}.RegistrationType", new FromStringToEnumValueResolver(registrationTypeType));
            expressions.Add($"{customActionType}.RegistrationTypeSpecified", new ExpressionValueResolver(() => true));
            expressions.Add($"{customActionType}.SequenceSpecified", new ExpressionValueResolver(() => true));
            expressions.Add($"{customActionType}.CommandUIExtension", new XmlAnyFromModeToSchemalValueResolver(commandUIExtensionType));

            PnPObjectsMapper.MapProperties(template.CustomActions, target, expressions, recursive: true);

            persistence.GetPublicInstanceProperty("CustomActions").SetValue(persistence, target);
        }
    }
}
