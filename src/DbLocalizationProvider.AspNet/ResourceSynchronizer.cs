﻿// Copyright (c) 2019 Valdis Iljuconoks.
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DbLocalizationProvider.Cache;
using DbLocalizationProvider.Internal;
using DbLocalizationProvider.Queries;

namespace DbLocalizationProvider.Sync
{
    public class ResourceSynchronizer
    {
        public void DiscoverAndRegister()
        {
            if(!ConfigurationContext.Current.DiscoverAndRegisterResources)
                return;

            var discoveredTypes = TypeDiscoveryHelper.GetTypes(t => t.GetCustomAttribute<LocalizedResourceAttribute>() != null,
                                                               t => t.GetCustomAttribute<LocalizedModelAttribute>() != null);

            var discoveredResources = discoveredTypes[0];
            var discoveredModels = discoveredTypes[1];
            var foreignResources = ConfigurationContext.Current.ForeignResources;
            if(foreignResources != null && foreignResources.Any())
            {
                discoveredResources.AddRange(foreignResources.Select(x => x.ResourceType));
            }

            // initialize db structures first (issue #53)
            if(ConfigurationContext.Current.Tenants.Any())
            {
                foreach(var t in ConfigurationContext.Current.Tenants)
                {
                    InitializeDatabaseStructures(t.ConnectionString);
                }
            }
            else
            {
                InitializeDatabaseStructures(ConfigurationContext.Current.DbContextConnectionString);
            }

            ResetSyncStatus();
            var allResources = new GetAllResources.Query(true).Execute();

            Parallel.Invoke(() => RegisterDiscoveredResources(discoveredResources, allResources),
                            () => RegisterDiscoveredResources(discoveredModels, allResources));

            StoreKnownResourcesAndPopulateCache();
        }

        private static void InitializeDatabaseStructures(string connectionString)
        {
            using (var ctx = new LanguageEntities(connectionString))
            {
                var tmp = ctx.LocalizationResources.FirstOrDefault();
            }
        }

        public void RegisterManually(IEnumerable<ManualResource> resources)
        {
            if(ConfigurationContext.Current.Tenants.Any())
            {
                foreach(var t in ConfigurationContext.Current.Tenants)
                {
                    RegisterManuallySingle(resources, t.ConnectionString);
                }
            }
            else
            {
                RegisterManuallySingle(resources, ConfigurationContext.Current.DbContextConnectionString);
            }
        }

        private void RegisterManuallySingle(IEnumerable<ManualResource> resources, string connectionString)
        {
            using (var db = new LanguageEntities(connectionString))
            {
                var defaultCulture = new DetermineDefaultCulture.Query().Execute();

                foreach (var resource in resources)
                    RegisterIfNotExist(db, resource.Key, resource.Translation, defaultCulture, "manual");

                db.SaveChanges();
            }
        }

        private void StoreKnownResourcesAndPopulateCache()
        {
            var allResources = new GetAllResources.Query(true).Execute();

            if(ConfigurationContext.Current.PopulateCacheOnStartup)
            {
                new ClearCache.Command().Execute();

                foreach(var resource in allResources)
                {
                    var key = CacheKeyHelper.BuildKey(resource.ResourceKey);
                    ConfigurationContext.Current.CacheManager.Insert(key, resource);
                }
            }
            else
            {
                // just store resource cache keys
                allResources.ForEach(r => ConfigurationContext.Current.BaseCacheManager.StoreKnownKey(r.ResourceKey));
            }
        }

        private void ResetSyncStatus()
        {
            if(ConfigurationContext.Current.Tenants.Any())
            {
                foreach(var t in ConfigurationContext.Current.Tenants)
                {
                    ResetSyncStatusExecute(t.ConnectionString);
                }
            }
            else
            {
                ResetSyncStatusExecute(ConfigurationContext.Current.DbContextConnectionString);
            }
        }

        private static void ResetSyncStatusExecute(string connectionString)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                var cmd = new SqlCommand("UPDATE dbo.LocalizationResources SET FromCode = 0", conn);

