﻿using Nemo.Attributes;
using Nemo.Configuration;
using Nemo.Id;
using Nemo.Reflection;
using Nemo.Serialization;
using Nemo.UnitOfWork;
using Nemo.Validation;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Nemo.Logging;
using Nemo.Utilities;

namespace Nemo.Extensions
{
    /// <summary>
    /// Extension methods for each DataEntity implementation to provide default ActiveRecord functionality.
    /// </summary>
    public static class ObjectExtensions
    {
        #region Property Accessor

        /// <summary>
        /// Property method returns a value of a property.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="dataEntity"></param>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        public static TResult Property<T, TResult>(this T dataEntity, string propertyName)
            where T : class
        {
            var reflectedType = Reflector.GetReflectedType(dataEntity.GetType());
            if (typeof(T) == typeof(object) && reflectedType.IsEmitted && reflectedType.InterfaceTypeName != null)
            {
                return (TResult)Reflector.Property.Get(reflectedType.InterfaceType, dataEntity, propertyName);
            }

            if (reflectedType.IsMarkerInterface || typeof(T) == typeof(object))
            {
                return (TResult)Reflector.Property.Get(reflectedType.UnderlyingType, dataEntity, propertyName);
            }
            
            return (TResult)Reflector.Property.Get(dataEntity, propertyName);
        }

        /// <summary>
        /// Property method returns a value of a property.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="dataEntity"></param>
        /// <param name="propertyName"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        public static TResult PropertyOrDefault<T, TResult>(this T dataEntity, string propertyName, TResult defaultValue)
            where T : class
        {
            var result = dataEntity.Property(propertyName);
            return result != null ? (TResult)result : defaultValue;
        }

        /// <summary>
        /// Property method returns a value of a property.
        /// </summary>
        /// <param name="dataEntity"></param>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        public static object Property<T>(this T dataEntity, string propertyName)
            where T : class
        {
            return dataEntity.Property<T, object>(propertyName);
        }

        /// <summary>
        /// Property method sets a value of a property.
        /// </summary>
        /// <param name="dataEntity"></param>
        /// <param name="propertyName"></param>
        /// <param name="propertyValue"></param>
        public static void Property<T>(this T dataEntity, string propertyName, object propertyValue)
            where T : class
        {
            var reflectedType = Reflector.GetReflectedType(dataEntity.GetType());
            if (typeof(T) == typeof(object) && reflectedType.IsEmitted && reflectedType.InterfaceTypeName != null)
            {
                Reflector.Property.Set(reflectedType.InterfaceType, dataEntity, propertyName, propertyValue);
            }
            else if (reflectedType.IsMarkerInterface || typeof(T) == typeof(object))
            {
                Reflector.Property.Set(reflectedType.UnderlyingType, dataEntity, propertyName, propertyValue);
            }
            else
            {
                Reflector.Property.Set(dataEntity, propertyName, propertyValue);
            }
        }

        /// <summary>
        /// PropertyExists method verifies if the property has value.
        /// </summary>
        /// <param name="dataEntity"></param>
        /// <param name="propertyName"></param>
        public static bool PropertyExists<T>(this T dataEntity, string propertyName)
            where T : class
        {
            object value;
            return dataEntity.PropertyTryGet(propertyName, out value);
        }

        /// <summary>
        /// PropertyTryGet method verifies if the property has value and returns the value.
        /// </summary>
        /// <param name="dataEntity"></param>
        /// <param name="propertyName"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static bool PropertyTryGet<T>(this T dataEntity, string propertyName, out object value)
            where T : class
        {
            var exists = false;
            value = null;
            try
            {
                value = dataEntity.Property(propertyName);
                exists = true;
            }
            catch { }
            return exists;
        }

        #endregion

        #region CRUD Methods

        /// <summary>
        /// Populate method provides an ability to populate an object by primary key.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataEntity"></param>
        /// <param name="config"></param>
        public static void Load<T>(this T dataEntity, IConfiguration config)
            where T : class
        {
            var parameters = GetLoadParameters(dataEntity);

            var retrievedObject = ObjectFactory.Retrieve<T>(parameters: parameters, config: config).FirstOrDefault();

            HandleLoad(dataEntity, retrievedObject, config);
        }

        public static void Load<T>(this T dataEntity)
            where T : class
        {
            dataEntity.Load(ConfigurationFactory.Get<T>());
        }

