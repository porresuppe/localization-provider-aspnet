namespace DbLocalizationProvider.AspNet
{
    public class ConnectionStringHelper
    {
        public static string ConnectionString
        {
            get
            {
                var tenant = ConfigurationContext.Current.GetTenant();
                return tenant != null ? tenant.ConnectionString : ConfigurationContext.Current.DbContextConnectionString;
            }
        }
    }
}
