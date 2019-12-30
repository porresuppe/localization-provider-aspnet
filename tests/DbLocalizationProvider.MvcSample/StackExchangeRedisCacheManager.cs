using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Web;
using DbLocalizationProvider.Cache;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace DbLocalizationProvider.MvcSample
{
    public class StackExchangeRedisCacheManager : ICacheManager
    {
        private readonly ConnectionMultiplexer _redis;

        public StackExchangeRedisCacheManager(string localhost)
        {
            _redis = ConnectionMultiplexer.Connect(localhost);
        }

        public void Insert(string key, object value)
        {
            var db = _redis.GetDatabase();
            db.StringSet(key, ToBytes(value));
        }

        public object Get(string key)
        {
            var db = _redis.GetDatabase();
            return Get<object>(db, key);
        }

        public void Remove(string key)
        {
            var db = _redis.GetDatabase();
            db.KeyDelete(key);
        }

        public event CacheEventHandler OnInsert;
        public event CacheEventHandler OnRemove;

        private static readonly Dictionary<Type, bool> numericTypes = new Dictionary<Type, bool> {
            { typeof(byte), true},
            { typeof(sbyte), true},
            { typeof(short), true},
            { typeof(ushort), true},
            { typeof(int), true},
            { typeof(uint), true},
            { typeof(long), true},
            { typeof(ulong), true},
            { typeof(double), true},
            { typeof(float), true},
            { typeof(decimal), true}
        };

        private static byte[] ToBytes<T>(T value)
        {
            var bytesValue = value as byte[];
            if(bytesValue == null && (numericTypes.ContainsKey(typeof(T)) || !Equals(value, default(T))))
                bytesValue = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
            return bytesValue;
        }

        private T Get<T>(IDatabase db, string key)
        {
            var redisValue = db.StringGet(key);
            return typeof(T) == typeof(byte[])
                ? (T)(object)redisValue
                : redisValue != RedisValue.Null ? JsonConvert.DeserializeObject<T>(redisValue) : default(T);
        }
    }
}