        public static async Task LoadAsync<T>(this T dataEntity, IConfiguration config)
            where T : class
        {
            var parameters = GetLoadParameters(dataEntity);

            var retrievedObject = (await ObjectFactory.RetrieveAsync<T>(parameters: parameters, config: config).ConfigureAwait(false)).FirstOrDefault();

            HandleLoad(dataEntity, retrievedObject, config);
        }

        public static Task LoadAsync<T>(this T dataEntity)
            where T : class
        {
            return dataEntity.LoadAsync(ConfigurationFactory.Get<T>());
        }

        private static Param[] GetLoadParameters<T>(T dataEntity)
            where T : class
        {
            dataEntity.ThrowIfNull("dataEntity");
            dataEntity.CheckReadOnly();

            // Get properties and build a property map
            var propertyMap = Reflector.GetPropertyMap<T>();

            // Convert readable primary key properties to rule parameters
            var parameters = propertyMap.Values
                .Where(p => p.CanRead && p.IsPrimaryKey)
                .Select(p => new Param
                {
                    Name = p.ParameterName ?? p.PropertyName,
                    Value = dataEntity.Property(p.PropertyName),
                    Direction = ParameterDirection.Input
                }).ToArray();

            return parameters;
        }

        private static void HandleLoad<T>(T dataEntity, T retrievedObject, IConfiguration config)
            where T : class
        {
            if (retrievedObject == null) return;
            config ??= ConfigurationFactory.Get<T>();
            ObjectFactory.Map(retrievedObject, dataEntity, config.AutoTypeCoercion);
            ObjectFactory.TrySetObjectState(dataEntity);
        }

        /// <summary>
        /// Insert method provides an ability to insert an object to the underlying data store.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataEntity"></param>
        /// <param name="config"></param>
        /// <param name="additionalParameters"></param>
        /// <returns></returns>
        public static bool Insert<T>(this T dataEntity, IConfiguration config, params Param[] additionalParameters)
            where T : class
        {
            GetInsertParameters(dataEntity, config, additionalParameters, out var propertyMap, out var identityProperty, out var parameters);

            var response = ObjectFactory.Insert<T>(parameters, config: config);

            return HandleInsert(dataEntity, parameters, identityProperty, propertyMap, response, config);
        }

        public static bool Insert<T>(this T dataEntity, params Param[] additionalParameters)
            where T : class
        {
            return dataEntity.Insert(ConfigurationFactory.Get<T>(), additionalParameters);
        }

        public static async Task<bool> InsertAsync<T>(this T dataEntity, IConfiguration config, params Param[] additionalParameters)
            where T : class
        {
            GetInsertParameters(dataEntity, config, additionalParameters, out var propertyMap, out var identityProperty, out var parameters);
            
            var response = await ObjectFactory.InsertAsync<T>(parameters, config: config).ConfigureAwait(false);

            return HandleInsert(dataEntity, parameters, identityProperty, propertyMap, response, config);
        }

        public static Task<bool> InsertAsync<T>(this T dataEntity, params Param[] additionalParameters)
            where T : class
        {
            return dataEntity.InsertAsync(ConfigurationFactory.Get<T>(), additionalParameters);
        }

        private static void GetInsertParameters<T>(T dataEntity, IConfiguration config, Param[] additionalParameters, out IDictionary<PropertyInfo, ReflectedProperty> propertyMap, out PropertyInfo identityProperty, out Param[] parameters)
            where T : class
        {
            dataEntity.ThrowIfNull("dataEntity");
            dataEntity.CheckReadOnly();

            // Validate an object before persisting
            var errors = dataEntity.Validate();
            if (errors.Any())
            {
                throw new ValidationException(errors);
            }

            // Get properties and build a property map
            propertyMap = Reflector.GetPropertyMap<T>();

            identityProperty = propertyMap
                .Where(p => p.Key.CanWrite && p.Value != null && p.Value.IsAutoGenerated)
                .Select(p => p.Key)
                .FirstOrDefault();
           
            // Generate key if primary key value was not set and no identity (autogenerated) property is defined
            if (identityProperty == null && dataEntity.IsNew())
            {
                dataEntity.GenerateKey(config);
            }

            parameters = GetInsertParameters(dataEntity, propertyMap);

            if (additionalParameters != null && additionalParameters.Length > 0)
            {
                var tempParameters = additionalParameters.GroupJoin(parameters, p => p.Name, p => p.Name, (a, p) => p.Any() ? p.First() : a).ToArray();
                parameters = tempParameters;
            }
        }

