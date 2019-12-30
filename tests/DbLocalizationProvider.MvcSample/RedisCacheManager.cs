using System.Web;
using DbLocalizationProvider.Cache;
using ServiceStack.Redis;

namespace DbLocalizationProvider.MvcSample
{
    public class RedisCacheManager : ICacheManager
    {
        public string Host { get; set; }

        public void Insert(string key, object value)
        {
            using(var redisClient = new RedisClient(Host))
            {
                if(redisClient.Get<string>(key) == null)
                {
                    redisClient.Set(key, value);
                }
            }
        }

        public object Get(string key)
        {
            using(var redisClient = new RedisClient(Host))
            {
                return redisClient.Get<string>(key);

            }
        }

        public void Remove(string key)
        {
            using(var redisClient = new RedisClient(Host))
            {
                redisClient.Remove(key);

            }
        }

        public event CacheEventHandler OnInsert;
        public event CacheEventHandler OnRemove;
    }
}
