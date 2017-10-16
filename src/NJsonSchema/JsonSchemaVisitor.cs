﻿//-----------------------------------------------------------------------
// <copyright file="JsonSchemaVisitor.cs" company="NJsonSchema">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/rsuter/NJsonSchema/blob/master/LICENSE.md</license>
// <author>Rico Suter, mail@rsuter.com</author>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using NJsonSchema.Infrastructure;
using NJsonSchema.References;

namespace NJsonSchema
{
    /// <summary>Visitor to transform an object with <see cref="JsonSchema4"/> objects.</summary>
    public abstract class JsonSchemaVisitor
    {
        /// <summary>Processes an object.</summary>
        /// <param name="obj">The object to process.</param>
        /// <returns>The task.</returns>
        public virtual async Task VisitAsync(object obj)
        {
            await VisitAsync(obj, "#", null, new HashSet<object>(), o => throw new NotSupportedException("Cannot replace the root."));
        }

        /// <summary>Processes an object.</summary>
        /// <param name="obj">The object to process.</param>
        /// <param name="path">The path</param>
        /// <param name="typeNameHint">The type name hint.</param>
        /// <param name="checkedObjects">The checked objects.</param>
        /// <param name="replacer">The replacer.</param>
        /// <returns>The task.</returns>
        protected virtual async Task VisitAsync(object obj, string path, string typeNameHint, ISet<object> checkedObjects, Action<object> replacer)
        {
            if (obj == null || checkedObjects.Contains(obj))
                return;
            checkedObjects.Add(obj);

            if (obj is JsonSchema4 schema)
            {
                var newSchema = await VisitSchemaAsync(schema, path, typeNameHint);
                if (newSchema != schema)
                {
                    replacer(newSchema);
                    schema = newSchema;
                }

                var newReference = await VisitJsonReferenceAsync(schema, path, typeNameHint);
                if (newReference != schema)
                {
                    replacer(newReference);
                    schema = (JsonSchema4)newReference;
                }

                if (schema == null)
                    return;

                foreach (var p in schema.Definitions.ToArray())
                {
                    await VisitAsync(p.Value, path + "/definitions/" + p.Key, p.Key, checkedObjects, o =>
                    {
                        if (o != null)
                            schema.Definitions[p.Key] = (JsonSchema4)o;
                        else
                            schema.Definitions.Remove(p.Key);
                    });
                }

                if (schema.AdditionalItemsSchema != null)
                    await VisitAsync(schema.AdditionalItemsSchema, path + "/additionalItems", null, checkedObjects, o => schema.AdditionalItemsSchema = (JsonSchema4)o);

                if (schema.AdditionalPropertiesSchema != null)
                    await VisitAsync(schema.AdditionalPropertiesSchema, path + "/additionalProperties", null, checkedObjects, o => schema.AdditionalPropertiesSchema = (JsonSchema4)o);

                if (schema.Item != null)
                    await VisitAsync(schema.Item, path + "/items", null, checkedObjects, o => schema.Item = (JsonSchema4)o);

                for (var i = 0; i < schema.Items.Count; i++)
                {
                    var index = i;
                    await VisitAsync(schema.Items.ElementAt(i), path + "/items[" + i + "]", null, checkedObjects, o => ReplaceOrDelete(schema.Items, index, (JsonSchema4)o));
                }

                for (var i = 0; i < schema.AllOf.Count; i++)
                {
                    var index = i;
                    await VisitAsync(schema.AllOf.ElementAt(i), path + "/allOf[" + i + "]", null, checkedObjects, o => ReplaceOrDelete(schema.AllOf, index, (JsonSchema4)o));
                }

                for (var i = 0; i < schema.AnyOf.Count; i++)
                {
                    var index = i;
                    await VisitAsync(schema.AnyOf.ElementAt(i), path + "/anyOf[" + i + "]", null, checkedObjects, o => ReplaceOrDelete(schema.AnyOf, index, (JsonSchema4)o));
                }

                for (var i = 0; i < schema.OneOf.Count; i++)
                {
                    var index = i;
                    await VisitAsync(schema.OneOf.ElementAt(i), path + "/oneOf[" + i + "]", null, checkedObjects, o => ReplaceOrDelete(schema.OneOf, index, (JsonSchema4)o));
                }

                if (schema.Not != null)
                    await VisitAsync(schema.Not, path + "/not", null, checkedObjects, o => schema.Not = (JsonSchema4)o);

                foreach (var p in schema.Properties.ToArray())
                    await VisitAsync(p.Value, path + "/properties/" + p.Key, p.Key, checkedObjects, o => schema.Properties[p.Key] = (JsonProperty)o);

                foreach (var p in schema.PatternProperties.ToArray())
                    await VisitAsync(p.Value, path + "/patternProperties/" + p.Key, null, checkedObjects, o => schema.PatternProperties[p.Key] = (JsonProperty)o);
            }
            else
            {
                if (obj is IJsonReference reference)
                {
                    var newReference = await VisitJsonReferenceAsync(reference, path, typeNameHint);
                    if (newReference != reference)
                    {
                        replacer(newReference);
                        obj = newReference;
                    }

                    if (obj == null)
                        return;
                }

                if (!(obj is string))
                {
                    // Reflection fallback
                    if (obj is IDictionary dictionary)
                    {
                        foreach (var key in dictionary.Keys.OfType<object>().ToArray())
                        {
                            await VisitAsync(dictionary[key], path + "/" + key, key.ToString(), checkedObjects, o =>
                            {
                                if (o != null)
                                    dictionary[key] = (JsonSchema4)o;
                                else
                                    dictionary.Remove(key);
                            });
                        }
                    }
                    else if (obj is IList list)
                    {
                        var items = list.OfType<object>().ToArray();
                        for (var i = 0; i < items.Length; i++)
                        {
                            var index = i;
                            await VisitAsync(items[i], path + "[" + i + "]", null, checkedObjects, o => ReplaceOrDelete(list, index, o));
                        }
                    }
                    else if (obj is IEnumerable enumerable)
                    {
                        var items = enumerable.OfType<object>().ToArray();
                        for (var i = 0; i < items.Length; i++)
                            await VisitAsync(items[i], path + "[" + i + "]", null, checkedObjects, o => throw new NotSupportedException("Cannot replace enumerable item."));
                    }
                    else
                    {
                        foreach (var member in ReflectionCache.GetPropertiesAndFields(obj.GetType()))
                        {
                            var value = member.GetValue(obj);
                            if (value != null)
                                await VisitAsync(value, path + "/" + member.GetName(), member.GetName(), checkedObjects, o => member.SetValue(obj, o));
                        }
                    }
                }
            }
        }