        private static bool HandleInsert<T>(T dataEntity, Param[] parameters, PropertyInfo identityProperty, IDictionary<PropertyInfo, ReflectedProperty> propertyMap, OperationResponse response, IConfiguration config)
            where T : class
        {
            var success = response != null && response.RecordsAffected > 0;

            if (!success)
            {
                return false;
            }

            if (identityProperty != null)
            {
                var identityValue = parameters.Single(p => p.Name == identityProperty.Name).Value;
                if (identityValue != null && !Convert.IsDBNull(identityValue))
                {
                    Reflector.Property.Set(dataEntity, identityProperty.Name, identityValue);
                }
            }

            Identity.Get<T>(config).Set(dataEntity);

            var outputProperties = propertyMap
                .Where(p => p.Key.CanWrite && p.Value != null
                            && (p.Value.Direction == ParameterDirection.InputOutput
                                || p.Value.Direction == ParameterDirection.Output))
                .Select(p => p.Key);

            SetOutputParameterValues(dataEntity, outputProperties, propertyMap, parameters);

            ObjectFactory.TrySetObjectState(dataEntity);

            if (!(dataEntity is IAuditableDataEntity)) return true;

            var logProvider = ConfigurationFactory.Get<T>().AuditLogProvider;
            if (logProvider != null)
            {
                logProvider.Write(new AuditLog<T>(ObjectFactory.OperationInsert, default(T), dataEntity));
            }

            return true;
        }

        /// <summary>
        ///  Update method provides an ability to update an object in the underlying data store.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataEntity"></param>
        /// <param name="config"></param>
        /// <param name="additionalParameters"></param>
        /// <returns></returns>
        public static bool Update<T>(this T dataEntity, IConfiguration config, params Param[] additionalParameters)
            where T : class
        {
            var supportsChangeTracking = GetUpdateParameters(dataEntity, additionalParameters, out var propertyMap, out var outputProperties, out var parameters);

            var response = ObjectFactory.Update<T>(parameters, config: config);

            return HandleUpdate(dataEntity, outputProperties, propertyMap, parameters, supportsChangeTracking, response, config);
        }

        public static bool Update<T>(this T dataEntity, params Param[] additionalParameters)
            where T : class
        {
            return dataEntity.Update(ConfigurationFactory.Get<T>(), additionalParameters);
        }

        public static async Task<bool> UpdateAsync<T>(this T dataEntity, IConfiguration config, params Param[] additionalParameters)
            where T : class
        {
            var supportsChangeTracking = GetUpdateParameters(dataEntity, additionalParameters, out var propertyMap, out var outputProperties, out var parameters);

            var response = await ObjectFactory.UpdateAsync<T>(parameters, config: config).ConfigureAwait(false);

            return HandleUpdate(dataEntity, outputProperties, propertyMap, parameters, supportsChangeTracking, response, config);
        }

        public static Task<bool> UpdateAsync<T>(this T dataEntity, params Param[] additionalParameters)
            where T : class
        {
            return dataEntity.UpdateAsync(ConfigurationFactory.Get<T>(), additionalParameters);
        }

        private static bool GetUpdateParameters<T>(T dataEntity, Param[] additionalParameters, out IDictionary<PropertyInfo, ReflectedProperty> propertyMap, out IEnumerable<PropertyInfo> outputProperties, out Param[] parameters) where T : class
        {
            dataEntity.ThrowIfNull("dataEntity");
            dataEntity.CheckReadOnly();

            // Validate an object before persisting
            var errors = dataEntity.Validate();
            if (errors.Any())
            {
                throw new ValidationException(errors);
            }

            var supportsChangeTracking = dataEntity is ITrackableDataEntity;
            if (supportsChangeTracking && ((ITrackableDataEntity)dataEntity).IsReadOnly())
            {
                throw new ApplicationException("Update Failed: provided object is read-only.");
            }

            // Get properties and build a property map
            var internalPropertyMap = Reflector.GetPropertyMap<T>();
            outputProperties = internalPropertyMap.Keys
                .Where(p => p.CanWrite && internalPropertyMap[p] != null
                            && (internalPropertyMap[p].Direction == ParameterDirection.InputOutput
                                || internalPropertyMap[p].Direction == ParameterDirection.Output));

            propertyMap = internalPropertyMap;

            parameters = GetUpdateParameters(dataEntity, internalPropertyMap);

            if (additionalParameters != null && additionalParameters.Length > 0)
            {
                var tempParameters = additionalParameters.GroupJoin(parameters, p => p.Name, p => p.Name, (a, p) => p.Any() ? p.First() : a).ToArray();
                parameters = tempParameters;
            }
            return supportsChangeTracking;
        }

