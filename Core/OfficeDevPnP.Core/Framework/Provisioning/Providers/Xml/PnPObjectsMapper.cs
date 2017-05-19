﻿using OfficeDevPnP.Core.Framework.Provisioning.Model;
using OfficeDevPnP.Core.Framework.Provisioning.Providers.Xml.Resolvers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace OfficeDevPnP.Core.Framework.Provisioning.Providers.Xml
{
    /// <summary>
    /// Utility class that maps one object to another
    /// </summary>
    internal static class PnPObjectsMapper
    {
        // TODO: Remember to cover the *Specified problem

        #region MapProperties

        /// <summary>
        /// Maps the properties of a typed source object, to the properties of an untyped destination object
        /// </summary>
        /// <typeparam name="TSource">The type of the source object</typeparam>
        /// <param name="source">The source object</param>
        /// <param name="destination">The destination object</param>
        /// <param name="resolverExpressions">Any custom resolver, optional</param>
        /// <param name="recursive">Defines whether to apply the mapping recursively, optional and by default false</param>
        public static void MapProperties<TSource>(TSource source, Object destination, Dictionary<Expression<Func<TSource, Object>>, IResolver> resolverExpressions = null, Boolean recursive = false)
        {
            Dictionary<string, IResolver> resolvers = ConvertExpressionsToResolvers(resolverExpressions);
            MapProperties(source, destination, resolvers, recursive);
        }

        /// <summary>
        /// Maps the properties of an untyped source object object, to the properties of a typed destination object
        /// </summary>
        /// <typeparam name="TDestination">The type of the destination object</typeparam>
        /// <param name="source">The source object</param>
        /// <param name="destination">The destination object</param>
        /// <param name="resolverExpressions">Any custom resolver, optional</param>
        /// <param name="recursive">Defines whether to apply the mapping recursively, optional and by default false</param>
        public static void MapProperties<TDestination>(Object source, TDestination destination, Dictionary<Expression<Func<TDestination, Object>>, IResolver> resolverExpressions = null, Boolean recursive = false)
        {
            Dictionary<string, IResolver> resolvers = ConvertExpressionsToResolvers(resolverExpressions);
            MapProperties(source, destination, resolvers, recursive);
        }

        /// <summary>
        /// Maps the properties of a source object, to the properties of a destination object
        /// </summary>
        /// <param name="source">The source object</param>
        /// <param name="destination">The destination object</param>
        /// <param name="resolvers">Any custom resolver, optional</param>
        /// <param name="recursive">Defines whether to apply the mapping recursively, optional and by default false</param>
        public static void MapProperties(Object source, Object destination, Dictionary<String, IResolver> resolvers = null, Boolean recursive = false)
        {
            // Retrieve the list of destination properties
            var destinationProperties = destination.GetType().GetProperties(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);

            // Retrieve the list of source properties
            var sourceProperties = source.GetType().GetProperties(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);

            // Normalize the keys of the resolvers, if any, just in case (maybe this step can be removed)
            if (null != resolvers)
            {
                resolvers = resolvers.ToDictionary(i => i.Key.ToUpper(), i => i.Value);
            }

            // Just for the properties that are not collection or complex types of the model
            // and that are not array or Xml domain model related
            var filteredProperties = destinationProperties.Where(
                p => (!Attribute.IsDefined(p, typeof(ObsoleteAttribute)) &&
                (p.PropertyType.BaseType.Name != typeof(ProvisioningTemplateCollection<>).Name || recursive) &&
                // p.PropertyType.BaseType.Name != typeof(BaseModel).Name && // TODO: Think about this rule ...
                (!p.PropertyType.IsArray || recursive) // &&
                // !p.PropertyType.Namespace.Contains(typeof(XMLConstants).Namespace)
                ));
            foreach (var dp in filteredProperties) // TODO: Think about this rule ...
            {
                // Let's try to see if we have a custom resolver for the current property
                var resolverKey = $"{dp.DeclaringType.FullName}.{dp.Name}".ToUpper();
                var resolver = resolvers != null && resolvers.ContainsKey(resolverKey) ? resolvers[resolverKey] : null;

                // Search for the matching source property
                var sp = sourceProperties.FirstOrDefault(p => p.Name.Equals(dp.Name, StringComparison.InvariantCultureIgnoreCase));
                if (null != sp || null != resolver)
                {
                    if (null != resolver)
                    {
                        if (resolver is IValueResolver)
                        {
                            // We have a resolver, thus we use it to resolve the input value
                            dp.SetValue(destination, ((IValueResolver)resolver)
                                .Resolve(source, destination, sp?.GetValue(source)));
                        }
                        else if (resolver is ITypeResolver)
                        {
                            // We have a resolver, thus we use it to resolve the input value
                            if (dp.PropertyType.BaseType.Name == typeof(ProvisioningTemplateCollection<>).Name)
                            {
                                var destinationCollection = dp.GetValue(destination);
                                if (destinationCollection != null)
                                {
                                    var resolvedCollection = ((ITypeResolver)resolver)
                                        .Resolve(source, resolvers, recursive);

                                    destinationCollection.GetType().GetMethod("AddRange",
                                        System.Reflection.BindingFlags.Instance |
                                        System.Reflection.BindingFlags.Public |
                                        System.Reflection.BindingFlags.IgnoreCase)
                                        .Invoke(destinationCollection, new Object[] { resolvedCollection });
                                }
                            }
                            else
                            {
                                dp.SetValue(destination, ((ITypeResolver)resolver)
                                    .Resolve(source, resolvers, recursive));
                            }
                        }
                    }
                    else if (null != sp)
                    {
                        try
                        {
                            // If the destination property is a custom collection of the 
                            // Domain Model and we have the recursive flag enabled
                            if (recursive && dp.PropertyType.BaseType.Name == typeof(ProvisioningTemplateCollection<>).Name)
                            {
                                // We need to recursively handle a collection of properties in the Domain Model
                                var destinationCollection = dp.GetValue(destination);
                                if (destinationCollection != null)
                                {
                                    var resolvedCollection =
                                        PnPObjectsMapper.MapObjects(sp.GetValue(source),
                                        new CollectionFromSchemaToModelTypeResolver(
                                            dp.PropertyType.BaseType.GenericTypeArguments[0]), resolvers, recursive);

                                    destinationCollection.GetType().GetMethod("AddRange",
                                        System.Reflection.BindingFlags.Instance |
                                        System.Reflection.BindingFlags.Public |
                                        System.Reflection.BindingFlags.IgnoreCase)
                                        .Invoke(destinationCollection, new Object[] { resolvedCollection });
                                }
                            }
                            // If the destination property is an array of the XML
                            // Schema Model and we have the recursive flag enabled
                            else if (recursive && dp.PropertyType.IsArray)
                            {
                                dp.SetValue(destination,
                                        PnPObjectsMapper.MapObjects(sp.GetValue(source),
                                            new CollectionFromModelToSchemaTypeResolver(dp.PropertyType.IsArray ? dp.PropertyType.GetElementType() : null), 
                                            resolvers, recursive));
                            }
                            else
                            {
                                object sourceValue = sp.GetValue(source);
                                if(sourceValue != null && dp.PropertyType == typeof(string) && sp.PropertyType != typeof(string))
                                {
                                    //default conversion to string
                                    sourceValue = sourceValue.ToString();
                                }
                                // We simply need to do 1:1 value mapping
                                dp.SetValue(destination, sourceValue);
                            }
                        }
                        catch (Exception)
                        {
                            // Right now, for testing purposes, I just output and skip any issue
                            // TODO: Handle issues insteaf of skipping them, we need to find a common pattern
                        }
                    }
                }
            }
        }

        #endregion

        #region MapObjects

        /// <summary>
        /// Maps a source object, into a destination object
        /// </summary>
        /// <typeparam name="TDestination">The type of the destination object</typeparam>
        /// <param name="source">The source object</param>
        /// <param name="resolver">A custom resolver</param>
        /// <param name="resolverExpressions">Any custom resolver, optional</param>
        /// <param name="recursive">Defines whether to apply the mapping recursively, optional and by default false</param>
        /// <returns>The mapped destination object</returns>
        public static Object MapObjects<TDestination>(Object source, ITypeResolver resolver, Dictionary<Expression<Func<TDestination, Object>>, IResolver> resolverExpressions = null, Boolean recursive = false)
        {
            Dictionary<string, IResolver> resolvers = ConvertExpressionsToResolvers(resolverExpressions);
            return(MapObjects(source, resolver, resolvers, recursive));
        }
        
        /// <summary>
        /// Maps a source object, into a destination object
        /// </summary>
        /// <param name="source">The source object</param>
        /// <param name="resolver">A custom resolver</param>
        /// <param name="resolvers">Any custom resolver, optional</param>
        /// <param name="recursive">Defines whether to apply the mapping recursively, optional and by default false</param>
        /// <returns>The mapped destination object</returns>
        public static Object MapObjects(Object source, ITypeResolver resolver, Dictionary<String, IResolver> resolvers = null, Boolean recursive = false)
        {
            Object result = null;

            // Normalize the keys of the resolvers, if any
            if (null != resolver)
            {
                result = resolver.Resolve(source, resolvers, recursive);
            }

            return (result);
        }

        #endregion

        #region Utility methods

        /// <summary>
        /// Transforms a Dictionary of IValueResolver instances by Expression into a Dictionary by String (property name)
        /// </summary>
        /// <typeparam name="TTarget">The target Type of the expression</typeparam>
        /// <param name="resolverExpressions">The Dictionary to transform</param>
        /// <returns>The transformed dictionary</returns>
        private static Dictionary<String, IResolver> ConvertExpressionsToResolvers<TTarget>(Dictionary<Expression<Func<TTarget, object>>, IResolver> resolverExpressions)
        {
            Dictionary<String, IResolver> resolvers = null;

            if (resolverExpressions != null)
            {
                resolvers = new Dictionary<String, IResolver>();

                foreach (var re in resolverExpressions.Keys)
                {
                    var propertySelector = re.Body as MemberExpression ?? ((UnaryExpression)re.Body).Operand as MemberExpression;
                    resolvers.Add($"{propertySelector.Member.DeclaringType.FullName}.{propertySelector.Member.Name}".ToUpper(), resolverExpressions[re]);
                }
            }

            return resolvers;
        }

        #endregion
    }
}