        /// <summary>Called when a <see cref="JsonSchema4"/> is visited.</summary>
        /// <param name="schema">The visited schema.</param>
        /// <param name="path">The path.</param>
        /// <param name="typeNameHint">The type name hint.</param>
        /// <returns>The task.</returns>
#pragma warning disable 1998
        protected virtual async Task<JsonSchema4> VisitSchemaAsync(JsonSchema4 schema, string path, string typeNameHint)
#pragma warning restore 1998
        {
            return schema;
        }

        /// <summary>Called when a <see cref="IJsonReference"/> is visited.</summary>
        /// <param name="reference">The visited schema.</param>
        /// <param name="path">The path.</param>
        /// <param name="typeNameHint">The type name hint.</param>
        /// <returns>The task.</returns>
#pragma warning disable 1998
        protected virtual async Task<IJsonReference> VisitJsonReferenceAsync(IJsonReference reference, string path, string typeNameHint)
#pragma warning restore 1998
        {
            return reference;
        }

        private void ReplaceOrDelete<T>(ICollection<T> collection, int index, T obj)
        {
            ((Collection<T>)collection).RemoveAt(index);
            if (obj != null)
                ((Collection<T>)collection).Insert(index, obj);
        }

        private void ReplaceOrDelete(IList collection, int index, object obj)
        {
            collection.RemoveAt(index);
            if (obj != null)
                collection.Insert(index, obj);
        }
    }
}