        private static bool HandleUpdate<T>(T dataEntity, IEnumerable<PropertyInfo> outputProperties, IDictionary<PropertyInfo, ReflectedProperty> propertyMap, Param[] parameters, bool supportsChangeTracking, OperationResponse response, IConfiguration config) 
            where T : class
        {
            var success = response != null && response.RecordsAffected > 0;

            if (!success)
            {
                return false;
            }

            Identity.Get<T>(config).Set(dataEntity);

            SetOutputParameterValues(dataEntity, outputProperties, propertyMap, parameters);

            if (supportsChangeTracking)
            {
                ((ITrackableDataEntity)dataEntity).ObjectState = ObjectState.Clean;
            }

            if (!(dataEntity is IAuditableDataEntity)) return true;

            var logProvider = ConfigurationFactory.Get<T>().AuditLogProvider;
            if (logProvider != null)
            {
                logProvider.Write(new AuditLog<T>(ObjectFactory.OperationUpdate, (dataEntity.Old() ?? dataEntity), dataEntity));
            }

            return true;
        }

        /// <summary>
        /// Delete method provides an ability to soft-delete an object from the underlying data store.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataEntity"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        public static bool Delete<T>(this T dataEntity, IConfiguration config)
            where T : class
        {
            var parameters = GetDeleteParameters(dataEntity);

            var response = ObjectFactory.Delete<T>(parameters, config: config);

            return HandleDelete(dataEntity, response, false, config);
        }

        public static bool Delete<T>(this T dataEntity)
            where T : class
        {
            return dataEntity.Delete(ConfigurationFactory.Get<T>());
        }

        public static async Task<bool> DeleteAsync<T>(this T dataEntity, IConfiguration config)
            where T : class
        {
            var parameters = GetDeleteParameters(dataEntity);

            var response = await ObjectFactory.DeleteAsync<T>(parameters, config: config).ConfigureAwait(false);

            return HandleDelete(dataEntity, response, false, config);
        }

        public static Task<bool> DeleteAsync<T>(this T dataEntity)
            where T : class
        {
            return dataEntity.DeleteAsync(ConfigurationFactory.Get<T>());
        }

        /// <summary>
        /// Destroy method provides an ability to hard-delete an object from the underlying data store.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataEntity"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        public static bool Destroy<T>(this T dataEntity, IConfiguration config)
            where T : class
        {
            var parameters = GetDeleteParameters(dataEntity);

            var response = ObjectFactory.Destroy<T>(parameters, config: config);

            return HandleDelete(dataEntity, response, true, config);
        }

        public static bool Destroy<T>(this T dataEntity)
            where T : class
        {
            return dataEntity.Destroy(ConfigurationFactory.Get<T>());
        }

        public static async Task<bool> DestroyAsync<T>(this T dataEntity, IConfiguration config)
            where T : class
        {
            var parameters = GetDeleteParameters(dataEntity);

            var response = await ObjectFactory.DestroyAsync<T>(parameters, config: config).ConfigureAwait(false);

            return HandleDelete(dataEntity, response, true, config);
        }

        public static Task<bool> DestroyAsync<T>(this T dataEntity)
            where T : class
        {
            return dataEntity.DestroyAsync(ConfigurationFactory.Get<T>());
        }

        private static Param[] GetDeleteParameters<T>(T dataEntity)
            where T : class
        {
            dataEntity.ThrowIfNull("dataEntity");
            dataEntity.CheckReadOnly();

            // Get properties and build a property map
            var propertyMap = Reflector.GetPropertyMap<T>();

            return GetDeleteParameters(dataEntity, propertyMap);
        }