                conn.Open();
                cmd.ExecuteNonQuery();
                conn.Close();
            }
        }

        private void RegisterDiscoveredResources(IEnumerable<Type> types, IEnumerable<LocalizationResource> allResources)
        {
            var helper = new TypeDiscoveryHelper();
            var properties = types.SelectMany(type => helper.ScanResources(type)).DistinctBy(r => r.Key);

            // split work queue by 400 resources each
            var groupedProperties = properties.SplitByCount(400);

            Parallel.ForEach(groupedProperties,
                             group =>
                             {
                                 var sb = new StringBuilder();
                                 sb.AppendLine("declare @resourceId int");

                                 var refactoredResources = group.Where(r => !string.IsNullOrEmpty(r.OldResourceKey));
                                 foreach(var refactoredResource in refactoredResources)
                                 {
                                     sb.Append($@"
if exists(select 1 from LocalizationResources with(nolock) where ResourceKey = '{refactoredResource.OldResourceKey}')
begin
    update dbo.LocalizationResources set ResourceKey = '{refactoredResource.Key}', FromCode = 1 where ResourceKey = '{refactoredResource.OldResourceKey}'
end
");
                                 }

                                 foreach(var property in group)
                                 {
                                     var existingResource = allResources.FirstOrDefault(r => r.ResourceKey == property.Key);

                                     if(existingResource == null)
                                     {
                                         sb.Append($@"
set @resourceId = isnull((select id from LocalizationResources where [ResourceKey] = '{property.Key}'), -1)
if (@resourceId = -1)
begin
    insert into LocalizationResources ([ResourceKey], ModificationDate, Author, FromCode, IsModified, IsHidden)
    values ('{property.Key}', getutcdate(), 'type-scanner', 1, 0, {Convert.ToInt32(property.IsHidden)})
    set @resourceId = SCOPE_IDENTITY()");

                                         // add all translations
                                         foreach(var propertyTranslation in property.Translations)
                                         {
                                             sb.Append($@"
    insert into LocalizationResourceTranslations (ResourceId, [Language], [Value]) values (@resourceId, '{propertyTranslation.Culture}', N'{
                                                               propertyTranslation.Translation.Replace("'", "''")
                                                           }')
");
                                         }

                                         sb.Append(@"
end
");
                                     }

                                     if(existingResource != null)
                                     {
                                         sb.AppendLine($"update LocalizationResources set FromCode = 1, IsHidden = {Convert.ToInt32(property.IsHidden)} where [Id] = {existingResource.Id}");

                                         var invariantTranslation = property.Translations.First(t => t.Culture == string.Empty);
                                         sb.AppendLine($"update LocalizationResourceTranslations set [Value] = N'{invariantTranslation.Translation.Replace("'", "''")}' where ResourceId={existingResource.Id} and [Language]='{invariantTranslation.Culture}'");

                                         // check whether we do have Invariant language translation in database
                                         // TODO: update this to extension method once main library is updated (via git submodule)
                                         if(existingResource.Translations.FirstOrDefault(_ => _.Language == string.Empty) == null
                                            && property.Translations.FirstOrDefault(_ => _.Culture == string.Empty) != null)
                                         {
                                             // we don't have Invariant translation in database - must create one regardless of modified state of the resource
                                             AddTranslationScript(existingResource, sb, property.Translations.First(_ => _.Culture == string.Empty));
                                         }

                                         if(existingResource.IsModified.HasValue && !existingResource.IsModified.Value)
                                         {
                                             // process all other languages except Invariant
                                             foreach(var propertyTranslation in property.Translations.Where(_ => _.Culture != string.Empty))
                                                 AddTranslationScript(existingResource, sb, propertyTranslation);
                                         }
                                     }
                                 }

                                 if(ConfigurationContext.Current.Tenants.Any())
                                 {
                                     foreach(var t in ConfigurationContext.Current.Tenants)
                                     {
                                         RegisterDiscoveredResourcesExecute(t.ConnectionString, sb.ToString());
                                     }
                                 }
                                 else
                                 {
                                     RegisterDiscoveredResourcesExecute(ConfigurationContext.Current.DbContextConnectionString, sb.ToString());
                                 }
                             });
        }

        private static void RegisterDiscoveredResourcesExecute(string connectionString, string cmdText)
        {
            using (var conn =
                new SqlConnection(connectionString))
            {
                var cmd = new SqlCommand(cmdText, conn)
                {
                    CommandTimeout = 60
                };

                conn.Open();
                cmd.ExecuteNonQuery();
                conn.Close();
            }
        }

        private static void AddTranslationScript(LocalizationResource existingResource, StringBuilder buffer, DiscoveredTranslation resource)
        {
            var existingTranslation = existingResource.Translations.FirstOrDefault(t => t.Language == resource.Culture);
            if(existingTranslation == null)
            {
                buffer.Append($@"
insert into LocalizationResourceTranslations (ResourceId, [Language], [Value]) values ({existingResource.Id}, '{resource.Culture}', N'{resource.Translation.Replace("'", "''")}')
");
            }
            else if(!existingTranslation.Value.Equals(resource.Translation))
            {
                buffer.Append($@"
update LocalizationResourceTranslations set [Value] = N'{resource.Translation.Replace("'", "''")}' where ResourceId={existingResource.Id} and [Language]='{resource.Culture}'
");
            }
        }

        private void RegisterIfNotExist(LanguageEntities db, string resourceKey, string resourceValue, string defaultCulture, string author = "type-scanner")
        {
            var existingResource = db.LocalizationResources.Include(r => r.Translations).FirstOrDefault(r => r.ResourceKey == resourceKey);

            if(existingResource != null)
            {
                existingResource.FromCode = true;

                // if resource is not modified - we can sync default value from code
                if(existingResource.IsModified.HasValue && !existingResource.IsModified.Value)
                {
                    existingResource.ModificationDate = DateTime.UtcNow;
                    var defaultTranslation = existingResource.Translations.FirstOrDefault(t => t.Language == defaultCulture);
                    if(defaultTranslation != null)
                    {
                        defaultTranslation.Value = resourceValue;
                    }
                }

                var fromCodeTranslation = existingResource.Translations.FindByLanguage(CultureInfo.InvariantCulture);
                if(fromCodeTranslation != null)
                {
                    fromCodeTranslation.Value = resourceValue;
                }
                else
                {
                    fromCodeTranslation = new LocalizationResourceTranslation
                                          {
                                              Language = CultureInfo.InvariantCulture.Name,
                                              Value = resourceValue
                                          };

                    existingResource.Translations.Add(fromCodeTranslation);
                }
            }
            else
            {
                // create new resource
                var resource = new LocalizationResource(resourceKey)
                               {
                                   ModificationDate = DateTime.UtcNow,
                                   Author = author,
                                   FromCode = true,
                                   IsModified = false
                               };

                resource.Translations.Add(new LocalizationResourceTranslation
                                          {
                                              Language = defaultCulture,
                                              Value = resourceValue
                                          });

                resource.Translations.Add(new LocalizationResourceTranslation
                                          {
                                              Language = CultureInfo.InvariantCulture.Name,
                                              Value = resourceValue
                                          });

                db.LocalizationResources.Add(resource);
            }
        }
    }
}
