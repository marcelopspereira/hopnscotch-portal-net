using Hopnscotch.Integration.AmoCRM.Annotations;
using Hopnscotch.Integration.AmoCRM.Entities;
using Newtonsoft.Json;

namespace Hopnscotch.Portal.Integration.AmoCRM.Entities
{
    [UsedImplicitly]
    public sealed class ApiCustomFieldResponse : ApiEntityResponseBase
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("values")]
        public ApiCustomFieldValueResponse[] Values { get; set; }
    }
}