        private static bool HandleDelete<T>(T dataEntity, OperationResponse response, bool destroy, IConfiguration config)
            where T : class
        {
            var success = response != null && response.RecordsAffected > 0;

            if (!success)
            {
                return false;
            }

            Identity.Get<T>(config).Remove(dataEntity);

            ObjectFactory.TrySetObjectState(dataEntity, ObjectState.Deleted);

            if (!(dataEntity is IAuditableDataEntity)) return true;

            var logProvider = ConfigurationFactory.Get<T>().AuditLogProvider;
            if (logProvider != null)
            {
                logProvider.Write(new AuditLog<T>(destroy ? ObjectFactory.OperationDestroy : ObjectFactory.OperationDelete, dataEntity, default(T)));
            }

            return true;
        }

        public static void Attach<T>(this T dataEntity)
            where T : class
        {
            dataEntity.Attach(ConfigurationFactory.Get<T>());
        }

        public static void Attach<T>(this T dataEntity, IConfiguration config)
            where T : class
        {
            Identity.Get<T>(config).Set(dataEntity);
        }

        public static void Detach<T>(this T dataEntity)
            where T : class
        {
            dataEntity.Detach(ConfigurationFactory.Get<T>());
        }

        public static void Detach<T>(this T dataEntity, IConfiguration config)
            where T : class
        {
            Identity.Get<T>(config).Remove(dataEntity);
        }

        #region ITrackableDataEntity Methods

        public static bool Save<T>(this T dataEntity)
            where T : class
        {
            var result = false;
            
            if (dataEntity.IsNew())
            {
                result = dataEntity.Insert();
            }
            else if (!(dataEntity is ITrackableDataEntity entity) || entity.IsDirty())
            {
                result = dataEntity.Update();
            }

            return result;
        }

        public static bool IsNew<T>(this T dataEntity)
            where T : class
        {
            var entity = dataEntity as ITrackableDataEntity;
            if (entity != null)
            {
                return entity.ObjectState == ObjectState.New;
            }
            var primaryKey = dataEntity.GetPrimaryKey();
            return primaryKey.Values.Sum(v => v == null || v == v.GetType().GetDefault() ? 1 : 0) == primaryKey.Values.Count;
        }

        public static bool IsReadOnly<T>(this T dataEntity)
           where T : class
        {
            var entity = dataEntity as ITrackableDataEntity;
            if (entity != null)
            {
                return entity.ObjectState == ObjectState.ReadOnly;
            }
            return Reflector.GetAttribute<ReadOnlyAttribute>(dataEntity.GetType()) != null;
        }

        public static bool IsDirty<T>(this T dataEntity)
           where T : class
        {
            var entity = dataEntity as ITrackableDataEntity;
            if (entity != null)
            {
                return entity.ObjectState == ObjectState.Dirty;
            }
            return false;
        }

        public static bool IsDeleted<T>(this T dataEntity)
           where T : class
        {
            var entity = dataEntity as ITrackableDataEntity;
            if (entity != null)
            {
                return entity.ObjectState == ObjectState.Deleted;
            }
            return false;
        }

        #endregion

        #region Parameter Methods

        internal static Param[] GetInsertParameters(object dataEntity, IDictionary<PropertyInfo, ReflectedProperty> propertyMap, int statementId = -1)
        {
            var parameters = propertyMap.Values
                            .Where(p => p.IsPersistent && (p.IsSimpleType || p.IsSimpleList) && (p.CanWrite || p.IsAutoGenerated))
                            .Select(p => new Param
                            {
                                Name = (p.ParameterName.NullIfEmpty() ?? p.PropertyName) + (statementId == -1 ? string.Empty : "_" + statementId),
                                Value = GetParameterValue(dataEntity, p),
                                DbType = Reflector.ClrToDbType(p.PropertyType),
                                Direction = p.IsAutoGenerated ? ParameterDirection.Output : p.Direction,
                                Source = p.MappedColumnName,
                                IsAutoGenerated = p.IsAutoGenerated,
                                IsPrimaryKey = p.IsPrimaryKey
                            });
            return parameters.ToArray();
        }

        internal static Param[] GetUpdateParameters(object dataEntity, IDictionary<PropertyInfo, ReflectedProperty> propertyMap, int statementId = -1)
        {
            var parameters = propertyMap.Values
                    .Where(p => p.IsPersistent && (p.IsSimpleType || p.IsSimpleList) && (p.CanWrite || p.IsAutoGenerated))
                    .Select(p => new Param
                    {
                        Name = (p.ParameterName.NullIfEmpty() ?? p.PropertyName) + (statementId == -1 ? string.Empty : "_" + statementId),
                        Value = GetParameterValue(dataEntity, p),
                        DbType = Reflector.ClrToDbType(p.PropertyType),
                        Direction = p.Direction,
                        Source = p.MappedColumnName,
                        IsAutoGenerated = p.IsAutoGenerated,
                        IsPrimaryKey = p.IsPrimaryKey
                    });

            return parameters.ToArray();
        }

        internal static Param[] GetDeleteParameters(object dataEntity, IDictionary<PropertyInfo, ReflectedProperty> propertyMap, int statementId = -1)
        {
            var parameters = propertyMap.Values
                    .Where(p => p.CanRead && p.IsPrimaryKey)
                    .Select(p => new Param
                    {
                        Name = (p.ParameterName.NullIfEmpty() ?? p.PropertyName) + (statementId == -1 ? string.Empty : "_" + statementId),
                        Value = GetParameterValue(dataEntity, p),
                        Source = p.MappedColumnName,
                        IsPrimaryKey = true
                    });

            return parameters.ToArray();
        }

        private static object GetParameterValue(object dataEntity, ReflectedProperty property)
        {
            var result = Reflector.Property.Get(dataEntity.GetType(), dataEntity, property.PropertyName);
            if (result != null && property.IsSimpleList && property.ElementType != typeof(byte))
            {
                result = ((IEnumerable)result).SafeCast<string>().ToDelimitedString(",");
            }
            return result;
        }

        private static void SetOutputParameterValues<T>(T dataEntity, IEnumerable<PropertyInfo> outputProperties, IDictionary<PropertyInfo, ReflectedProperty> propertyMap, IList<Param> parameters)
        {
            var parameterMap = parameters.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First().Value);

            // Set output parameter values
            foreach (var outputProperty in outputProperties)
            {
                string outputPropertyName;
                if (propertyMap[outputProperty] != null && !string.IsNullOrEmpty(propertyMap[outputProperty].ParameterName))
                {
                    outputPropertyName = propertyMap[outputProperty].ParameterName;
                }
                else
                {
                    outputPropertyName = outputProperty.Name;
                }

                object outputPropertyValue;
                if (parameterMap.TryGetValue(outputPropertyName, out outputPropertyValue) && !Convert.IsDBNull(outputPropertyValue))
                {
                    Reflector.Property.Set(dataEntity, outputProperty.Name, outputPropertyValue);
                }
            }
        }

        #endregion

        #endregion

        #region Hash/ID Generation Methods

        private static readonly ConcurrentDictionary<Type, string[]> _primaryAndCacheKeys = new ConcurrentDictionary<Type, string[]>();
        private static readonly ConcurrentDictionary<Tuple<Type, PropertyInfo, Type>, IIdGenerator> _idGenerators = new ConcurrentDictionary<Tuple<Type, PropertyInfo, Type>, IIdGenerator>();
        
        /// <summary>
        /// GetPrimaryKey method returns primary key of a business object (if available)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataEntity"></param>
        /// <returns></returns>
        public static IDictionary<string, object> GetPrimaryKey<T>(this T dataEntity)
            where T : class
        {
            // Get properties and build a property map
            var interfaceType = typeof(T);
            if (interfaceType == typeof(object) && Reflector.IsEmitted(dataEntity.GetType()))
            {
                interfaceType = Reflector.GetInterface(dataEntity.GetType());
            }
            else if (Reflector.IsMarkerInterface<T>() || interfaceType == typeof(object))
            {
                interfaceType = dataEntity.GetType();
            }

            var primaryKeyProperties = _primaryAndCacheKeys.GetOrAdd(interfaceType, ObjectFactory.GetPrimaryKeyProperties) ?? new string[] { };

            var primaryKey = new SortedDictionary<string, object>();

            for (var i = 0; i < primaryKeyProperties.Length; i++)
            {
                var value = dataEntity.Property(primaryKeyProperties[i]);
                primaryKey[primaryKeyProperties[i]] = value;
            }

            return primaryKey;
        }

        public static void GenerateKey<T>(this T dataEntity, IConfiguration config = null)
            where T : class
        {
            var propertyMap = Reflector.GetPropertyMap<T>();
            var generatorKeys = propertyMap.Where(p => p.Value != null && p.Value.Generator != null).Select(p => Tuple.Create(typeof(T), p.Key, p.Value.Generator));
            foreach (var key in generatorKeys)
            {
                var generator = _idGenerators.GetOrAdd(key, k => (IIdGenerator)(k.Item3 == typeof(HiLoGenerator) ? k.Item3.New(k.Item1, k.Item2, config) : k.Item3.New()));

                dataEntity.Property(key.Item2.Name, generator.Generate());
            }
        }

        internal static void GenerateKeys<T>(this IList<T> dataEntities, IConfiguration config)
            where T : class
        {
            var propertyMap = Reflector.GetPropertyMap<T>();
            var generatorKeys = propertyMap.Where(p => p.Value != null && p.Value.Generator != null).Select(p => Tuple.Create(typeof(T), p.Key, p.Value.Generator));
            foreach (var key in generatorKeys)
            {
                var generator = _idGenerators.GetOrAdd(key, k => (IIdGenerator)(k.Item3 == typeof(HiLoGenerator) ? k.Item3.New(k.Item1, k.Item2, config) : k.Item3.New()));

                foreach (var dataEntity in dataEntities.Where(e => e.IsNew()))
                {
                    dataEntity.Property(key.Item2.Name, generator.Generate());
                }
            }
        }

        public static string ComputeHash<T>(this T dataEntity)
            where T : class
        {
            var hash = Hash.Compute(Encoding.UTF8.GetBytes(dataEntity.GetPrimaryKey().Select(p => $"{p.Key}={p.Value}").ToDelimitedString(",")));
            var type = typeof(T);
            if (type == typeof(object) && Reflector.IsEmitted(dataEntity.GetType()))
            {
                type = Reflector.GetInterface(dataEntity.GetType());
            }
            else if (Reflector.IsMarkerInterface<T>())
            {
                type = dataEntity.GetType();
            }
            return type.FullName + "/" + hash;
        }

        internal static string ComputeHash(this SortedDictionary<string, object> values, Type objectType)
        {
            var hash = Hash.Compute(Encoding.UTF8.GetBytes(values.Select(p => $"{p.Key}={p.Value}").ToDelimitedString(",")));
            if (objectType == typeof(object) && Reflector.IsEmitted(objectType))
            {
                objectType = Reflector.GetInterface(objectType);
            }
            return objectType.FullName + "/" + hash;
        }

        internal static Func<SortedDictionary<string, object>> GetKeySelector(this DataRow row, string[] primaryKey)
        {
            return () => new SortedDictionary<string, object>(primaryKey.ToDictionary(k => k, k => row[k]));
        }

        internal static Func<SortedDictionary<string, object>> GetKeySelector(this object item, string[] propertyKey)
        {
            return () => new SortedDictionary<string, object>(propertyKey.ToDictionary(k => k, k => item.Property(k)));
        }

        #endregion

        #region ReadOnly Methods

        public static T AsReadOnly<T>(this T dataEntity)
            where T : class
        {
            return dataEntity == null ? null : Adapter.Guard(dataEntity);
        }

        public static List<T> AsReadOnly<T>(this List<T> dataEntities)
            where T : class
        {
            return dataEntities == null ? null : dataEntities.Select(b => b.AsReadOnly()).ToList();
        }

        public static IList<T> AsReadOnly<T>(this IList<T> dataEntities)
            where T : class
        {
            return dataEntities == null ? null : dataEntities.Select(b => b.AsReadOnly()).ToArray();
        }

        internal static void CheckReadOnly<T>(this T dataEntity)
            where T : class
        {
            // Read-only objects can't participate in CRUD
            if (dataEntity.IsReadOnly())
            {
                throw new NotSupportedException("Operation is not allowed: object instance is read-only.");
            }
        }

        #endregion

        #region Clone Methods

        /// <summary>
        /// Creates a deep copy of the interface instance. 
        /// NOTE: The object must be serializable.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="instance"></param>
        /// <returns></returns>
        public static T Clone<T>(this T instance)
            where T : class
        {
            var data = instance.Serialize(SerializationMode.SerializeAll);
            var value = data.Deserialize<T>();
            return value;
        }

        /// <summary>
        /// Creates a deep copy of the collection of interface instances. 
        /// NOTE: The object must be serializable.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IEnumerable<T> Clone<T>(this IEnumerable<T> collection)
            where T : class
        {
            var data = collection.Serialize(SerializationMode.SerializeAll);
            var value = data.Deserialize<T>();
            return value;
        }

        #endregion
    }
}
